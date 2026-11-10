using Application.Bills;
using Application.Common.Exceptions;
using Application.Documents;
using Application.Suppliers;
using Domain.Bills;
using Domain.Common;
using Domain.Documents;
using Infrastructure.Documents;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public class SourceDocumentApprovalEntityTests
{
    private static SourceDocument New()
        => SourceDocument.Create(Guid.NewGuid(), Guid.NewGuid(), DocumentKind.Png, "bill.png", 100, new string('a', 64), 1, null);

    private static readonly DateTimeOffset Now = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Approve_LinksTheBill_AndWorksOnlyOnce()
    {
        var document = New();
        var billId = Guid.NewGuid();

        document.Approve(billId, Now);

        Assert.Equal(DocumentStatus.Approved, document.Status);
        Assert.Equal(billId, document.BillId);
        Assert.Equal(Now, document.ApprovedAt);

        Assert.Throws<DomainException>(() => document.Approve(Guid.NewGuid(), Now));
        Assert.Throws<DomainException>(() => document.Reject("Too late", Now));
        Assert.Throws<DomainException>(() => document.EnsureCanExtract());
    }

    [Fact]
    public void ARejectedDocument_CannotBeApproved_AndABillIdIsRequired()
    {
        var rejected = New();
        rejected.Reject("Not an invoice", Now);
        Assert.Throws<DomainException>(() => rejected.Approve(Guid.NewGuid(), Now));

        Assert.Throws<DomainException>(() => New().Approve(Guid.Empty, Now));
    }
}

public class ApprovalFlowTests
{
    private static readonly FixedTimeProvider Clock = PaymentTestData.Clock; // 15 January 2026

    private const string GoodJson = """
        {"supplierName":"Acme Supplies Ltd","supplierTaxId":"TIN-998","invoiceNumber":"SUP-2001",
         "invoiceDate":"2026-01-10","dueDate":"2026-02-09","currency":"NGN","subtotal":1000,"taxTotal":75,"total":1075,
         "lines":[{"description":"Goods","quantity":10,"unitPrice":100,"discountPercent":0,"taxRatePercent":7.5}]}
        """;

    // 10 x 100 with 7.5% tax = 1075.00
    private static readonly IReadOnlyList<BillLineInput> DefaultLines = [new BillLineInput("Goods", 10m, 100m, 0m, 7.5m)];

    private static ApproveDocumentCommand Command(
        Guid? supplierId, NewSupplierInput? newSupplier = null, string number = "SUP-2001",
        DateOnly? billDate = null, decimal? printedTotal = 1075m, bool confirmCurrency = false,
        IReadOnlyList<BillLineInput>? lines = null)
        => new(supplierId, newSupplier, number, billDate ?? new DateOnly(2026, 1, 10), null, "From the scan",
            lines ?? DefaultLines, printedTotal, confirmCurrency);

    private static async Task<DocumentDto> UploadAsync(Seed seed, byte fill)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new UploadDocumentHandler(
                new DocumentRepository(ctx), new DatabaseDocumentStorage(ctx),
                new FakePdfExtractor(_ => new PdfTextResult(1, "")), ctx,
                new TestCurrentOrganization(seed.OrganizationId), new TestCurrentUser(seed.UserId),
                new DocumentSettings())
            .Handle(new UploadDocumentCommand("bill.png", DocBytes.Png(fill)), CancellationToken.None);
    }

    private static async Task ExtractAsync(Seed seed, Guid documentId, string json)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        await new ExtractDocumentHandler(
                new DocumentRepository(ctx), new DatabaseDocumentStorage(ctx),
                new ScriptedExtractionModel(_ => json), new DocumentExtractionRepository(ctx),
                new ExtractionReviewBuilder(new SupplierRepository(ctx), new BillRepository(ctx)),
                new OrganizationRepository(ctx), ctx, new TestCurrentOrganization(seed.OrganizationId),
                new TestCurrentUser(seed.UserId),
                new Application.Assistant.AssistantSettings { Enabled = true, ApiKey = "test-key" }, Clock)
            .Handle(documentId, CancellationToken.None);
    }

    private static async Task<ApprovalResultDto> ApproveAsync(Seed seed, Guid documentId, ApproveDocumentCommand command)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new ApproveDocumentHandler(
                new DocumentRepository(ctx), new BillRepository(ctx), new SupplierRepository(ctx),
                new OrganizationRepository(ctx), ctx, ctx, new TestCurrentOrganization(seed.OrganizationId), Clock)
            .Handle(documentId, command, CancellationToken.None);
    }

    private static async Task<DocumentDto> GetDocumentAsync(Seed seed, Guid documentId)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new GetDocumentHandler(new DocumentRepository(ctx)).Handle(documentId, CancellationToken.None);
    }

    private static async Task<int> BillCountAsync(Seed seed)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var result = await new BillKit(ctx, seed.OrganizationId, Clock).List().Handle(
            new ListBillsQuery(null, null, null, null, null, 1, 50), CancellationToken.None);
        return result.Total;
    }

    private static async Task<int> SupplierCountAsync(Seed seed)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var result = await new ListSuppliersHandler(new SupplierRepository(ctx)).Handle(
            new ListSuppliersQuery(null, true, 1, 50), CancellationToken.None);
        return result.Total;
    }

    [Fact]
    public async Task ApprovingAReviewedDocument_CreatesADraftBill_AndLinksBothWays()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed, "Acme Supplies Limited");
        var document = await UploadAsync(seed, 41);
        await ExtractAsync(seed, document.Id, GoodJson);

        var result = await ApproveAsync(seed, document.Id, Command(supplier.Id));

        Assert.False(result.SupplierCreated);
        Assert.Equal(BillStatus.Draft, result.Bill.Status);
        Assert.Equal("SUP-2001", result.Bill.Number);
        Assert.Equal(supplier.Name, result.Bill.SupplierName);
        Assert.Equal(1075.00m, result.Bill.Total);
        Assert.Equal(new DateOnly(2026, 2, 9), result.Bill.DueDate);   // 10 Jan + the supplier's 30 days

        var read = await GetDocumentAsync(seed, document.Id);
        Assert.Equal(DocumentStatus.Approved, read.Status);
        Assert.Equal(result.Bill.Id, read.BillId);

        Assert.Single((await BillTestData.GetAsync(seed, result.Bill.Id)).Lines);

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var linked = await new GetDocumentByBillHandler(new DocumentRepository(ctx))
            .Handle(result.Bill.Id, CancellationToken.None);
        Assert.Equal(document.Id, linked.Id);
    }

    [Fact]
    public async Task ApprovingWithANewSupplier_CreatesTheSupplierAndTheBillTogether()
    {
        var seed = await PaymentTestData.SeedAsync();
        var document = await UploadAsync(seed, 42);

        var result = await ApproveAsync(seed, document.Id, Command(
            null, new NewSupplierInput("Brand New Traders", "TIN-555", "ap@brandnew.com", null, 14)));

        Assert.True(result.SupplierCreated);
        Assert.Equal("Brand New Traders", result.Bill.SupplierName);
        Assert.Equal(new DateOnly(2026, 1, 24), result.Bill.DueDate);   // 10 Jan + 14 days

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var supplier = await new BillKit(ctx, seed.OrganizationId, Clock).GetSupplier()
            .Handle(result.Bill.SupplierId, CancellationToken.None);
        Assert.Equal("TIN-555", supplier.TaxIdentificationNumber);
        Assert.Equal(14, supplier.DefaultPaymentTermsDays);
    }

    [Fact]
    public async Task ANewSupplierThatMatchesAnExistingOne_IsRefused_AndNothingIsSaved()
    {
        var seed = await PaymentTestData.SeedAsync();
        await BillTestData.SupplierAsync(seed, "Acme Supplies Limited");
        var document = await UploadAsync(seed, 43);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => ApproveAsync(seed, document.Id,
            Command(null, new NewSupplierInput("ACME SUPPLIES LTD.", null, null, null, null))));

        Assert.Contains("Acme Supplies Limited", conflict.Message);
        Assert.Equal(1, await SupplierCountAsync(seed));
        Assert.Equal(0, await BillCountAsync(seed));

        var read = await GetDocumentAsync(seed, document.Id);
        Assert.Equal(DocumentStatus.Uploaded, read.Status);
        Assert.Null(read.BillId);
    }

    [Fact]
    public async Task BothOrNeitherSupplierChoices_AreRefused()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);
        var document = await UploadAsync(seed, 44);

        await Assert.ThrowsAsync<DomainException>(() => ApproveAsync(seed, document.Id, Command(null)));
        await Assert.ThrowsAsync<DomainException>(() => ApproveAsync(seed, document.Id,
            Command(supplier.Id, new NewSupplierInput("Another", null, null, null, null))));
    }

    [Fact]
    public async Task ADuplicateBillNumber_IsRefused_AndTheDocumentStaysUnapproved()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);
        await BillTestData.BillAsync(seed, supplier.Id, "SUP-2001");
        var document = await UploadAsync(seed, 45);

        await Assert.ThrowsAsync<ConflictException>(() => ApproveAsync(seed, document.Id, Command(supplier.Id)));

        Assert.Equal(1, await BillCountAsync(seed));
        Assert.Equal(DocumentStatus.Uploaded, (await GetDocumentAsync(seed, document.Id)).Status);
    }

    [Fact]
    public async Task TheLines_MustAddUpToThePrintedTotal()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);
        var document = await UploadAsync(seed, 46);

        var error = await Assert.ThrowsAsync<DomainException>(
            () => ApproveAsync(seed, document.Id, Command(supplier.Id, printedTotal: 1000m)));
        Assert.Contains("1075.00", error.Message);

        Assert.Equal(0, await BillCountAsync(seed));

        // Without a printed total, the lines alone decide.
        var result = await ApproveAsync(seed, document.Id, Command(supplier.Id, printedTotal: null));
        Assert.Equal(1075.00m, result.Bill.Total);
    }

    [Fact]
    public async Task AFutureDateAndMissingLines_AreRefused()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);
        var document = await UploadAsync(seed, 47);

        await Assert.ThrowsAsync<DomainException>(() => ApproveAsync(seed, document.Id,
            Command(supplier.Id, billDate: new DateOnly(2026, 1, 16))));
        await Assert.ThrowsAsync<DomainException>(() => ApproveAsync(seed, document.Id,
            Command(supplier.Id, lines: [])));
    }

    [Fact]
    public async Task ADifferentCurrency_NeedsAnExplicitAcknowledgment()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);
        var document = await UploadAsync(seed, 48);
        await ExtractAsync(seed, document.Id, GoodJson.Replace("\"currency\":\"NGN\"", "\"currency\":\"USD\""));

        var error = await Assert.ThrowsAsync<DomainException>(
            () => ApproveAsync(seed, document.Id, Command(supplier.Id)));
        Assert.Contains("USD", error.Message);
        Assert.Equal(0, await BillCountAsync(seed));

        var result = await ApproveAsync(seed, document.Id, Command(supplier.Id, confirmCurrency: true));
        Assert.Equal("NGN", result.Bill.CurrencyCode);
    }

    [Fact]
    public async Task ADocumentCanOnlyBeApprovedOnce_AndThenNeitherRejectedNorExtracted()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);
        var document = await UploadAsync(seed, 49);
        await ApproveAsync(seed, document.Id, Command(supplier.Id));

        await Assert.ThrowsAsync<DomainException>(
            () => ApproveAsync(seed, document.Id, Command(supplier.Id, number: "SUP-2002")));
        Assert.Equal(1, await BillCountAsync(seed));

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        await Assert.ThrowsAsync<DomainException>(() => new RejectDocumentHandler(
                new DocumentRepository(ctx), new DatabaseDocumentStorage(ctx), ctx, Clock)
            .Handle(document.Id, new RejectDocumentCommand("Too late"), CancellationToken.None));

        await Assert.ThrowsAsync<DomainException>(() => ExtractAsync(seed, document.Id, GoodJson));
    }

    [Fact]
    public async Task ADocumentCanBeApprovedByHand_WithoutAnyExtraction()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);
        var document = await UploadAsync(seed, 50);

        var result = await ApproveAsync(seed, document.Id, Command(supplier.Id));

        Assert.Equal(BillStatus.Draft, result.Bill.Status);
        Assert.Equal(DocumentStatus.Approved, (await GetDocumentAsync(seed, document.Id)).Status);
    }

    [Fact]
    public async Task TwoSimultaneousApprovals_OnlyOneBillIsCreated()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);
        var document = await UploadAsync(seed, 51);

        async Task<bool> TryApproveAsync(string number)
        {
            try
            {
                await ApproveAsync(seed, document.Id, Command(supplier.Id, number: number));
                return true;
            }
            catch (DomainException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(TryApproveAsync("SUP-A"), TryApproveAsync("SUP-B"));

        Assert.Single(results, succeeded => succeeded);
        Assert.Equal(1, await BillCountAsync(seed));
    }

    [Fact]
    public async Task AnotherOrganization_CannotApproveOrUseTheseSuppliers()
    {
        var seedA = await PaymentTestData.SeedAsync();
        var seedB = await PaymentTestData.SeedAsync();
        var supplierA = await BillTestData.SupplierAsync(seedA);
        var supplierB = await BillTestData.SupplierAsync(seedB);
        var documentA = await UploadAsync(seedA, 52);
        var documentB = await UploadAsync(seedB, 53);

        await Assert.ThrowsAsync<NotFoundException>(
            () => ApproveAsync(seedB, documentA.Id, Command(supplierB.Id)));

        await Assert.ThrowsAsync<NotFoundException>(
            () => ApproveAsync(seedB, documentB.Id, Command(supplierA.Id)));

        Assert.Equal(0, await BillCountAsync(seedA));
        Assert.Equal(0, await BillCountAsync(seedB));
        Assert.Equal(DocumentStatus.Uploaded, (await GetDocumentAsync(seedB, documentB.Id)).Status);
    }

    [Fact]
    public async Task ABillEnteredByHand_HasNoLinkedDocument()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);
        var bill = await BillTestData.BillAsync(seed, supplier.Id);

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        await Assert.ThrowsAsync<NotFoundException>(() => new GetDocumentByBillHandler(new DocumentRepository(ctx))
            .Handle(bill.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ApprovalIsAudited_WithTheBillItCreated()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);
        var document = await UploadAsync(seed, 54);

        var result = await ApproveAsync(seed, document.Id, Command(supplier.Id));

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var trail = await ctx.AuditEntries.ToListAsync();

        Assert.Contains(trail, a =>
            a.Action == "Document.Approved" && a.EntityId == document.Id &&
            a.Details!.Contains(result.Bill.Id.ToString()));
        Assert.Contains(trail, a => a.Action == "Bill.Created" && a.EntityId == result.Bill.Id);
    }
}
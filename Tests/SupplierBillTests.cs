using Application.Bills;
using Application.Common.Exceptions;
using Application.Suppliers;
using Domain.Bills;
using Domain.Common;
using Domain.Invoices;
using Domain.Organizations;
using Domain.Suppliers;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public class SupplierEntityTests
{
    [Fact]
    public void Create_WithBlankName_Throws()
        => Assert.Throws<DomainException>(() => Supplier.Create(Guid.NewGuid(), "  "));

    [Fact]
    public void Update_WithInvalidEmailOrPaymentTerms_Throws()
    {
        var supplier = Supplier.Create(Guid.NewGuid(), "Acme Supplies");

        Assert.Throws<DomainException>(() =>
            supplier.Update("Acme", "not-an-email", null, null, null, null, null, null, null, null, null, 30));
        Assert.Throws<DomainException>(() =>
            supplier.Update("Acme", null, null, null, null, null, null, null, null, null, null, 400));
    }
}

public class SupplierBillEntityTests
{
    private static SupplierBill Draft(string number = "SUP-001")
        => SupplierBill.CreateDraft(
            Guid.NewGuid(), Guid.NewGuid(), "ngn", number, new DateOnly(2026, 1, 10), new DateOnly(2026, 2, 9), null);

    // 10 x 100 with 7.5% tax: subtotal 1000.00, tax 75.00, total 1075.00
    private static SupplierBill Open()
    {
        var bill = Draft();
        bill.ReplaceLines([new LineInput("Goods", 10m, 100m, 0m, 7.5m)]);
        bill.Finalize(new DateTimeOffset(2026, 1, 11, 9, 0, 0, TimeSpan.Zero));
        return bill;
    }

    [Fact]
    public void Totals_AreTheSumOfTheCalculatedLines()
    {
        var bill = Draft();
        bill.ReplaceLines(
        [
            new LineInput("Widget", 3m, 19.99m, 10m, 7.5m),
            new LineInput("Service", 2m, 100m, 0m, 7.5m)
        ]);

        Assert.Equal("NGN", bill.CurrencyCode);
        Assert.Equal(259.97m, bill.Subtotal);
        Assert.Equal(6.00m, bill.DiscountTotal);
        Assert.Equal(19.05m, bill.TaxTotal);
        Assert.Equal(273.02m, bill.Total);
        Assert.Equal(bill.Total, bill.Balance);
    }

    [Fact]
    public void NumberKey_IgnoresCaseAndSpaces()
    {
        Assert.Equal(SupplierBill.KeyFor("inv 001"), SupplierBill.KeyFor("  INV001 "));
        Assert.Equal("SUP-001", Draft(" sup-001 ").NumberKey);
        Assert.Throws<DomainException>(() => SupplierBill.KeyFor("   "));
    }

    [Fact]
    public void CreateDraft_RejectsADueDateBeforeTheBillDate()
        => Assert.Throws<DomainException>(() => SupplierBill.CreateDraft(
            Guid.NewGuid(), Guid.NewGuid(), "NGN", "SUP-001", new DateOnly(2026, 2, 9), new DateOnly(2026, 1, 10), null));

    [Fact]
    public void Finalize_NeedsLines_AndMakesTheBillOpen()
    {
        var empty = Draft();
        Assert.Throws<DomainException>(() => empty.Finalize(DateTimeOffset.UtcNow));

        var open = Open();
        Assert.Equal(BillStatus.Open, open.Status);
        Assert.NotNull(open.FinalizedAt);
    }

    [Fact]
    public void AFinalizedBill_CannotBeEdited()
    {
        var bill = Open();

        Assert.Throws<DomainException>(() => bill.ReplaceLines([new LineInput("Other", 1m, 1m)]));
        Assert.Throws<DomainException>(() => bill.UpdateDetails(
            Guid.NewGuid(), "SUP-002", new DateOnly(2026, 1, 10), new DateOnly(2026, 2, 9), null));
    }

    [Fact]
    public void Payments_PartialThenFull_AndReversal_UpdateStatusAndBalance()
    {
        var bill = Open();

        bill.ApplyPayment(500.00m);
        Assert.Equal(BillStatus.PartiallyPaid, bill.Status);
        Assert.Equal(575.00m, bill.Balance);

        Assert.Throws<DomainException>(() => bill.ApplyPayment(575.01m));

        bill.ApplyPayment(575.00m);
        Assert.Equal(BillStatus.Paid, bill.Status);

        bill.ReversePayment(75.00m);
        Assert.Equal(BillStatus.PartiallyPaid, bill.Status);
        Assert.Equal(1000.00m, bill.AmountPaid);

        bill.ReversePayment(1000.00m);
        Assert.Equal(BillStatus.Open, bill.Status);
    }

    [Fact]
    public void Void_WorksForDraftsAndUnpaidOpenBills_ButNotForPaidOnes()
    {
        var now = new DateTimeOffset(2026, 1, 20, 9, 0, 0, TimeSpan.Zero);

        var draft = Draft();
        draft.Void("Entered in error", now);
        Assert.Equal(BillStatus.Void, draft.Status);

        var open = Open();
        Assert.Throws<DomainException>(() => open.Void("  ", now));
        open.Void("Supplier cancelled it", now);
        Assert.Equal(BillStatus.Void, open.Status);

        var paid = Open();
        paid.ApplyPayment(100m);
        Assert.Throws<DomainException>(() => paid.Void("Too late", now));
    }

    [Fact]
    public void Overdue_IsDerivedFromTheDueDateAndStatus()
    {
        var bill = Open(); // due 9 Feb 2026

        Assert.False(bill.IsOverdue(new DateOnly(2026, 2, 9)));
        Assert.True(bill.IsOverdue(new DateOnly(2026, 2, 10)));
        Assert.Equal(1, bill.DaysOverdue(new DateOnly(2026, 2, 10)));

        bill.ApplyPayment(bill.Total);
        Assert.False(bill.IsOverdue(new DateOnly(2026, 3, 1)));
    }
}

public class PayablesPermissionTests
{
    [Fact]
    public void Staff_CannotSeeOrManagePayables()
    {
        Assert.False(RolePermissions.Has(OrganizationRole.Staff, Permission.ViewPayables));
        Assert.False(RolePermissions.Has(OrganizationRole.Staff, Permission.ManagePayables));
    }

    [Fact]
    public void AccountantAndAbove_CanSeeAndManagePayables()
    {
        foreach (var role in new[] { OrganizationRole.Accountant, OrganizationRole.Admin, OrganizationRole.Owner })
        {
            Assert.True(RolePermissions.Has(role, Permission.ViewPayables));
            Assert.True(RolePermissions.Has(role, Permission.ManagePayables));
        }
    }
}

internal sealed class BillKit(AppDbContext ctx, Guid organizationId, TimeProvider clock)
{
    private readonly TestCurrentOrganization _organization = new(organizationId);

    public CreateSupplierHandler CreateSupplier() => new(new SupplierRepository(ctx), ctx, _organization);
    public ArchiveSupplierHandler ArchiveSupplier() => new(new SupplierRepository(ctx), ctx);
    public GetSupplierHandler GetSupplier() => new(new SupplierRepository(ctx));

    public CreateBillHandler Create() => new(
        new BillRepository(ctx), new SupplierRepository(ctx), new OrganizationRepository(ctx),
        ctx, _organization, clock);

    public UpdateBillHandler Update() => new(
        new BillRepository(ctx), new SupplierRepository(ctx), new OrganizationRepository(ctx),
        ctx, _organization, clock);

    public FinalizeBillHandler Finalize() => new(
        new BillRepository(ctx), new SupplierRepository(ctx), new OrganizationRepository(ctx),
        ctx, _organization, clock);

    public VoidBillHandler Void() => new(
        new BillRepository(ctx), new SupplierRepository(ctx), new OrganizationRepository(ctx),
        ctx, _organization, clock);

    public GetBillHandler Get() => new(
        new BillRepository(ctx), new SupplierRepository(ctx), new OrganizationRepository(ctx),
        _organization, clock);

    public ListBillsHandler List() => new(
        new BillRepository(ctx), new OrganizationRepository(ctx), _organization, clock);
}

internal static class BillTestData
{
    public static readonly FixedTimeProvider Clock = PaymentTestData.Clock; // 15 January 2026

    // 10 x 100.00 with 7.5% tax = 1075.00
    public static IReadOnlyList<BillLineInput> Lines { get; } = [new BillLineInput("Goods", 10m, 100m, 0m, 7.5m)];

    public static async Task<SupplierDto> SupplierAsync(
        Seed seed, string? name = null, int? terms = null, bool archived = false)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var kit = new BillKit(ctx, seed.OrganizationId, Clock);

        var supplier = await kit.CreateSupplier().Handle(
            new SupplierInput(name ?? $"Supplier {Guid.NewGuid()}", null, null, null, null, null, null, null, null, null, null, terms),
            CancellationToken.None);

        if (archived)
            await kit.ArchiveSupplier().Handle(supplier.Id, true, CancellationToken.None);

        return supplier;
    }

    public static async Task<BillDto> BillAsync(
        Seed seed, Guid supplierId, string number = "SUP-001", DateOnly? billDate = null,
        DateOnly? dueDate = null, IReadOnlyList<BillLineInput>? lines = null)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new BillKit(ctx, seed.OrganizationId, Clock).Create().Handle(
            new BillInput(supplierId, number, billDate ?? new DateOnly(2026, 1, 10), dueDate, "Notes", lines ?? Lines),
            CancellationToken.None);
    }

    public static async Task<BillDto> FinalizeAsync(
        Seed seed, Guid billId, bool confirm = false, TimeProvider? clock = null)
    {
        clock ??= Clock;
        await using var ctx = TestDatabase.CreateContext(clock, seed.OrganizationId, seed.UserId);
        return await new BillKit(ctx, seed.OrganizationId, clock).Finalize().Handle(
            billId, new FinalizeBillCommand(confirm), CancellationToken.None);
    }

    public static async Task<BillDto> VoidAsync(Seed seed, Guid billId, string reason = "Entered in error")
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new BillKit(ctx, seed.OrganizationId, Clock).Void().Handle(
            billId, new VoidBillCommand(reason), CancellationToken.None);
    }

    public static async Task<BillDto> GetAsync(Seed seed, Guid billId)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new BillKit(ctx, seed.OrganizationId, Clock).Get().Handle(billId, CancellationToken.None);
    }
}

public class BillFlowTests
{
    private static readonly FixedTimeProvider Clock = BillTestData.Clock;

    [Fact]
    public async Task CreatingADraft_SavesLinesAndTotals_AndReadsBackExactly()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);

        var created = await BillTestData.BillAsync(seed, supplier.Id);
        var read = await BillTestData.GetAsync(seed, created.Id);

        Assert.Equal(BillStatus.Draft, read.Status);
        Assert.Equal("SUP-001", read.Number);
        Assert.Equal("NGN", read.CurrencyCode);
        Assert.Equal(supplier.Name, read.SupplierName);
        Assert.Equal(1000.00m, read.Subtotal);
        Assert.Equal(75.00m, read.TaxTotal);
        Assert.Equal(1075.00m, read.Total);
        Assert.Equal(1075.00m, read.Balance);
        Assert.Single(read.Lines);
    }

    [Fact]
    public async Task TheDueDate_DefaultsToTheSuppliersPaymentTerms_UnlessGiven()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed, terms: 14);

        var defaulted = await BillTestData.BillAsync(seed, supplier.Id, "SUP-001");
        var explicitDue = await BillTestData.BillAsync(
            seed, supplier.Id, "SUP-002", dueDate: new DateOnly(2026, 3, 1));

        Assert.Equal(new DateOnly(2026, 1, 24), defaulted.DueDate);   // 10 Jan + 14 days
        Assert.Equal(new DateOnly(2026, 3, 1), explicitDue.DueDate);
    }

    [Fact]
    public async Task ADuplicateNumber_IsRejectedForTheSameSupplier_ButNotForAnotherOne()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplierA = await BillTestData.SupplierAsync(seed);
        var supplierB = await BillTestData.SupplierAsync(seed);
        await BillTestData.BillAsync(seed, supplierA.Id, "INV-77");

        await Assert.ThrowsAsync<ConflictException>(
            () => BillTestData.BillAsync(seed, supplierA.Id, " inv-77 ", lines: [new BillLineInput("Other", 1m, 5m)]));

        await BillTestData.BillAsync(seed, supplierB.Id, "INV-77");
    }

    [Fact]
    public async Task VoidingABill_FreesItsNumberForReentry()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);
        var first = await BillTestData.BillAsync(seed, supplier.Id, "INV-77");

        await BillTestData.VoidAsync(seed, first.Id);

        var again = await BillTestData.BillAsync(seed, supplier.Id, "INV-77");
        Assert.Equal(BillStatus.Draft, again.Status);
    }

    [Fact]
    public async Task APossibleDuplicate_IsFlagged_AndNeedsConfirmationToFinalize()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);

        var original = await BillTestData.BillAsync(seed, supplier.Id, "INV-100");
        await BillTestData.FinalizeAsync(seed, original.Id);

        // Different number, same supplier, same date, same total.
        var suspect = await BillTestData.BillAsync(seed, supplier.Id, "INV-100-COPY");

        var flagged = Assert.Single(suspect.PossibleDuplicates);
        Assert.Equal("INV-100", flagged.Number);

        await Assert.ThrowsAsync<ConflictException>(() => BillTestData.FinalizeAsync(seed, suspect.Id));

        var finalized = await BillTestData.FinalizeAsync(seed, suspect.Id, confirm: true);
        Assert.Equal(BillStatus.Open, finalized.Status);
    }

    [Fact]
    public async Task FinalizingABill_MakesItOpen_AndItCanNoLongerBeUpdated()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);
        var draft = await BillTestData.BillAsync(seed, supplier.Id);

        var finalized = await BillTestData.FinalizeAsync(seed, draft.Id);
        Assert.Equal(BillStatus.Open, finalized.Status);

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        await Assert.ThrowsAsync<DomainException>(() => new BillKit(ctx, seed.OrganizationId, Clock).Update().Handle(
            draft.Id,
            new BillInput(supplier.Id, "SUP-001", new DateOnly(2026, 1, 10), null, null, BillTestData.Lines),
            CancellationToken.None));
    }

    [Fact]
    public async Task UpdatingADraft_ReplacesTheLinesAndDetails()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);
        var draft = await BillTestData.BillAsync(seed, supplier.Id);

        await using (var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId))
        {
            await new BillKit(ctx, seed.OrganizationId, Clock).Update().Handle(
                draft.Id,
                new BillInput(supplier.Id, "SUP-001-REV", new DateOnly(2026, 1, 12), null, "Corrected",
                    [new BillLineInput("Only item", 1m, 50m)]),
                CancellationToken.None);
        }

        var read = await BillTestData.GetAsync(seed, draft.Id);
        Assert.Equal("SUP-001-REV", read.Number);
        Assert.Equal(new DateOnly(2026, 1, 12), read.BillDate);
        Assert.Equal(50.00m, read.Total);
        Assert.Single(read.Lines);
        Assert.Equal("Corrected", read.Notes);
    }

    [Fact]
    public async Task ABillDatedInTheFuture_IsRejected()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);

        await Assert.ThrowsAsync<DomainException>(
            () => BillTestData.BillAsync(seed, supplier.Id, billDate: new DateOnly(2026, 1, 16)));
    }

    [Fact]
    public async Task AnArchivedSupplier_CannotReceiveBills()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed, archived: true);

        await Assert.ThrowsAsync<DomainException>(() => BillTestData.BillAsync(seed, supplier.Id));
    }

    [Fact]
    public async Task AnotherOrganization_CannotSeeOrUseTheSuppliersAndBills()
    {
        var seedA = await PaymentTestData.SeedAsync();
        var seedB = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seedA);
        var bill = await BillTestData.BillAsync(seedA, supplier.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => BillTestData.GetAsync(seedB, bill.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => BillTestData.BillAsync(seedB, supplier.Id));

        await using var ctx = TestDatabase.CreateContext(Clock, seedB.OrganizationId, seedB.UserId);
        var kit = new BillKit(ctx, seedB.OrganizationId, Clock);

        await Assert.ThrowsAsync<NotFoundException>(() => kit.GetSupplier().Handle(supplier.Id, CancellationToken.None));

        var listed = await kit.List().Handle(new ListBillsQuery(null, null, null, null, null, 1, 100), CancellationToken.None);
        Assert.DoesNotContain(listed.Items, b => b.Id == bill.Id);
    }

    [Fact]
    public async Task TheOverdueAndDueByFilters_FollowTheDueDate()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed, terms: 30);
        var draft = await BillTestData.BillAsync(seed, supplier.Id); // due 9 Feb 2026
        var open = await BillTestData.FinalizeAsync(seed, draft.Id);

        var later = new FixedTimeProvider(Clock.GetUtcNow().AddDays(60)); // 16 March 2026

        async Task<IReadOnlyList<BillSummaryDto>> ListAsync(TimeProvider clock, bool? overdue, DateOnly? dueBy)
        {
            await using var ctx = TestDatabase.CreateContext(clock, seed.OrganizationId, seed.UserId);
            var result = await new BillKit(ctx, seed.OrganizationId, clock).List().Handle(
                new ListBillsQuery(null, null, null, overdue, dueBy, 1, 50), CancellationToken.None);
            return result.Items;
        }

        Assert.DoesNotContain(await ListAsync(Clock, true, null), b => b.Id == open.Id);
        Assert.Contains(await ListAsync(later, true, null), b => b.Id == open.Id && b.IsOverdue);

        Assert.Contains(await ListAsync(Clock, null, new DateOnly(2026, 2, 9)), b => b.Id == open.Id);
        Assert.DoesNotContain(await ListAsync(Clock, null, new DateOnly(2026, 2, 8)), b => b.Id == open.Id);
    }

    [Fact]
    public async Task SupplierAndBillChanges_AreAudited()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);
        var draft = await BillTestData.BillAsync(seed, supplier.Id);
        await BillTestData.FinalizeAsync(seed, draft.Id);

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var trail = await ctx.AuditEntries.ToListAsync();

        Assert.Contains(trail, a => a.Action == "Supplier.Created" && a.EntityId == supplier.Id);
        Assert.Contains(trail, a => a.Action == "Bill.Created" && a.EntityId == draft.Id);
        Assert.Contains(trail, a =>
            a.Action == "Bill.StatusChanged" && a.EntityId == draft.Id && a.Details == "Draft -> Open (SUP-001)");
    }
}
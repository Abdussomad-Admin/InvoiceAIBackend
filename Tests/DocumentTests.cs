using System.Security.Cryptography;
using System.Text;
using Application.Common.Exceptions;
using Application.Documents;
using Domain.Common;
using Domain.Documents;
using Infrastructure.Documents;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using DocumentSettings = Application.Documents.DocumentSettings;
namespace Tests;

internal sealed class FakePdfExtractor(Func<byte[], PdfTextResult> extract) : IPdfTextExtractor
{
    public PdfTextResult Extract(byte[] content) => extract(content);
}

internal static class DocBytes
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // Different "fill" values make different files, so they get different hashes.
    public static byte[] Png(byte fill = 1, int extra = 0)
        => [.. PngSignature, .. Enumerable.Repeat(fill, 32 + extra)];

    public static byte[] Pdf(string marker = "a") => Encoding.ASCII.GetBytes("%PDF-1.4 " + marker);
}

public class DocumentRulesTests
{
    [Fact]
    public void Detect_UsesTheFilesOwnBytes_NotItsNameOrClaimedType()
    {
        Assert.Equal(DocumentKind.Pdf, DocumentRules.Detect("%PDF-1.7 hello"u8));
        Assert.Equal(DocumentKind.Jpeg, DocumentRules.Detect(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 }));
        Assert.Equal(DocumentKind.Png, DocumentRules.Detect(DocBytes.Png()));

        Assert.Null(DocumentRules.Detect("MZ this is a program"u8));
        Assert.Null(DocumentRules.Detect("PK zip file"u8));
        Assert.Null(DocumentRules.Detect("%PDF"u8));                 // too short to be a header
        Assert.Null(DocumentRules.Detect("  %PDF-1.4 late"u8));      // header must be at byte zero
        Assert.Null(DocumentRules.Detect(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void SafeFileName_RemovesFoldersAndOddCharacters()
    {
        Assert.Equal("passwd", DocumentRules.SafeFileName("../../etc/passwd"));
        Assert.Equal("inv 01.pdf", DocumentRules.SafeFileName(@"C:\temp\inv 01.pdf"));
        Assert.Equal("badname.pdf", DocumentRules.SafeFileName("bad\u0001name<>.pdf"));
        Assert.Equal("document", DocumentRules.SafeFileName("   "));
        Assert.Equal("document", DocumentRules.SafeFileName(null));
        Assert.Equal(200, DocumentRules.SafeFileName(new string('a', 300) + ".pdf").Length);
    }
}

public class SourceDocumentEntityTests
{
    private static readonly string Hash = new('a', 64);

    private static SourceDocument New(
        DocumentKind kind = DocumentKind.Pdf, long size = 1000, string? hash = null, int pages = 1, string? text = null)
        => SourceDocument.Create(Guid.NewGuid(), Guid.NewGuid(), kind, "inv.pdf", size, hash ?? Hash, pages, text);

    [Fact]
    public void Create_WithInvalidValues_Throws()
    {
        Assert.Throws<DomainException>(() => New(size: 0));
        Assert.Throws<DomainException>(() => New(size: DocumentRules.MaxBytes + 1));
        Assert.Throws<DomainException>(() => New(hash: "not-a-hash"));
        Assert.Throws<DomainException>(() => New(hash: new string('A', 64)));   // must be lowercase hex
        Assert.Throws<DomainException>(() => New(pages: 0));
        Assert.Throws<DomainException>(() => New(pages: DocumentRules.MaxPdfPages + 1));
        Assert.Throws<DomainException>(() => New(kind: (DocumentKind)99));
        Assert.Throws<DomainException>(() => SourceDocument.Create(
            Guid.Empty, Guid.NewGuid(), DocumentKind.Pdf, "a.pdf", 10, Hash, 1, null));
    }

    [Fact]
    public void ATextLayer_NeedsEnoughReadableCharacters_AndOnlyAppliesToPdfs()
    {
        Assert.True(New(text: new string('x', 60)).HasTextLayer);
        Assert.False(New(text: "   \n  short ").HasTextLayer);
        Assert.False(New(text: null).HasTextLayer);
        Assert.False(New(kind: DocumentKind.Png, text: new string('x', 60)).HasTextLayer);
    }

    [Fact]
    public void Create_CleansTheFileName_AndCapsTheText()
    {
        var document = SourceDocument.Create(
            Guid.NewGuid(), Guid.NewGuid(), DocumentKind.Pdf, "../x/inv.pdf", 10, Hash, 1,
            new string('x', DocumentRules.MaxExtractedChars + 500));

        Assert.Equal("inv.pdf", document.FileName);
        Assert.Equal(DocumentRules.MaxExtractedChars, document.ExtractedText!.Length);
        Assert.Equal(DocumentStatus.Uploaded, document.Status);
    }

    [Fact]
    public void Reject_NeedsAReason_AndWorksOnlyOnce()
    {
        var document = New();
        var now = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

        Assert.Throws<DomainException>(() => document.Reject("  ", now));

        document.Reject("Not an invoice", now);
        Assert.Equal(DocumentStatus.Rejected, document.Status);
        Assert.Equal("Not an invoice", document.RejectionReason);

        Assert.Throws<DomainException>(() => document.Reject("Again", now));
    }
}

public class DocumentSettingsTests
{
    [Fact]
    public void TheStorageLimit_MustBeSensible()
    {
        new DocumentSettings().Validate();
        Assert.Throws<InvalidOperationException>(() => new DocumentSettings { MaxStoredMegabytes = 0 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new DocumentSettings { MaxStoredMegabytes = 9999 }.Validate());
    }
}

public class PdfPigTextExtractorTests
{
    private static byte[] MakePdf(params string[] pages)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(document =>
        {
            foreach (var text in pages)
            {
                document.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.Content().Text(text);
                });
            }
        }).GeneratePdf();
    }

    [Fact]
    public void Extract_ReadsTheTextAndThePageCount()
    {
        var result = new PdfPigTextExtractor().Extract(MakePdf("Invoice SUP-001 total 1075.00", "Second page note"));

        Assert.Equal(2, result.PageCount);
        Assert.Contains("SUP-001", result.Text);
        Assert.Contains("1075.00", result.Text);
        Assert.Contains("Second page note", result.Text);
    }

    [Fact]
    public void Extract_RefusesDamagedFiles()
        => Assert.Throws<DomainException>(
            () => new PdfPigTextExtractor().Extract(Encoding.ASCII.GetBytes("%PDF-1.4 this is not really a pdf")));

    [Fact]
    public void Extract_RefusesTooManyPages()
        => Assert.Throws<DomainException>(
            () => new PdfPigTextExtractor().Extract(
                MakePdf(Enumerable.Repeat("page", DocumentRules.MaxPdfPages + 1).ToArray())));
}

public class DocumentFlowTests
{
    private static readonly FixedTimeProvider Clock = PaymentTestData.Clock;

    private static async Task<DocumentDto> UploadAsync(
        Seed seed, byte[] content, string name = "inv.png", IPdfTextExtractor? pdf = null, DocumentSettings? settings = null)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var handler = new UploadDocumentHandler(
            new DocumentRepository(ctx), new DatabaseDocumentStorage(ctx),
            pdf ?? new FakePdfExtractor(_ => new PdfTextResult(1, "")), ctx,
            new TestCurrentOrganization(seed.OrganizationId), new TestCurrentUser(seed.UserId),
            settings ?? new DocumentSettings());

        return await handler.Handle(new UploadDocumentCommand(name, content), CancellationToken.None);
    }

    private static async Task<DocumentFile> DownloadAsync(Seed seed, Guid id)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new DownloadDocumentHandler(new DocumentRepository(ctx), new DatabaseDocumentStorage(ctx))
            .Handle(id, CancellationToken.None);
    }

    private static async Task<DocumentDto> RejectAsync(Seed seed, Guid id, string reason = "Not an invoice")
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new RejectDocumentHandler(
                new DocumentRepository(ctx), new DatabaseDocumentStorage(ctx), ctx, Clock)
            .Handle(id, new RejectDocumentCommand(reason), CancellationToken.None);
    }

    private static async Task<DocumentDto> GetAsync(Seed seed, Guid id)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new GetDocumentHandler(new DocumentRepository(ctx)).Handle(id, CancellationToken.None);
    }

    private static async Task<Application.Common.PagedResult<DocumentSummaryDto>> ListAsync(
        Seed seed, DocumentStatus? status = null, int page = 1, int pageSize = 50)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new ListDocumentsHandler(new DocumentRepository(ctx))
            .Handle(new ListDocumentsQuery(status, page, pageSize), CancellationToken.None);
    }

    [Fact]
    public async Task UploadingAnImage_StoresIt_AndItDownloadsUnchanged()
    {
        var seed = await PaymentTestData.SeedAsync();
        var bytes = DocBytes.Png();

        var document = await UploadAsync(seed, bytes, "../../my invoice.png");

        Assert.Equal("my invoice.png", document.FileName);
        Assert.Equal(DocumentKind.Png, document.Kind);
        Assert.Equal("image/png", document.ContentType);
        Assert.Equal(bytes.Length, document.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), document.Sha256);
        Assert.Equal(1, document.PageCount);
        Assert.Equal(DocumentStatus.Uploaded, document.Status);
        Assert.Null(document.Advice);

        var file = await DownloadAsync(seed, document.Id);
        Assert.Equal(bytes, file.Content);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal("my invoice.png", file.FileName);
    }

    [Fact]
    public async Task UploadingATextPdf_RecordsPagesAndTheTextLayer()
    {
        var seed = await PaymentTestData.SeedAsync();
        var extractor = new FakePdfExtractor(_ => new PdfTextResult(2, new string('a', 120) + " Invoice SUP-001"));

        var document = await UploadAsync(seed, DocBytes.Pdf("text"), "bill.pdf", extractor);

        Assert.Equal(DocumentKind.Pdf, document.Kind);
        Assert.Equal(2, document.PageCount);
        Assert.True(document.HasTextLayer);
        Assert.Contains("SUP-001", document.TextPreview);
        Assert.Null(document.Advice);
    }

    [Fact]
    public async Task AScannedPdf_IsAccepted_ButFlaggedWithAdvice()
    {
        var seed = await PaymentTestData.SeedAsync();
        var extractor = new FakePdfExtractor(_ => new PdfTextResult(1, "   "));

        var document = await UploadAsync(seed, DocBytes.Pdf("scan"), "scan.pdf", extractor);

        Assert.False(document.HasTextLayer);
        Assert.Contains("image", document.Advice);
    }

    [Fact]
    public async Task UnsupportedEmptyAndOversizedFiles_AreRefused_AndNothingIsStored()
    {
        var seed = await PaymentTestData.SeedAsync();

        await Assert.ThrowsAsync<DomainException>(() => UploadAsync(seed, "PK\u0003\u0004 not allowed"u8.ToArray(), "x.pdf"));
        await Assert.ThrowsAsync<DomainException>(() => UploadAsync(seed, []));
        await Assert.ThrowsAsync<DomainException>(() => UploadAsync(seed, DocBytes.Png(extra: DocumentRules.MaxBytes)));

        Assert.Empty((await ListAsync(seed)).Items);
    }

    [Fact]
    public async Task AnUnreadablePdf_IsRefused_AndNothingIsStored()
    {
        var seed = await PaymentTestData.SeedAsync();
        var extractor = new FakePdfExtractor(_ => throw new DomainException("This PDF could not be read. It may be damaged."));

        await Assert.ThrowsAsync<DomainException>(() => UploadAsync(seed, DocBytes.Pdf("bad"), "bad.pdf", extractor));

        Assert.Empty((await ListAsync(seed)).Items);
    }

    [Fact]
    public async Task TheSameFileTwice_IsRefused_UntilTheFirstIsRejected()
    {
        var seed = await PaymentTestData.SeedAsync();
        var bytes = DocBytes.Png(fill: 7);

        var first = await UploadAsync(seed, bytes, "first.png");

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => UploadAsync(seed, bytes, "again.png"));
        Assert.Contains("first.png", conflict.Message);

        await RejectAsync(seed, first.Id);

        var second = await UploadAsync(seed, bytes, "again.png");
        Assert.Equal(DocumentStatus.Uploaded, second.Status);
    }

    [Fact]
    public async Task TheStorageLimit_IsEnforcedPerOrganization()
    {
        var seed = await PaymentTestData.SeedAsync();
        var settings = new DocumentSettings { MaxStoredMegabytes = 1 };

        await UploadAsync(seed, DocBytes.Png(fill: 1, extra: 600 * 1024), settings: settings);

        await Assert.ThrowsAsync<DomainException>(
            () => UploadAsync(seed, DocBytes.Png(fill: 2, extra: 600 * 1024), settings: settings));
    }

    [Fact]
    public async Task RejectingADocument_NeedsAReason_FreesTheFile_AndKeepsTheRecord()
    {
        var seed = await PaymentTestData.SeedAsync();
        var document = await UploadAsync(seed, DocBytes.Png(fill: 3));

        await Assert.ThrowsAsync<DomainException>(() => RejectAsync(seed, document.Id, "  "));

        var rejected = await RejectAsync(seed, document.Id, "Not an invoice");
        Assert.Equal(DocumentStatus.Rejected, rejected.Status);

        var read = await GetAsync(seed, document.Id);
        Assert.Equal("Not an invoice", read.RejectionReason);

        await Assert.ThrowsAsync<NotFoundException>(() => DownloadAsync(seed, document.Id));

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        Assert.False(await ctx.DocumentBlobs.AnyAsync(b => b.DocumentId == document.Id));

        await Assert.ThrowsAsync<DomainException>(() => RejectAsync(seed, document.Id, "Again"));
    }

    [Fact]
    public async Task AnotherOrganization_CannotSeeDownloadOrRejectDocuments()
    {
        var seedA = await PaymentTestData.SeedAsync();
        var seedB = await PaymentTestData.SeedAsync();
        var document = await UploadAsync(seedA, DocBytes.Png(fill: 4));

        await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(seedB, document.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => DownloadAsync(seedB, document.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => RejectAsync(seedB, document.Id));
        Assert.DoesNotContain((await ListAsync(seedB)).Items, d => d.Id == document.Id);

        // The same file can be uploaded by the other organization: hashes only clash within one.
        await UploadAsync(seedB, DocBytes.Png(fill: 4));
    }

    [Fact]
    public async Task TheList_FiltersByStatus_AndPages()
    {
        var seed = await PaymentTestData.SeedAsync();
        var first = await UploadAsync(seed, DocBytes.Png(fill: 11), "a.png");
        await UploadAsync(seed, DocBytes.Png(fill: 12), "b.png");
        await UploadAsync(seed, DocBytes.Png(fill: 13), "c.png");
        await RejectAsync(seed, first.Id);

        var waiting = await ListAsync(seed, DocumentStatus.Uploaded);
        Assert.Equal(2, waiting.Total);
        Assert.DoesNotContain(waiting.Items, d => d.Id == first.Id);

        var rejected = await ListAsync(seed, DocumentStatus.Rejected);
        Assert.Equal(first.Id, Assert.Single(rejected.Items).Id);

        var paged = await ListAsync(seed, page: 2, pageSize: 2);
        Assert.Equal(3, paged.Total);
        Assert.Single(paged.Items);
    }

    [Fact]
    public async Task UploadsAndRejections_AreAudited()
    {
        var seed = await PaymentTestData.SeedAsync();
        var document = await UploadAsync(seed, DocBytes.Png(fill: 5), "audited.png");
        await RejectAsync(seed, document.Id, "Duplicate of an earlier bill");

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var trail = await ctx.AuditEntries.Where(a => a.EntityId == document.Id).ToListAsync();

        Assert.Contains(trail, a => a.Action == "Document.Uploaded" && a.Details!.StartsWith("audited.png"));
        Assert.Contains(trail, a => a.Action == "Document.Rejected" && a.Details!.Contains("Duplicate of an earlier bill"));
        Assert.All(trail, a => Assert.Equal(seed.UserId, a.UserId));
    }
}
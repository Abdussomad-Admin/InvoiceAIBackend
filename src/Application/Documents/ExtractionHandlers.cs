using System.Diagnostics;
using System.Text.Json;
using Application.Billing;
using Application.Assistant;
using Application.Bills;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Invoices;
using Application.Organizations;
using Application.Suppliers;
using Domain.Bills;
using Domain.Common;
using Domain.Documents;
using Domain.Organizations;

namespace Application.Documents;

public sealed class ExtractionReviewBuilder(ISupplierRepository suppliers, IBillRepository bills)
{
    private const string Notice =
        "These values were read by an AI model and may contain mistakes. Check every figure against the original document before approving.";

    public async Task<ExtractionReviewDto> BuildAsync(
        SourceDocument document, Organization organization, DateOnly today, ExtractedBill data,
        CancellationToken cancellationToken)
    {
        var issues = ExtractionValidator.Validate(data, today, organization.BaseCurrencyCode).ToList();

        var candidates = await suppliers.ListForMatchingAsync(cancellationToken);
        var (suggestions, strongMatchId) = SupplierMatcher.Match(data.SupplierName, data.SupplierTaxId, candidates);

        IReadOnlyList<PossibleDuplicateDto> duplicates = [];
        var numberInUse = false;

        if (strongMatchId is { } supplierId)
        {
            if (!string.IsNullOrWhiteSpace(data.InvoiceNumber))
            {
                numberInUse = await bills.NumberInUseAsync(
                    supplierId, SupplierBill.KeyFor(data.InvoiceNumber), Guid.Empty, cancellationToken);

                if (numberInUse)
                    issues.Add(new ExtractionIssue("invoiceNumber", IssueSeverity.Error,
                        "A bill with this number already exists for this supplier."));
            }

            if (data.Total is { } total && data.InvoiceDate is { } date)
            {
                duplicates = await bills.FindPossibleDuplicatesAsync(
                    supplierId, MoneyMath.Round(total), date, Guid.Empty, cancellationToken);

                if (duplicates.Count > 0 && !numberInUse)
                    issues.Add(new ExtractionIssue("total", IssueSeverity.Warning,
                        "A bill with the same supplier, date and total already exists."));
            }
        }

        return new ExtractionReviewDto(
            document.Id, document.ExtractionAttempts, document.Status, data, issues,
            ExtractionValidator.ConfidenceOf(issues), organization.BaseCurrencyCode,
            strongMatchId, suggestions, numberInUse, duplicates, Notice);
    }
}

public sealed class ExtractDocumentHandler(
    IDocumentRepository documents, IDocumentStorage storage, IDocumentExtractionModel model,
    IDocumentExtractionRepository history, ExtractionReviewBuilder review, IOrganizationRepository organizations,
    IUnitOfWork unitOfWork, ICurrentOrganization currentOrganization, ICurrentUser currentUser,
    AssistantSettings settings, TimeProvider clock, IPlanLimitChecker? planLimits = null)
{
    private const int MaxPromptChars = 30_000;

    public static readonly JsonSerializerOptions StoredJson = new(JsonSerializerDefaults.Web);

    public async Task<ExtractionReviewDto> Handle(Guid documentId, CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
            throw new AssistantUnavailableException("AI features are not enabled on this server.");

        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);

        var document = await documents.GetByIdAsync(documentId, cancellationToken)
            ?? throw new NotFoundException("Document not found.");

        document.EnsureCanExtract();
        if (planLimits is not null) await planLimits.EnsureAiExtractionCapacityAsync(cancellationToken);

        if (document.Kind == DocumentKind.Pdf && !document.HasTextLayer)
            throw new DomainException("This PDF has no readable text, so it may be a scan. Upload a photo or image of the invoice instead.");

        var request = await BuildRequestAsync(document, cancellationToken);

        var timer = Stopwatch.StartNew();
        ExtractedBill? bill = null;
        string? raw = null;
        string? error = null;
        var outage = false;

        try
        {
            raw = await model.ExtractAsync(request, cancellationToken);
            bill = ExtractionParser.Parse(raw);
        }
        catch (AssistantUnavailableException ex)
        {
            error = ex.Message;
            outage = true;
        }
        catch (DomainException ex)
        {
            error = ex.Message;
        }

        timer.Stop();
        var duration = (int)timer.ElapsedMilliseconds;

        if (bill is null)
        {
            // Every attempt is recorded, including the ones that fail.
            document.RecordFailure(error!);
            history.Add(DocumentExtraction.Create(
                organization.Id, document.Id, document.ExtractionAttempts, userId, settings.Model,
                false, duration, 0, error, raw));
            await unitOfWork.SaveChangesAsync(cancellationToken);

            throw outage ? new AssistantUnavailableException(error!) : new DomainException(error!);
        }

        document.RecordExtraction(JsonSerializer.Serialize(bill, StoredJson));

        var result = await review.BuildAsync(
            document, organization, InvoiceLookups.Today(organization, clock), bill, cancellationToken);

        history.Add(DocumentExtraction.Create(
            organization.Id, document.Id, document.ExtractionAttempts, userId, settings.Model,
            true, duration, result.Issues.Count, null, raw));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }

    private async Task<ExtractionRequest> BuildRequestAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        if (document.Kind == DocumentKind.Pdf)
        {
            var text = document.ExtractedText ?? "";
            if (text.Length > MaxPromptChars) text = text[..MaxPromptChars];

            return new ExtractionRequest(ExtractionPrompts.System, ExtractionPrompts.Instructions, text, null, null);
        }

        var image = await storage.ReadAsync(document.Id, cancellationToken)
            ?? throw new NotFoundException("The document content is not available.");

        return new ExtractionRequest(
            ExtractionPrompts.System, ExtractionPrompts.Instructions, null, image, document.ContentType);
    }
}

public sealed class GetExtractionHandler(
    IDocumentRepository documents, IOrganizationRepository organizations, ExtractionReviewBuilder review,
    ICurrentOrganization currentOrganization, TimeProvider clock)
{
    // The checks are rebuilt from the stored values each time, so supplier matches and duplicate
    // warnings are always current.
    public async Task<ExtractionReviewDto> Handle(Guid documentId, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);

        var document = await documents.GetByIdAsync(documentId, cancellationToken)
            ?? throw new NotFoundException("Document not found.");

        if (document.ExtractedData is null)
            throw new NotFoundException("This document has not been extracted yet.");

        var bill = JsonSerializer.Deserialize<ExtractedBill>(document.ExtractedData, ExtractDocumentHandler.StoredJson)
            ?? throw new NotFoundException("This document has not been extracted yet.");

        return await review.BuildAsync(
            document, organization, InvoiceLookups.Today(organization, clock), bill, cancellationToken);
    }
}

public sealed class ListExtractionsHandler(IDocumentRepository documents, IDocumentExtractionRepository history)
{
    public async Task<IReadOnlyList<ExtractionAttemptDto>> Handle(Guid documentId, CancellationToken cancellationToken)
    {
        _ = await documents.GetByIdAsync(documentId, cancellationToken)
            ?? throw new NotFoundException("Document not found.");

        return await history.ListAsync(documentId, cancellationToken);
    }
}

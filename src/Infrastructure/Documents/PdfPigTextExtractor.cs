using Application.Documents;
using Domain.Common;
using Domain.Documents;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace Infrastructure.Documents;

public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public PdfTextResult Extract(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var document = PdfDocument.Open(stream, new ParsingOptions { UseLenientParsing = true });

            if (document.NumberOfPages > DocumentRules.MaxPdfPages)
                throw new DomainException($"This PDF has more than {DocumentRules.MaxPdfPages} pages.");

            var text = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                if (text.Length >= DocumentRules.MaxExtractedChars) break;

                text.AppendLine(page.Text);
                text.AppendLine();
            }

            return new PdfTextResult(Math.Max(1, document.NumberOfPages), text.ToString());
        }
        catch (DomainException)
        {
            throw;
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new DomainException("This PDF is password-protected. Remove the password and upload it again.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new DomainException("This PDF could not be read. It may be damaged.");
        }
    }
}
using System.Text.Encodings.Web;

namespace Application.Common;

public static class BrandedEmail
{
    public static string Render(string title, string body, string? buttonText = null, string? buttonUrl = null, string? footer = null)
    {
        var safeTitle = HtmlEncoder.Default.Encode(title);
        var safeBody = string.Join("</p><p style=\"margin:0 0 14px;font-size:15px;line-height:1.6;color:#40536d\">",
            body.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(paragraph => HtmlEncoder.Default.Encode(paragraph).Replace("\n", "<br>")));
        var button = !string.IsNullOrWhiteSpace(buttonText) && !string.IsNullOrWhiteSpace(buttonUrl)
            ? $"<p style=\"margin:24px 0\"><a href=\"{HtmlEncoder.Default.Encode(buttonUrl)}\" style=\"display:inline-block;padding:13px 24px;border-radius:9px;background:#0874f8;color:#ffffff;text-decoration:none;font-size:15px;font-weight:700\">{HtmlEncoder.Default.Encode(buttonText)}</a></p>"
            : "";
        var safeFooter = HtmlEncoder.Default.Encode(footer ?? "Invoice AI · Smart. Simple. Powerful.");

        return $"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:32px 16px;background:#f3f7fc;font-family:Arial,Helvetica,sans-serif;color:#14284a">
              <div style="max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #e1eaf5;border-radius:16px;overflow:hidden">
                <div style="padding:25px 30px;border-bottom:1px solid #edf1f7;font-size:22px;font-weight:700">Invoice <span style="color:#08b979">AI</span></div>
                <div style="padding:30px">
                  <h1 style="margin:0 0 20px;font-size:25px;line-height:1.25">{safeTitle}</h1>
                  <p style="margin:0 0 14px;font-size:15px;line-height:1.6;color:#40536d">{safeBody}</p>
                  {button}
                </div>
                <div style="padding:15px 30px;border-top:1px solid #edf1f7;background:#f9fbfe;font-size:12px;color:#687b93">{safeFooter}</div>
              </div>
            </body>
            </html>
            """;
    }

    public static string FromPlainText(string text, string title)
    {
        var paragraphs = text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var body = string.Join("\n\n", paragraphs);
        return Render(title, body);
    }
}

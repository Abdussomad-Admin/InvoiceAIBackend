using System.Net;
using System.Text.RegularExpressions;

namespace Infrastructure.Email;

internal static class EmailHtml
{
    public static string FromPlainText(string text)
    {
        var encoded = WebUtility.HtmlEncode(text)
            .Replace("\r\n", "\n")
            .Replace("\n", "<br>");

        var linked = Regex.Replace(encoded, @"https?://[^\s<]+",
            m => $"<a href=\"{m.Value}\">{m.Value}</a>");

        return "<html><body style=\"font-family:Arial,sans-serif;font-size:15px;line-height:1.5\">"
               + linked + "</body></html>";
    }
}
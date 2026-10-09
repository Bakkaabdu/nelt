using System.Net;
using System.Text.RegularExpressions;

namespace Nelt.IntegrationTests.Infrastructure;

/// <summary>Turns an HTML error page (e.g. the developer exception page) into a short readable text for test output.</summary>
public static class Html
{
    private static readonly Regex Blocks = new("<(script|style)[^>]*>.*?</\\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new("\\s+", RegexOptions.Compiled);

    public static string Excerpt(string html, int maxLength = 1500)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "(empty body)";
        }

        var text = Blocks.Replace(html, " ");
        text = Tags.Replace(text, " ");
        text = Spaces.Replace(WebUtility.HtmlDecode(text), " ").Trim();
        return text.Length > maxLength ? text[..maxLength] + " …" : text;
    }
}

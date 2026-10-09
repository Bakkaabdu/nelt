using System.Text.RegularExpressions;

namespace Nelt.Application.Common;

/// <summary>Converts YouTube/Vimeo watch links into privacy-friendly embed URLs. Anything else is rejected.</summary>
public static partial class VideoEmbed
{
    public static string? ToEmbedUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        string? id = null;

        if (host is "youtu.be")
        {
            id = uri.AbsolutePath.Trim('/');
        }
        else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com")
        {
            var match = YouTubeQuery().Match(uri.Query);
            id = match.Success ? match.Groups[1].Value : uri.AbsolutePath.StartsWith("/embed/", StringComparison.Ordinal) ? uri.AbsolutePath[7..] : null;
        }

        if (id is not null)
        {
            return YouTubeId().IsMatch(id) ? $"https://www.youtube-nocookie.com/embed/{id}?rel=0" : null;
        }

        if (host is "vimeo.com" or "www.vimeo.com" or "player.vimeo.com")
        {
            var match = VimeoId().Match(uri.AbsolutePath);
            return match.Success ? $"https://player.vimeo.com/video/{match.Groups[1].Value}?dnt=1" : null;
        }

        return null;
    }

    [GeneratedRegex(@"[?&]v=([A-Za-z0-9_-]{6,20})")]
    private static partial Regex YouTubeQuery();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{6,20}$")]
    private static partial Regex YouTubeId();

    [GeneratedRegex(@"/(?:video/)?(\d{5,12})")]
    private static partial Regex VimeoId();
}

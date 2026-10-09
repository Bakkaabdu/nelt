using Nelt.Application.Common;
using Nelt.Domain.Common;
using Nelt.Domain.Services;

namespace Nelt.UnitTests;

public class CommonTests
{
    [Fact]
    public void Localized_text_falls_back_to_english_then_any_value()
    {
        var text = LocalizedText.Of("Hello", de: "Hallo");

        Assert.Equal("Hallo", text.Get("de"));
        Assert.Equal("Hello", text.Get("zh"));
        Assert.Equal("Hallo", LocalizedText.Of(null, de: "Hallo").Get("ar"));
    }

    [Theory]
    [InlineData("Deutsch für Anfänger – A1", "deutsch-fur-anfanger-a1")]
    [InlineData("Straße & Stadt", "strase-stadt")]
    [InlineData("汉语", "course")]
    public void Slug_is_ascii_and_url_safe(string input, string expected)
    {
        Assert.Equal(expected, Slug.Create(input, "course"));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ?rel=0")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ?rel=0")]
    [InlineData("https://vimeo.com/76979871", "https://player.vimeo.com/video/76979871?dnt=1")]
    [InlineData("http://youtu.be/dQw4w9WgXcQ", null)]
    [InlineData("https://evil.example/watch?v=dQw4w9WgXcQ", null)]
    public void Only_https_youtube_and_vimeo_links_are_embedded(string url, string? expected)
    {
        Assert.Equal(expected, VideoEmbed.ToEmbedUrl(url));
    }

    [Fact]
    public void File_policy_rejects_disallowed_extensions_and_oversized_files()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        Assert.Null(FilePolicy.Validate(new FileUpload(stream, "lesson.pdf", 3), FileCategory.Material));
        Assert.NotNull(FilePolicy.Validate(new FileUpload(stream, "payload.exe", 3), FileCategory.Material));
        Assert.NotNull(FilePolicy.Validate(new FileUpload(stream, "photo.png", FilePolicy.MaxBytes(FileCategory.Image) + 1), FileCategory.Image));
    }

    [Fact]
    public void Certificate_serial_has_expected_shape()
    {
        Assert.Matches(@"^NELT-2026-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", CertificateSerial.New(new DateTime(2026, 9, 29)));
    }

    [Fact]
    public void Platform_time_round_trips_and_survives_dst_gaps()
    {
        var time = new PlatformTime(TimeProvider.System);
        time.UseZone("Europe/Berlin");

        var local = new DateTime(2026, 7, 1, 18, 0, 0);
        Assert.Equal(local, time.ToLocal(time.ToUtc(local)));

        // 02:30 on the spring-forward night does not exist in Berlin; it must not throw.
        var gap = time.ToUtc(new DateTime(2026, 3, 29, 2, 30, 0));
        Assert.Equal(DateTimeKind.Utc, gap.Kind);
    }
}

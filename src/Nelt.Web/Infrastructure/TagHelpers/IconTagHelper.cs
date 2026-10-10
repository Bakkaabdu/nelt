using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Nelt.Web.Infrastructure.TagHelpers;

/// <summary><c>&lt;icon name="calendar" /&gt;</c> → a reference into the cached SVG sprite.</summary>
[HtmlTargetElement("icon", Attributes = "name", TagStructure = TagStructure.WithoutEndTag)]
public sealed class IconTagHelper(IHttpContextAccessor accessor) : TagHelper
{
    public const string SpriteVersion = "4";

    public string Name { get; set; } = string.Empty;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var pathBase = accessor.HttpContext?.Request.PathBase.Value ?? string.Empty;
        var extra = output.Attributes["class"]?.Value?.ToString();
        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(extra) ? "icon" : $"icon {extra}");
        output.Attributes.SetAttribute("aria-hidden", "true");
        output.Attributes.SetAttribute("focusable", "false");
        output.Content.SetHtmlContent($"<use href=\"{pathBase}/img/icons.svg?v={SpriteVersion}#{Name}\"></use>");
    }
}

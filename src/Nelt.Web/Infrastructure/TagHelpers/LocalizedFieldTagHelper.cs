using Nelt.Web;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;
using Nelt.Application.Common;
using Nelt.Domain.Common;
using Nelt.Web.Infrastructure.Localization;

namespace Nelt.Web.Infrastructure.TagHelpers;

/// <summary>
/// Editor for admin-managed multilingual content: one tab per platform language (EN · AR · DE · 中文).
/// Posts as {Name}.En / .Ar / .De / .Zh, binding straight into a <see cref="LocalizedText"/>.
/// </summary>
[HtmlTargetElement("localized-field", Attributes = "asp-for", TagStructure = TagStructure.WithoutEndTag)]
public sealed class LocalizedFieldTagHelper(IHtmlGenerator generator, IStringLocalizer<SharedResource> l) : TagHelper
{
    private static readonly (string Key, string Lang, string Tab, bool Rtl)[] Languages =
    [
        ("En", "en", "EN", false),
        ("Ar", "ar", "AR", true),
        ("De", "de", "DE", false),
        ("Zh", "zh", "中文", false),
    ];

    [HtmlAttributeName("asp-for")]
    public ModelExpression For { get; set; } = default!;

    public int Rows { get; set; }

    public string? Hint { get; set; }

    public bool Wide { get; set; }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var fullName = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(For.Name);
        var baseId = TagBuilder.CreateSanitizedId(fullName, "_");
        var model = For.Model as LocalizedText ?? new LocalizedText();
        var rule = For.Metadata.ValidatorMetadata.OfType<LocalizedTextAttribute>().FirstOrDefault();
        var label = For.Metadata.DisplayName ?? For.Metadata.PropertyName ?? For.Name;

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", Wide ? "field field--wide lt" : "field lt");
        output.Attributes.SetAttribute("data-lt", string.Empty);

        var head = new TagBuilder("div");
        head.AddCssClass("lt__head");
        var labelTag = new TagBuilder("label");
        labelTag.AddCssClass("field__label");
        labelTag.Attributes["for"] = $"{baseId}_En";
        labelTag.InnerHtml.Append(label);
        if (rule?.RequireEnglish == true)
        {
            labelTag.InnerHtml.AppendHtml("<span class=\"req\" aria-hidden=\"true\">*</span>");
        }

        head.InnerHtml.AppendHtml(labelTag);

        var tabs = new TagBuilder("div");
        tabs.AddCssClass("lt__tabs");
        tabs.Attributes["role"] = "tablist";
        tabs.Attributes["aria-label"] = l["Language"].Value;

        var panes = new TagBuilder("div");
        panes.AddCssClass("lt__panes");

        for (var i = 0; i < Languages.Length; i++)
        {
            var (key, lang, tabText, rtl) = Languages[i];
            var name = $"{fullName}.{key}";
            var id = $"{baseId}_{key}";
            var value = ViewContext.ModelState.TryGetValue(name, out var entry) && entry.AttemptedValue is not null
                ? entry.AttemptedValue
                : ValueOf(model, key);

            var tab = new TagBuilder("button");
            tab.AddCssClass(i == 0 ? "lt__tab is-active" : "lt__tab");
            if (!string.IsNullOrWhiteSpace(value))
            {
                tab.AddCssClass("has-value");
            }

            tab.Attributes["type"] = "button";
            tab.Attributes["role"] = "tab";
            tab.Attributes["data-lt-tab"] = key;
            tab.Attributes["aria-controls"] = id;
            tab.Attributes["aria-selected"] = i == 0 ? "true" : "false";
            tab.InnerHtml.Append(tabText);
            tabs.InnerHtml.AppendHtml(tab);

            var control = new TagBuilder(Rows > 0 ? "textarea" : "input");
            control.AddCssClass(Rows > 0 ? "input input--area" : "input");
            control.Attributes["name"] = name;
            control.Attributes["id"] = id;
            control.Attributes["lang"] = lang;
            control.Attributes["dir"] = rtl ? "rtl" : "ltr";
            control.Attributes["data-lt-pane"] = key;
            if (rule is not null)
            {
                control.Attributes["maxlength"] = rule.MaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (i == 0 && rule?.RequireEnglish == true)
            {
                control.Attributes["required"] = "required";
            }

            if (i > 0)
            {
                control.Attributes["hidden"] = "hidden";
            }

            if (Rows > 0)
            {
                control.Attributes["rows"] = Rows.ToString(System.Globalization.CultureInfo.InvariantCulture);
                control.InnerHtml.Append(value ?? string.Empty);
            }
            else
            {
                control.TagRenderMode = TagRenderMode.SelfClosing;
                control.Attributes["type"] = "text";
                control.Attributes["value"] = value ?? string.Empty;
            }

            panes.InnerHtml.AppendHtml(control);
        }

        head.InnerHtml.AppendHtml(tabs);
        output.Content.AppendHtml(head);
        output.Content.AppendHtml(panes);

        if (!string.IsNullOrEmpty(Hint))
        {
            output.Content.AppendHtml($"<p class=\"field__hint\">{System.Net.WebUtility.HtmlEncode(l[Hint])}</p>");
        }

        output.Content.AppendHtml(generator.GenerateValidationMessage(ViewContext, For.ModelExplorer, For.Name, null, "span", new { @class = "field__error" }));
    }

    private static string? ValueOf(LocalizedText text, string key) => key switch
    {
        "Ar" => text.Ar,
        "De" => text.De,
        "Zh" => text.Zh,
        _ => text.En,
    };
}

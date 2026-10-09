using Nelt.Web;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;
using Nelt.Web.Infrastructure.Localization;
using Nelt.Web.Infrastructure.Razor;

namespace Nelt.Web.Infrastructure.TagHelpers;

/// <summary>
/// One consistent form row: label, the right control for the property type, hint and validation message.
/// <c>&lt;field asp-for="Input.Title" /&gt;</c>. Number/date values are rendered culture-invariant to match
/// the invariant form binding.
/// </summary>
[HtmlTargetElement("field", Attributes = "asp-for", TagStructure = TagStructure.WithoutEndTag)]
public sealed class FieldTagHelper(IHtmlGenerator generator, IStringLocalizer<SharedResource> l) : TagHelper
{
    [HtmlAttributeName("asp-for")]
    public ModelExpression For { get; set; } = default!;

    [HtmlAttributeName("asp-items")]
    public IEnumerable<SelectListItem>? Items { get; set; }

    /// <summary>English hint text (localized).</summary>
    public string? Hint { get; set; }

    /// <summary>English label override (localized).</summary>
    public string? Label { get; set; }

    /// <summary>English text of an empty first option for selects (localized).</summary>
    public string? Empty { get; set; }

    /// <summary>Renders a textarea with this many rows.</summary>
    public int Rows { get; set; }

    /// <summary>Overrides the input type (e.g. password).</summary>
    public string? Type { get; set; }

    public string? Placeholder { get; set; }

    public bool Wide { get; set; }

    public string? Autocomplete { get; set; }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var explorer = For.ModelExplorer;
        var metadata = For.Metadata;
        var name = For.Name;
        var type = Nullable.GetUnderlyingType(metadata.ModelType) ?? metadata.ModelType;
        var attributes = metadata.ValidatorMetadata;
        var required = attributes.OfType<RequiredAttribute>().Any();
        var labelText = Label is not null ? l[Label].Value : metadata.DisplayName ?? metadata.PropertyName ?? name;

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        var css = "field";
        if (Wide)
        {
            css += " field--wide";
        }

        if (type == typeof(bool))
        {
            css += " field--check";
        }

        output.Attributes.SetAttribute("class", (css + " " + output.Attributes["class"]?.Value).Trim());

        var control = new Dictionary<string, object> { ["class"] = "input" };
        if (required)
        {
            control["required"] = "required";
        }

        if (!string.IsNullOrEmpty(Placeholder))
        {
            control["placeholder"] = l[Placeholder].Value;
        }

        if (!string.IsNullOrEmpty(Autocomplete))
        {
            control["autocomplete"] = Autocomplete;
        }

        if (type == typeof(bool))
        {
            control["class"] = "check__box";
            var label = new TagBuilder("label");
            label.AddCssClass("check");
            label.InnerHtml.AppendHtml(generator.GenerateCheckBox(ViewContext, explorer, name, null, control));
            label.InnerHtml.AppendHtml("<span class=\"check__ui\" aria-hidden=\"true\"></span>");
            var text = new TagBuilder("span");
            text.AddCssClass("check__label");
            text.InnerHtml.Append(labelText);
            label.InnerHtml.AppendHtml(text);
            output.Content.AppendHtml(label);
            output.Content.AppendHtml(generator.GenerateHiddenForCheckbox(ViewContext, explorer, name));
            AppendHintAndError(output, explorer, name);
            return;
        }

        var labelTag = generator.GenerateLabel(ViewContext, explorer, name, labelText, new { @class = "field__label" });
        if (required)
        {
            labelTag.InnerHtml.AppendHtml("<span class=\"req\" aria-hidden=\"true\">*</span>");
        }

        output.Content.AppendHtml(labelTag);

        if (Items is not null || type.IsEnum)
        {
            control["class"] = "input input--select";
            var items = Items ?? EnumItems(type, For.Model);
            output.Content.AppendHtml(generator.GenerateSelect(ViewContext, explorer, Empty is null ? null : l[Empty].Value, name, items, false, control));
        }
        else if (Rows > 0)
        {
            control["class"] = "input input--area";
            AddLength(control, attributes);
            output.Content.AppendHtml(generator.GenerateTextArea(ViewContext, explorer, name, Rows, 0, control));
        }
        else
        {
            var (inputType, value) = InputFor(type, For.Model, metadata.DataTypeName);
            control["type"] = Type ?? inputType;
            if (inputType == "number")
            {
                control["inputmode"] = type == typeof(decimal) || type == typeof(double) ? "decimal" : "numeric";
                control["step"] = type == typeof(decimal) || type == typeof(double) ? "0.01" : "1";
                if (attributes.OfType<RangeAttribute>().FirstOrDefault() is { } range)
                {
                    control["min"] = Convert.ToString(range.Minimum, CultureInfo.InvariantCulture)!;
                    control["max"] = Convert.ToString(range.Maximum, CultureInfo.InvariantCulture)!;
                }
            }

            AddLength(control, attributes);
            if ((Type ?? inputType) == "password")
            {
                value = string.Empty;
            }

            output.Content.AppendHtml(generator.GenerateTextBox(ViewContext, explorer, name, value, null, control));
        }

        AppendHintAndError(output, explorer, name);
    }

    private void AppendHintAndError(TagHelperOutput output, Microsoft.AspNetCore.Mvc.ViewFeatures.ModelExplorer explorer, string name)
    {
        if (!string.IsNullOrEmpty(Hint))
        {
            var hint = new TagBuilder("p");
            hint.AddCssClass("field__hint");
            hint.InnerHtml.Append(l[Hint]);
            output.Content.AppendHtml(hint);
        }

        output.Content.AppendHtml(generator.GenerateValidationMessage(ViewContext, explorer, name, null, "span", new { @class = "field__error" }));
    }

    private IEnumerable<SelectListItem> EnumItems(Type enumType, object? current)
        => Enum.GetValues(enumType).Cast<Enum>()
            .Select(v => new SelectListItem(EnumLabels.Get(v, l), v.ToString(), Equals(v, current)));

    private static void AddLength(Dictionary<string, object> control, IEnumerable<object> attributes)
    {
        if (attributes.OfType<StringLengthAttribute>().FirstOrDefault() is { } length)
        {
            control["maxlength"] = length.MaximumLength.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static (string Type, string? Value) InputFor(Type type, object? model, string? dataType)
    {
        var invariant = CultureInfo.InvariantCulture;
        return type switch
        {
            _ when type == typeof(DateTime) => ("datetime-local", (model as DateTime?)?.ToString("yyyy-MM-ddTHH:mm", invariant)),
            _ when type == typeof(DateOnly) => ("date", (model as DateOnly?)?.ToString("yyyy-MM-dd", invariant)),
            _ when type == typeof(TimeOnly) => ("time", (model as TimeOnly?)?.ToString("HH:mm", invariant)),
            _ when type == typeof(int) || type == typeof(long) => ("number", Convert.ToString(model, invariant)),
            _ when type == typeof(decimal) || type == typeof(double) => ("number", model is null ? null : Convert.ToDecimal(model, invariant).ToString("0.##", invariant)),
            _ => (dataType switch
            {
                nameof(DataType.EmailAddress) => "email",
                nameof(DataType.Password) => "password",
                nameof(DataType.PhoneNumber) => "tel",
                nameof(DataType.Url) => "url",
                _ => "text",
            }, model as string),
        };
    }
}

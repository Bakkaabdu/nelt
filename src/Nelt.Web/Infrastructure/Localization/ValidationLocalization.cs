using Nelt.Web;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Nelt.Web.Infrastructure.Localization;

/// <summary>
/// Gives built-in validation attributes an English message template when none is set, so the data-annotation
/// localizer can translate them (the framework's built-in messages are not localizable).
/// </summary>
public sealed class DefaultValidationMessagesProvider : IValidationMetadataProvider
{
    public void CreateValidationMetadata(ValidationMetadataProviderContext context)
    {
        foreach (var attribute in context.ValidationMetadata.ValidatorMetadata.OfType<ValidationAttribute>())
        {
            if (attribute.ErrorMessage is not null || attribute.ErrorMessageResourceName is not null)
            {
                continue;
            }

            attribute.ErrorMessage = attribute switch
            {
                RequiredAttribute => "The {0} field is required.",
                StringLengthAttribute { MinimumLength: > 0 } => "{0} must be between {2} and {1} characters long.",
                StringLengthAttribute => "{0} can be at most {1} characters long.",
                RangeAttribute => "{0} must be between {1} and {2}.",
                EmailAddressAttribute => "Please enter a valid email address.",
                PhoneAttribute => "Please enter a valid phone number.",
                UrlAttribute => "Please enter a valid web address.",
                CompareAttribute => "{0} and {1} do not match.",
                _ => attribute.ErrorMessage,
            };
        }
    }
}

/// <summary>Localized messages for model binding failures (e.g. text typed into a number field).</summary>
public sealed class ConfigureModelBindingMessages(IStringLocalizerFactory factory) : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options)
    {
        var l = factory.Create(typeof(SharedResource));
        var messages = options.ModelBindingMessageProvider;
        messages.SetValueMustNotBeNullAccessor(_ => l["This field is required."]);
        messages.SetMissingBindRequiredValueAccessor(_ => l["This field is required."]);
        messages.SetMissingKeyOrValueAccessor(() => l["This field is required."]);
        messages.SetAttemptedValueIsInvalidAccessor((value, _) => l["The value '{0}' is not valid.", value]);
        messages.SetUnknownValueIsInvalidAccessor(_ => l["The value is not valid."]);
        messages.SetValueIsInvalidAccessor(value => l["The value '{0}' is not valid.", value]);
        messages.SetValueMustBeANumberAccessor(_ => l["Please enter a number."]);
        messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value => l["The value '{0}' is not valid.", value]);
        messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => l["The value is not valid."]);
        messages.SetNonPropertyValueMustBeANumberAccessor(() => l["Please enter a number."]);
    }
}

/// <summary>
/// Translates messages produced by IValidatableObject and custom attributes (English keys) before the action runs.
/// Messages that are already localized are not keys and pass through unchanged.
/// </summary>
public sealed class LocalizeModelStateFilter(IStringLocalizer<SharedResource> l) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var state = context.ModelState;
        if (state.IsValid)
        {
            return;
        }

        foreach (var (key, entry) in state.ToList())
        {
            if (entry is null || entry.Errors.Count == 0)
            {
                continue;
            }

            var messages = entry.Errors.Select(e => e.ErrorMessage).ToList();
            var localized = messages.Select(m => string.IsNullOrEmpty(m) ? m : l[m].Value).ToList();
            if (messages.SequenceEqual(localized))
            {
                continue;
            }

            entry.Errors.Clear();
            foreach (var message in localized)
            {
                state.AddModelError(key, message);
            }
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}

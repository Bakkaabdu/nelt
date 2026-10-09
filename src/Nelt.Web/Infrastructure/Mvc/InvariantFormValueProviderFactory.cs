using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Nelt.Web.Infrastructure.Mvc;

/// <summary>
/// Binds form posts with the invariant culture. HTML number/date inputs always submit invariant values
/// ("12.5", "2026-10-01"); binding them with a German or Arabic culture would silently corrupt them.
/// </summary>
public sealed class InvariantFormValueProviderFactory : IValueProviderFactory
{
    public async Task CreateValueProviderAsync(ValueProviderFactoryContext context)
    {
        var request = context.ActionContext.HttpContext.Request;
        if (!request.HasFormContentType)
        {
            return;
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(context.ActionContext.HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            // Same contract as the built-in factory: surfaces as a 400 instead of an unhandled exception.
            throw new ValueProviderException(ex.Message, ex);
        }

        context.ValueProviders.Add(new FormValueProvider(BindingSource.Form, form, CultureInfo.InvariantCulture));
    }
}

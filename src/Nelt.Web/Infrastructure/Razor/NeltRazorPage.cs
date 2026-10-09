using Nelt.Web;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Localization;
using Nelt.Application.Common;
using Nelt.Domain.Common;
using Nelt.Domain.Enums;
using Nelt.Web.Infrastructure.Localization;

namespace Nelt.Web.Infrastructure.Razor;

/// <summary>Base class for every view: localization, time-zone aware formatting and enum labels.</summary>
public abstract class NeltRazorPage<TModel> : RazorPage<TModel>
{
    private IStringLocalizer<SharedResource>? _l;
    private IPlatformTime? _time;

    /// <summary>UI strings. English text is the key; missing translations fall back to English.</summary>
    public IStringLocalizer<SharedResource> L => _l ??= Context.RequestServices.GetRequiredService<IStringLocalizer<SharedResource>>();

    public IPlatformTime Time => _time ??= Context.RequestServices.GetRequiredService<IPlatformTime>();

    public string Lang => Cultures.Current.Code;

    public bool IsRtl => Cultures.Current.IsRightToLeft;

    /// <summary>Picks the current language from admin-managed multilingual content.</summary>
    public string Tx(LocalizedText? text) => text?.Get(Lang) ?? string.Empty;

    public string Label(Enum value) => EnumLabels.Get(value, L);

    public string Date(DateTime utc) => Time.ToLocal(utc).ToString(DatePattern, CultureInfo.CurrentCulture);

    public string Date(DateOnly date) => date.ToString(DatePattern, CultureInfo.CurrentCulture);

    public string DateTimeText(DateTime utc) => Time.ToLocal(utc).ToString(DatePattern + " · HH:mm", CultureInfo.CurrentCulture);

    public string Clock(DateTime utc) => Time.ToLocal(utc).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"18:00–20:00", isolated as left-to-right so it never flips inside Arabic text.</summary>
    public string Range(DateTime startUtc, DateTime endUtc) => $"\u2066{Clock(startUtc)}–{Clock(endUtc)}\u2069";

    public string Weekday(DateTime utc) => Time.ToLocal(utc).ToString("dddd", CultureInfo.CurrentCulture);

    public string Money(decimal amount, string currency)
        => amount == 0 ? L["Free"] : $"{amount.ToString(amount % 1 == 0 ? "N0" : "N2", CultureInfo.CurrentCulture)} {currency}";

    public string Percent(decimal? value) => value is null ? "—" : $"{value.Value.ToString("0.#", CultureInfo.CurrentCulture)}%";

    public string FileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024d / 1024d:0.#} MB",
        _ => $"{bytes / 1024d / 1024d / 1024d:0.##} GB",
    };

    /// <summary>Where "My dashboard" leads for the signed-in user.</summary>
    public string DashboardUrl()
    {
        var url = Context.RequestServices.GetRequiredService<IUrlHelperFactory>().GetUrlHelper(ViewContext);
        if (User.IsInRole(Roles.Admin))
        {
            return url.Action("Index", "Dashboard", new { area = "Admin" })!;
        }

        return User.IsInRole(Roles.Instructor)
            ? url.Action("Index", "Courses", new { area = "Teach" })!
            : url.Action("Index", "Dashboard", new { area = "Learn" })!;
    }

    /// <summary>True when the current request is served by the given area/controller (for navigation highlighting).</summary>
    public bool IsActive(string area, params string[] controllers)
    {
        var values = ViewContext.RouteData.Values;
        var currentArea = values["area"] as string ?? string.Empty;
        var controller = values["controller"] as string ?? string.Empty;
        return string.Equals(currentArea, area, StringComparison.OrdinalIgnoreCase)
               && (controllers.Length == 0 || controllers.Any(c => string.Equals(c, controller, StringComparison.OrdinalIgnoreCase)));
    }

    public string Active(string area, params string[] controllers) => IsActive(area, controllers) ? "is-active" : string.Empty;

    /// <summary>Colour band of a level: 1 = Schwarz (A1–A2 / HSK 1–2), 2 = Rot, 3 = Gold.</summary>
    public static string Band(string levelCode)
    {
        var code = levelCode.ToUpperInvariant();
        if (code.StartsWith('A'))
        {
            return "band-1";
        }

        if (code.StartsWith('B'))
        {
            return "band-2";
        }

        if (code.StartsWith('C'))
        {
            return "band-3";
        }

        var digits = new string(code.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n <= 2 ? "band-1" : n <= 4 ? "band-2" : "band-3" : "band-1";
    }

    public static string BandForRank(int rank) => rank <= 2 ? "band-1" : rank <= 4 ? "band-2" : "band-3";

    public string LanguageCode(TargetLanguage language) => language == TargetLanguage.German ? "DE" : "中文";

    private string DatePattern => Lang switch
    {
        "de" => "d. MMM yyyy",
        "zh" => "yyyy年M月d日",
        "ar" => "d MMMM yyyy",
        _ => "d MMM yyyy",
    };
}

public static class EnumLabels
{
    public static string Get(Enum value, IStringLocalizer localizer)
    {
        var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
        var name = member?.GetCustomAttribute<DisplayAttribute>()?.Name ?? value.ToString();
        return localizer[name];
    }
}

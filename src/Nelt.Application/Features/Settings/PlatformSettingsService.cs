using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Domain.Common;
using Nelt.Domain.Entities;

namespace Nelt.Application.Features.Settings;

public interface IPlatformSettingsService
{
    Task<SiteSettings> GetAsync(CancellationToken ct = default);
    Task<SettingsInput> GetForEditAsync(CancellationToken ct = default);
    Task<Result> UpdateAsync(SettingsInput input, CancellationToken ct = default);
}

internal sealed class PlatformSettingsService(IAppDbContext db, ContentCache cache, IPlatformTime time) : IPlatformSettingsService
{
    private const string CacheKey = "settings";

    public Task<SiteSettings> GetAsync(CancellationToken ct = default)
        => cache.GetOrCreateAsync(CacheKey, async token =>
        {
            var entity = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == PlatformSettings.SingletonId, token)
                         ?? new PlatformSettings();
            time.UseZone(entity.TimeZoneId);
            return ToSnapshot(entity);
        }, ct);

    public async Task<SettingsInput> GetForEditAsync(CancellationToken ct = default)
    {
        var s = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == PlatformSettings.SingletonId, ct) ?? new PlatformSettings();
        return new SettingsInput
        {
            SiteName = s.SiteName,
            Tagline = s.Tagline.CloneOrEmpty(),
            HeroTitle = s.HeroTitle.CloneOrEmpty(),
            HeroSubtitle = s.HeroSubtitle.CloneOrEmpty(),
            AboutTitle = s.AboutTitle.CloneOrEmpty(),
            AboutBody = s.AboutBody.CloneOrEmpty(),
            PaymentInstructions = s.PaymentInstructions.CloneOrEmpty(),
            Address = s.Address.CloneOrEmpty(),
            ContactEmail = s.ContactEmail,
            ContactPhone = s.ContactPhone,
            WhatsAppNumber = s.WhatsAppNumber,
            FacebookUrl = s.FacebookUrl,
            InstagramUrl = s.InstagramUrl,
            TimeZoneId = s.TimeZoneId,
            Currency = s.Currency,
        };
    }

    public async Task<Result> UpdateAsync(SettingsInput input, CancellationToken ct = default)
    {
        var s = await db.PlatformSettings.FirstOrDefaultAsync(x => x.Id == PlatformSettings.SingletonId, ct);
        if (s is null)
        {
            s = new PlatformSettings { Id = PlatformSettings.SingletonId };
            db.PlatformSettings.Add(s);
        }

        s.SiteName = input.SiteName.Trim();
        s.Tagline.CopyFrom(input.Tagline);
        s.HeroTitle.CopyFrom(input.HeroTitle);
        s.HeroSubtitle.CopyFrom(input.HeroSubtitle);
        s.AboutTitle.CopyFrom(input.AboutTitle);
        s.AboutBody.CopyFrom(input.AboutBody);
        s.PaymentInstructions.CopyFrom(input.PaymentInstructions);
        s.Address.CopyFrom(input.Address);
        s.ContactEmail = Clean(input.ContactEmail);
        s.ContactPhone = Clean(input.ContactPhone);
        s.WhatsAppNumber = Clean(input.WhatsAppNumber);
        s.FacebookUrl = Clean(input.FacebookUrl);
        s.InstagramUrl = Clean(input.InstagramUrl);
        s.TimeZoneId = input.TimeZoneId.Trim();
        s.Currency = input.Currency.Trim().ToUpperInvariant();
        s.UpdatedAt = time.UtcNow;

        await db.SaveChangesAsync(ct);
        time.UseZone(s.TimeZoneId);
        cache.Invalidate();
        return Result.Success();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SiteSettings ToSnapshot(PlatformSettings s) => new(
        s.SiteName, s.Tagline.CloneOrEmpty(), s.HeroTitle.CloneOrEmpty(), s.HeroSubtitle.CloneOrEmpty(), s.AboutTitle.CloneOrEmpty(), s.AboutBody.CloneOrEmpty(),
        s.PaymentInstructions.CloneOrEmpty(), s.Address.CloneOrEmpty(), s.ContactEmail, s.ContactPhone, s.WhatsAppNumber, s.FacebookUrl,
        s.InstagramUrl, s.TimeZoneId, s.Currency);
}

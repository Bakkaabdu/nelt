using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Domain.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Levels;

public sealed record LevelRow(int Id, TargetLanguage Language, string Code, int Rank, LocalizedText Name, int CourseCount, int MaterialCount);

public sealed record LevelOption(int Id, TargetLanguage Language, string Code, LocalizedText Name);

public sealed class LevelInput
{
    [Required, Display(Name = "Language")]
    public TargetLanguage Language { get; set; } = TargetLanguage.German;

    [Required, StringLength(16), Display(Name = "Code")]
    public string Code { get; set; } = string.Empty;

    [Range(1, 99), Display(Name = "Order")]
    public int Rank { get; set; } = 1;

    [LocalizedText(80, RequireEnglish = true), Display(Name = "Name")]
    public LocalizedText Name { get; set; } = new();

    [LocalizedText(600), Display(Name = "Description")]
    public LocalizedText Description { get; set; } = new();
}

public interface ILevelService
{
    Task<IReadOnlyList<LevelRow>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LevelOption>> OptionsAsync(CancellationToken ct = default);
    Task<LevelInput?> GetForEditAsync(int id, CancellationToken ct = default);
    Task<Result<int>> CreateAsync(LevelInput input, CancellationToken ct = default);
    Task<Result> UpdateAsync(int id, LevelInput input, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
}

internal sealed class LevelService(IAppDbContext db, ContentCache cache) : ILevelService
{
    public async Task<IReadOnlyList<LevelRow>> ListAsync(CancellationToken ct = default)
        => await db.Levels.AsNoTracking()
            .OrderBy(l => l.Language).ThenBy(l => l.Rank)
            .Select(l => new LevelRow(l.Id, l.Language, l.Code, l.Rank, l.Name, l.Courses.Count, l.Materials.Count))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LevelOption>> OptionsAsync(CancellationToken ct = default)
        => await db.Levels.AsNoTracking()
            .OrderBy(l => l.Language).ThenBy(l => l.Rank)
            .Select(l => new LevelOption(l.Id, l.Language, l.Code, l.Name))
            .ToListAsync(ct);

    public async Task<LevelInput?> GetForEditAsync(int id, CancellationToken ct = default)
        => await db.Levels.AsNoTracking().Where(l => l.Id == id)
            .Select(l => new LevelInput { Language = l.Language, Code = l.Code, Rank = l.Rank, Name = l.Name, Description = l.Description })
            .FirstOrDefaultAsync(ct);

    public async Task<Result<int>> CreateAsync(LevelInput input, CancellationToken ct = default)
    {
        if (await ConflictAsync(null, input, ct) is { } error)
        {
            return error;
        }

        var level = new Level();
        Apply(level, input);
        db.Levels.Add(level);
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return level.Id;
    }

    public async Task<Result> UpdateAsync(int id, LevelInput input, CancellationToken ct = default)
    {
        var level = await db.Levels.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (level is null)
        {
            return Error.NotFound();
        }

        if (await ConflictAsync(id, input, ct) is { } error)
        {
            return error;
        }

        Apply(level, input);
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        var level = await db.Levels.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (level is null)
        {
            return Error.NotFound();
        }

        if (await db.Courses.AnyAsync(c => c.LevelId == id, ct) || await db.Materials.AnyAsync(m => m.LevelId == id, ct))
        {
            return Error.Conflict("This level still has courses or materials.");
        }

        db.Levels.Remove(level);
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return Result.Success();
    }

    private async Task<Error?> ConflictAsync(int? id, LevelInput input, CancellationToken ct)
    {
        var code = input.Code.Trim().ToUpperInvariant();
        if (await db.Levels.AnyAsync(l => l.Id != id && l.Language == input.Language && l.Code == code, ct))
        {
            return Error.Validation("A level with this code already exists.", nameof(LevelInput.Code));
        }

        if (await db.Levels.AnyAsync(l => l.Id != id && l.Language == input.Language && l.Rank == input.Rank, ct))
        {
            return Error.Validation("Another level of this language already uses this order.", nameof(LevelInput.Rank));
        }

        return null;
    }

    private static void Apply(Level level, LevelInput input)
    {
        level.Language = input.Language;
        level.Code = input.Code.Trim().ToUpperInvariant();
        level.Rank = input.Rank;
        level.Name.CopyFrom(input.Name);
        level.Description.CopyFrom(input.Description);
    }
}

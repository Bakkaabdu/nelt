using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Domain.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Materials;

public sealed record LibraryLevel(int Id, TargetLanguage Language, string Code, LocalizedText Name, int MaterialCount);

public sealed record LibraryItem(
    int Id,
    MaterialType Type,
    string Title,
    string? Description,
    string FileName,
    string ContentType,
    long SizeBytes,
    int? CourseId,
    LocalizedText? CourseTitle,
    bool IsPublished,
    DateTime CreatedAt);

public sealed record LibraryCourse(int Id, LocalizedText Title);

public sealed record MaterialLibrary(
    IReadOnlyList<LibraryLevel> Levels,
    LibraryLevel? Selected,
    IReadOnlyList<LibraryCourse> Courses,
    IReadOnlyDictionary<MaterialType, IReadOnlyList<LibraryItem>> Groups);

public sealed class MaterialInput
{
    [Range(1, int.MaxValue, ErrorMessage = "Please choose a level."), Display(Name = "Level")]
    public int LevelId { get; set; }

    [Display(Name = "Only for course")]
    public int? CourseId { get; set; }

    [Display(Name = "Category")]
    public MaterialType Type { get; set; } = MaterialType.Document;

    [Required, StringLength(200), Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000), Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Visible to students")]
    public bool IsPublished { get; set; } = true;
}

public interface IMaterialService
{
    Task<MaterialLibrary> LibraryAsync(int? levelId, CancellationToken ct = default);
    Task<Result<MaterialInput>> GetForEditAsync(int id, CancellationToken ct = default);
    Task<Result<int>> UploadAsync(MaterialInput input, FileUpload file, CancellationToken ct = default);
    Task<Result> UpdateAsync(int id, MaterialInput input, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
    Task<Result> MoveAsync(int id, int direction, CancellationToken ct = default);
}

/// <summary>
/// The level-organised library of books, documents and audio. Admins manage every level; instructors manage the
/// levels they teach (and may pin a file to one of their own courses).
/// </summary>
internal sealed class MaterialService(IAppDbContext db, ICurrentUser user, IFileStorage storage) : IMaterialService
{
    public async Task<MaterialLibrary> LibraryAsync(int? levelId, CancellationToken ct = default)
    {
        var levels = await ManageableLevels().AsNoTracking()
            .OrderBy(l => l.Language).ThenBy(l => l.Rank)
            .Select(l => new LibraryLevel(l.Id, l.Language, l.Code, l.Name, l.Materials.Count))
            .ToListAsync(ct);

        var selected = levels.FirstOrDefault(l => l.Id == levelId) ?? levels.FirstOrDefault();
        if (selected is null)
        {
            return new MaterialLibrary(levels, null, [], new Dictionary<MaterialType, IReadOnlyList<LibraryItem>>());
        }

        var items = await db.Materials.AsNoTracking().Where(m => m.LevelId == selected.Id)
            .OrderBy(m => m.Type).ThenBy(m => m.SortOrder).ThenBy(m => m.Title)
            .Select(m => new LibraryItem(m.Id, m.Type, m.Title, m.Description, m.FileName, m.ContentType, m.SizeBytes, m.CourseId,
                m.Course != null ? m.Course.Title : null, m.IsPublished, m.CreatedAt))
            .ToListAsync(ct);

        var courses = await ManageableCoursesOfLevel(selected.Id).AsNoTracking()
            .OrderBy(c => c.SortOrder).Select(c => new LibraryCourse(c.Id, c.Title)).ToListAsync(ct);

        var groups = Enum.GetValues<MaterialType>()
            .ToDictionary(t => t, t => (IReadOnlyList<LibraryItem>)items.Where(i => i.Type == t).ToList());

        return new MaterialLibrary(levels, selected, courses, groups);
    }

    public async Task<Result<MaterialInput>> GetForEditAsync(int id, CancellationToken ct = default)
    {
        var material = await ManageableMaterials().AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (material is null)
        {
            return Error.NotFound();
        }

        return new MaterialInput
        {
            LevelId = material.LevelId,
            CourseId = material.CourseId,
            Type = material.Type,
            Title = material.Title,
            Description = material.Description,
            IsPublished = material.IsPublished,
        };
    }

    public async Task<Result<int>> UploadAsync(MaterialInput input, FileUpload file, CancellationToken ct = default)
    {
        if (await ValidateTargetAsync(input, ct) is { } error)
        {
            return error;
        }

        if (FilePolicy.Validate(file, FileCategory.Material) is { } fileError)
        {
            return fileError with { Field = "file" };
        }

        var type = FilePolicy.IsAudio(file.FileName) ? MaterialType.Audio : input.Type;
        var order = await db.Materials.Where(m => m.LevelId == input.LevelId && m.Type == type).MaxAsync(m => (int?)m.SortOrder, ct) ?? 0;

        var material = new LearningMaterial
        {
            LevelId = input.LevelId,
            CourseId = input.CourseId,
            Type = type,
            Title = input.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            FileKey = await storage.SaveAsync(file.Content, "materials", file.Extension, ct),
            FileName = file.SafeFileName,
            ContentType = FilePolicy.ContentTypeFor(file.Extension),
            SizeBytes = file.Length,
            SortOrder = order + 1,
            IsPublished = input.IsPublished,
            UploadedById = user.UserId,
        };

        db.Materials.Add(material);
        await db.SaveChangesAsync(ct);
        return material.Id;
    }

    public async Task<Result> UpdateAsync(int id, MaterialInput input, CancellationToken ct = default)
    {
        var material = await ManageableMaterials().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (material is null)
        {
            return Error.NotFound();
        }

        if (await ValidateTargetAsync(input, ct) is { } error)
        {
            return error;
        }

        if (material.LevelId != input.LevelId || material.Type != input.Type)
        {
            material.SortOrder = (await db.Materials.Where(m => m.LevelId == input.LevelId && m.Type == input.Type)
                .MaxAsync(m => (int?)m.SortOrder, ct) ?? 0) + 1;
        }

        material.LevelId = input.LevelId;
        material.CourseId = input.CourseId;
        material.Type = input.Type;
        material.Title = input.Title.Trim();
        material.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        material.IsPublished = input.IsPublished;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        var material = await ManageableMaterials().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (material is null)
        {
            return Error.NotFound();
        }

        db.Materials.Remove(material);
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(material.FileKey, ct);
        return Result.Success();
    }

    public async Task<Result> MoveAsync(int id, int direction, CancellationToken ct = default)
    {
        var material = await ManageableMaterials().AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (material is null)
        {
            return Error.NotFound();
        }

        var siblings = await db.Materials.Where(m => m.LevelId == material.LevelId && m.Type == material.Type)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Title).ToListAsync(ct);
        var index = siblings.FindIndex(m => m.Id == id);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= siblings.Count)
        {
            return Result.Success();
        }

        (siblings[index], siblings[target]) = (siblings[target], siblings[index]);
        for (var i = 0; i < siblings.Count; i++)
        {
            siblings[i].SortOrder = i + 1;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Error?> ValidateTargetAsync(MaterialInput input, CancellationToken ct)
    {
        if (!await ManageableLevels().AnyAsync(l => l.Id == input.LevelId, ct))
        {
            return Error.Validation("You cannot manage materials for this level.", nameof(MaterialInput.LevelId));
        }

        if (input.CourseId is { } courseId && !await ManageableCoursesOfLevel(input.LevelId).AnyAsync(c => c.Id == courseId, ct))
        {
            return Error.Validation("The course must belong to the selected level.", nameof(MaterialInput.CourseId));
        }

        return null;
    }

    private IQueryable<Level> ManageableLevels()
    {
        if (user.IsAdmin)
        {
            return db.Levels;
        }

        var me = user.UserId;
        return db.Levels.Where(l => l.Courses.Any(c => c.InstructorId == me));
    }

    private IQueryable<Course> ManageableCoursesOfLevel(int levelId)
    {
        var query = db.Courses.Where(c => c.LevelId == levelId);
        if (user.IsAdmin)
        {
            return query;
        }

        var me = user.UserId;
        return query.Where(c => c.InstructorId == me);
    }

    private IQueryable<LearningMaterial> ManageableMaterials()
    {
        if (user.IsAdmin)
        {
            return db.Materials;
        }

        var me = user.UserId;
        return db.Materials.Where(m => m.Level!.Courses.Any(c => c.InstructorId == me) && (m.CourseId == null || m.Course!.InstructorId == me));
    }
}

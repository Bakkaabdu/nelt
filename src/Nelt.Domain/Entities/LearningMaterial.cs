using Nelt.Domain.Common;
using Nelt.Domain.Enums;

namespace Nelt.Domain.Entities;

/// <summary>
/// A book, document or audio recording. Materials always belong to a proficiency level; optionally they are
/// restricted to one course of that level. Students see level-wide materials of every level they study.
/// </summary>
public class LearningMaterial : AuditableEntity
{
    public int LevelId { get; set; }
    public Level? Level { get; set; }
    public int? CourseId { get; set; }
    public Course? Course { get; set; }

    public MaterialType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public string FileKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }

    public int SortOrder { get; set; }
    public bool IsPublished { get; set; } = true;
    public Guid? UploadedById { get; set; }
}

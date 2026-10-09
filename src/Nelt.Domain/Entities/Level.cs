using Nelt.Domain.Common;
using Nelt.Domain.Enums;

namespace Nelt.Domain.Entities;

/// <summary>A proficiency level (e.g. CEFR B1 for German, HSK 3 for Chinese). Rank orders levels within a language.</summary>
public class Level : Entity
{
    public TargetLanguage Language { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Rank { get; set; }
    public LocalizedText Name { get => field ??= new(); set; } = new();
    public LocalizedText Description { get => field ??= new(); set; } = new();

    public ICollection<Course> Courses { get; } = new List<Course>();
    public ICollection<LearningMaterial> Materials { get; } = new List<LearningMaterial>();
}

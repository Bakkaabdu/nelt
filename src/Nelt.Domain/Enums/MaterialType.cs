using System.ComponentModel.DataAnnotations;

namespace Nelt.Domain.Enums;

public enum MaterialType
{
    [Display(Name = "Books")] Book = 1,
    [Display(Name = "Documents")] Document = 2,
    [Display(Name = "Audio recordings")] Audio = 3,
    [Display(Name = "Worksheets")] Worksheet = 4,
    [Display(Name = "Other materials")] Other = 5,
}

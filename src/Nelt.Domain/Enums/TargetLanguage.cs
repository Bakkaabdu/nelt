using System.ComponentModel.DataAnnotations;

namespace Nelt.Domain.Enums;

/// <summary>The language being taught.</summary>
public enum TargetLanguage
{
    [Display(Name = "German")] German = 1,
    [Display(Name = "Chinese")] Chinese = 2,
}

using System.ComponentModel.DataAnnotations;

namespace Nelt.Domain.Enums;

/// <summary>How a course is offered.</summary>
public enum DeliveryMode
{
    [Display(Name = "Online")] Online = 1,
    [Display(Name = "In person")] InPerson = 2,
    [Display(Name = "Online & in person")] Hybrid = 3,
}

/// <summary>How an individual student attends a course.</summary>
public enum StudyMode
{
    [Display(Name = "Online")] Online = 1,
    [Display(Name = "In person")] InPerson = 2,
}

public static class DeliveryModeExtensions
{
    public static bool Allows(this DeliveryMode delivery, StudyMode mode) => delivery switch
    {
        DeliveryMode.Online => mode == StudyMode.Online,
        DeliveryMode.InPerson => mode == StudyMode.InPerson,
        _ => true,
    };
}

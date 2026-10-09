using System.ComponentModel.DataAnnotations;

namespace Nelt.Domain.Enums;

public enum EventType
{
    [Display(Name = "Scheduled visit")] ScheduledVisit = 1,
    [Display(Name = "Conversation session")] ConversationSession = 2,
    [Display(Name = "Delegation visit")] DelegationVisit = 3,
    [Display(Name = "Special guest")] GuestVisit = 4,
    [Display(Name = "Cultural event")] CulturalEvent = 5,
    [Display(Name = "Educational event")] Educational = 6,
}

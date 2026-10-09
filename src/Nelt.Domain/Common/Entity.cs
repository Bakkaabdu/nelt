namespace Nelt.Domain.Common;

/// <summary>Base type for every persisted aggregate/entity. Integer keys keep clustered indexes compact.</summary>
public abstract class Entity
{
    public int Id { get; set; }
}

/// <summary>Entities whose creation/modification timestamps are stamped automatically by the persistence layer.</summary>
public abstract class AuditableEntity : Entity, IAuditable
{
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
}

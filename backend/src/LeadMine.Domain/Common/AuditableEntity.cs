namespace LeadMine.Domain.Common;

/// <summary>
/// Base for entities that track who created/changed them and when.
/// <see cref="Persistence.LeadMineDbContext"/> stamps these automatically on
/// SaveChanges, so callers never set them by hand.
/// </summary>
public abstract class AuditableEntity
{
    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>
    /// Soft-delete marker. Global query filters exclude these rows, so a
    /// deleted record stays auditable without polluting normal reads.
    /// </summary>
    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}

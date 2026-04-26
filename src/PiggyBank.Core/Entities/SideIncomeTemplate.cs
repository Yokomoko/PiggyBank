namespace PiggyBank.Core.Entities;

/// <summary>
/// A reusable template for a recurring side-income job (e.g. "Cleaning shift
/// 2.5h × £18"). Saved per profile; the Side Income view loads them as a
/// dropdown next to the add-entry form so logging a recurring gig is two
/// clicks instead of four typed fields.
/// </summary>
public sealed class SideIncomeTemplate : ProfileOwnedEntity
{
    /// <summary>Short name shown in the dropdown. Required.</summary>
    public required string Name { get; set; }

    /// <summary>Default Description copied into the entry on use.</summary>
    public string? Description { get; set; }

    /// <summary>Default hours. When paired with HourlyRate, the entry's Total
    /// is computed; otherwise FixedTotal seeds the entry directly.</summary>
    public decimal? DurationHours { get; set; }

    public decimal? HourlyRate { get; set; }

    /// <summary>Used for flat-fee templates with no hourly breakdown.</summary>
    public decimal? FixedTotal { get; set; }

    public int SortOrder { get; set; }
}

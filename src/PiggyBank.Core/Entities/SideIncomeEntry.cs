namespace PiggyBank.Core.Entities;

/// <summary>
/// An ad-hoc income event — paid cash work, freelance gig, bonus from a side
/// job, etc. Deliberately decoupled from <see cref="Month"/> so calendar-month
/// reporting stays clean (cash-in-hand often arrives before or after a payday
/// window). The user can then allocate all or part of the <see cref="Total"/>
/// to a <see cref="Pocket"/> (direct deposit) or to the main ledger (creates
/// an income <see cref="Transaction"/> in the payday Month that contains
/// <see cref="PaidOn"/>).
/// </summary>
/// <remarks>
/// <see cref="Total"/> is the source of truth for the entry's value. The
/// optional <see cref="DurationHours"/> + <see cref="HourlyRate"/> pair is
/// just UX sugar so a shift-based earner (cash-in-hand 3h @ £15) can let the
/// UI compute the total; flat fees skip both and enter Total directly.
/// </remarks>
public sealed class SideIncomeEntry : ProfileOwnedEntity
{
    /// <summary>When the money actually landed / was paid over.</summary>
    public required DateOnly PaidOn { get; set; }

    /// <summary>Free-text description of the work. Optional.</summary>
    public string? Description { get; set; }

    /// <summary>Optional hours worked. When paired with <see cref="HourlyRate"/>
    /// the UI computes a suggested Total; the stored Total is authoritative.</summary>
    public decimal? DurationHours { get; set; }

    /// <summary>Optional pay rate. See <see cref="DurationHours"/>.</summary>
    public decimal? HourlyRate { get; set; }

    /// <summary>The total earned for this entry. Always positive.</summary>
    public required decimal Total { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

namespace PiggyBank.Core.Entities;

/// <summary>
/// A pay-cycle period. In the spreadsheet this is a whole worksheet
/// ("Apr-May 25"). In the app it's a row that anchors the payday
/// window, rollover balance, and all the ledger / outgoing entities.
/// </summary>
public sealed class Month : ProfileOwnedEntity
{
    /// <summary>Start of the pay window (typically == LastPayday for monthly cycle).</summary>
    public required DateOnly PeriodStart { get; set; }

    /// <summary>End of the pay window (typically == NextPayday - 1 day).</summary>
    public required DateOnly PeriodEnd { get; set; }

    public required DateOnly LastPayday { get; set; }
    public required DateOnly NextPayday { get; set; }

    /// <summary>B20 in workbook. Carried over from prior month's close, editable.</summary>
    public decimal CarriedOverBalance { get; set; }

    /// <summary>Monthly take-home salary for this pay window. Null = unset
    /// (treated as £0 by the allowance engine). Stored nullable so a cleared
    /// NumberBox doesn't trip validation — the distinction between "deliberate
    /// zero" and "not filled in yet" has no product meaning here.</summary>
    public decimal? MonthlySalary { get; set; }

    public string? Notes { get; set; }

    /// <summary>True once the user runs the "close month" ceremony. Closed months
    /// are read-only from the UI; editing requires explicit unlock.</summary>
    public bool IsClosed { get; set; }
}

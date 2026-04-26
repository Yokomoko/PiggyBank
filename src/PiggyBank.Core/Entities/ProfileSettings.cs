namespace PiggyBank.Core.Entities;

public sealed class ProfileSettings : ProfileOwnedEntity
{
    public decimal DailyFoodBudget { get; set; } = 45m;
    public decimal BufferPerDay { get; set; } = 10m;
    public PayCycle PayCycleDefault { get; set; } = PayCycle.Monthly;

    /// <summary>Day-of-month (1–31) of the primary payday. Configurable
    /// per profile so partners with different paydays each see their own
    /// pay window. If the month has fewer days than this value the
    /// calculator clamps to the last day of the month before applying the
    /// weekend/bank-holiday adjustment.</summary>
    public int PrimaryPaydayDayOfMonth { get; set; } = 25;

    /// <summary>When true, if the nominal payday falls on a Saturday/Sunday or
    /// UK bank holiday the calculator bumps the effective payday BACK to the
    /// previous working day. Matches how most UK employers handle the 24th/25th
    /// rule. Defaults to true.</summary>
    public bool AdjustPaydayForWeekendsAndBankHolidays { get; set; } = true;

    public bool WageVisible { get; set; }              // session-default; runtime toggle lives in VM

    /// <summary>Ballpark tax band for side-income set-aside estimates.
    /// Null defaults to TradingAllowance (£1k tax-free).</summary>
    public TaxBand? SideIncomeTaxBand { get; set; }

    /// <summary>Custom rate (0..1) used when SideIncomeTaxBand == Custom.
    /// Null otherwise.</summary>
    public decimal? SideIncomeTaxCustomRate { get; set; }

    /// <summary>Default recipient name pre-filled in the side-income invoice
    /// dialog. Null on first run; persisted on submit so subsequent invoices
    /// auto-fill it. User can still edit per-click.</summary>
    public string? InvoiceRecipientName { get; set; }

    /// <summary>Default subject prefix for side-income invoice emails. The
    /// dialog renders the full subject as "{prefix} for {Month Year}".
    /// Null on first run; persisted on submit. Falls back to "Hours invoice".</summary>
    public string? InvoiceSubjectPrefix { get; set; }

    /// <summary>Comma-separated list of default <c>To:</c> recipient email
    /// addresses for side-income invoices. Persisted on submit so the user
    /// doesn't have to retype them. The first address (or the user's edit)
    /// drives the mailto: target; the rest become additional <c>To:</c>
    /// recipients. Null on first run.</summary>
    public string? InvoiceToEmails { get; set; }

    /// <summary>Comma-separated list of default <c>Cc:</c> email addresses
    /// for side-income invoices. Same persistence rules as
    /// <see cref="InvoiceToEmails"/>. Null on first run.</summary>
    public string? InvoiceCcEmails { get; set; }
}

/// <summary>Ballpark UK tax bands used by the side-income set-aside
/// estimator. Rates combine income tax + Class 4 NI for self-employed
/// side gigs. Real HMRC bracket maths is more nuanced (personal
/// allowance interactions, allowable expenses, etc.) — this is a
/// useful "set this aside" rule of thumb, nothing more.</summary>
public enum TaxBand
{
    /// <summary>£1,000 trading allowance applies — no tax owed below it.
    /// Above the threshold we apply basic rate to the excess.</summary>
    TradingAllowance = 0,

    /// <summary>20% income tax + 6% Class 4 NI = 26%.</summary>
    Basic = 1,

    /// <summary>40% + 2% NI = 42%.</summary>
    Higher = 2,

    /// <summary>45% + 2% NI = 47%.</summary>
    Additional = 3,

    /// <summary>User-supplied rate via SideIncomeTaxCustomRate.</summary>
    Custom = 99,
}

public enum PayCycle
{
    Monthly = 0,
    Weekly = 1,
    Fortnightly = 2,
    FourWeekly = 3,
}

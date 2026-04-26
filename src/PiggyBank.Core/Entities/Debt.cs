namespace PiggyBank.Core.Entities;

/// <summary>
/// A single tracked debt — credit card, loan, finance agreement, etc.
/// The current balance lives on <see cref="DebtSnapshot"/> rows, not here:
/// one snapshot per month (or ad-hoc) keeps a history that a per-Debt
/// chart can render. Only <see cref="Limit"/> (relevant for credit cards)
/// and metadata stay on the Debt itself.
/// </summary>
public sealed class Debt : ProfileOwnedEntity
{
    public required string Name { get; set; }
    public required DebtKind Kind { get; set; }

    /// <summary>Credit limit for a card. Null for loans and other fixed debt.</summary>
    public decimal? Limit { get; set; }

    /// <summary>APR as a decimal (e.g. 0.199 for 19.9%). Used by a future
    /// snowball/avalanche simulator. Null means "unknown".</summary>
    public decimal? AnnualPercentageRate { get; set; }

    /// <summary>Contractual minimum monthly payment. Fixed for loans and
    /// finance; for credit cards it's usually % of balance but we let the
    /// user fix a £ figure (matches how people mentally plan).</summary>
    public decimal? MinimumMonthlyPayment { get; set; }

    /// <summary>Scheduled monthly overpayment the user always makes on top
    /// of the minimum (e.g. "I pay an extra £70 every month on top of the
    /// contractual minimum"). Feeds the simulator so projections match
    /// reality.</summary>
    public decimal? ScheduledOverpayment { get; set; }

    /// <summary>Many UK personal loans and mortgages charge a deferred
    /// interest penalty on overpayments — typically 60 days' worth of interest
    /// on the overpaid amount. Null = no penalty (most credit cards, modern
    /// regulated loans). 60 = the common UK personal-loan default. Applied
    /// in the simulator to make overpayment projections realistic rather
    /// than a too-good-to-be-true pace.</summary>
    public int? OverpaymentInterestPenaltyDays { get; set; }

    /// <summary>Opening balance recorded when the user first added the debt
    /// (negative = owed). Acts as the seed for the history chart before
    /// any snapshots exist.</summary>
    public required decimal OpeningBalance { get; set; }

    /// <summary>Free-form notes the user attaches (account number last four,
    /// statement date, etc.).</summary>
    public string? Notes { get; set; }

    /// <summary>Soft delete — hides from the dashboard but keeps history.
    /// Cleared debts should be archived, not hard-deleted.</summary>
    public DateTime? ArchivedAtUtc { get; set; }

    public int SortOrder { get; set; }
}

public enum DebtKind
{
    CreditCard = 0,
    Loan = 1,
    Finance = 2,  // e.g. car finance
    Mortgage = 3,
    Other = 99,
}

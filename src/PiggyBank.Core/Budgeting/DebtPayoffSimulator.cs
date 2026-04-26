namespace PiggyBank.Core.Budgeting;

/// <summary>
/// Simulates a debt payoff under two classic strategies:
/// <list type="bullet">
///   <item><b>Avalanche</b> — attack highest-APR first (math-optimal: least interest paid).</item>
///   <item><b>Snowball</b> — attack smallest-balance first (psychology-optimal: quick wins keep you going).</item>
/// </list>
/// Per-debt inputs are explicit so a real personal loan with a fixed
/// overpayment ("I pay £70 a month over the minimum") simulates
/// accurately. When a debt clears its combined <c>Min + Overpayment</c>
/// rolls into the extra pool for the remaining targets — this is the
/// classic "snowball" of freed-up cashflow.
/// </summary>
public sealed class DebtPayoffSimulator
{
    /// <summary>Maximum months to simulate. Pathological input (min+extra = 0
    /// with APR &gt; 0) would loop forever otherwise.</summary>
    public const int MaxMonths = 600;  // 50 years

    public DebtPayoffPlan RunAvalanche(IEnumerable<PayoffDebt> debts, decimal extraMonthly)
        => Run(debts, extraMonthly, PayoffStrategy.Avalanche);

    public DebtPayoffPlan RunSnowball(IEnumerable<PayoffDebt> debts, decimal extraMonthly)
        => Run(debts, extraMonthly, PayoffStrategy.Snowball);

    private static DebtPayoffPlan Run(
        IEnumerable<PayoffDebt> input,
        decimal extraMonthly,
        PayoffStrategy strategy)
    {
        var debts = input
            .Where(d => d.Balance > 0m)
            .Select(d => new SimState(d))
            .ToList();
        if (debts.Count == 0)
            return new DebtPayoffPlan(strategy, 0, 0m, Array.Empty<DebtPayoffStep>());

        var timeline = new List<DebtPayoffStep>();
        decimal totalInterest = 0m;
        decimal freedRollover = 0m;  // min+overpayment of cleared debts flows here

        for (int month = 1; month <= MaxMonths; month++)
        {
            // 1) Accrue this month's interest on every outstanding debt.
            foreach (var d in debts.Where(x => x.Balance > 0m))
            {
                var monthlyInterest = Math.Round(
                    d.Balance * (d.Source.AprFraction / 12m),
                    2,
                    MidpointRounding.AwayFromZero);
                d.Balance += monthlyInterest;
                d.InterestPaid += monthlyInterest;
                totalInterest += monthlyInterest;
            }

            // 2) Pay minimums on every outstanding debt — these are contractual
            //    and don't trigger the overpayment penalty.
            foreach (var d in debts.Where(x => x.Balance > 0m))
            {
                var pay = Math.Min(d.Source.MinimumMonthly, d.Balance);
                d.Balance -= pay;
            }

            // 3) Pay the scheduled monthly overpayment on every outstanding debt.
            //    Overpayments DO trigger the penalty (if set) — that's the 60-day
            //    interest-on-overpayment charge many UK loans apply.
            foreach (var d in debts.Where(x => x.Balance > 0m))
            {
                if (d.Source.ScheduledOverpayment <= 0m) continue;
                var pay = Math.Min(d.Source.ScheduledOverpayment, d.Balance);
                var penalty = PenaltyFor(d.Source, pay);
                d.Balance -= pay;
                d.Balance += penalty;      // bank tacks it back on
                d.InterestPaid += penalty;
                totalInterest += penalty;
            }

            // 4) Extra attack pool = user's free budget + rolled-over freed payments.
            //    All of it goes to the priority target. Also triggers the penalty.
            var attack = extraMonthly + freedRollover;
            while (attack > 0m)
            {
                var target = Prioritise(debts.Where(x => x.Balance > 0m), strategy);
                if (target is null) break;
                var pay = Math.Min(attack, target.Balance);
                var penalty = PenaltyFor(target.Source, pay);
                target.Balance -= pay;
                target.Balance += penalty;
                target.InterestPaid += penalty;
                totalInterest += penalty;
                attack -= pay;
            }

            // 4) Any debt that just cleared this cycle frees its min+overpayment
            //    to the rollover pool for future months.
            foreach (var d in debts.Where(x => x.Balance <= 0m && !x.Freed))
            {
                freedRollover += d.Source.MinPlusOverpayment;
                d.Freed = true;
            }

            var outstanding = debts.Sum(x => x.Balance);
            timeline.Add(new DebtPayoffStep(month, outstanding, totalInterest));

            if (outstanding <= 0m)
                return new DebtPayoffPlan(strategy, month, totalInterest, timeline);
        }

        // Didn't clear within cap — caller can flag it.
        return new DebtPayoffPlan(strategy, MaxMonths, totalInterest, timeline);
    }

    /// <summary>Simple interest on the overpaid amount for the debt's
    /// configured penalty window (typically 60 days for UK loans).
    /// Zero when no penalty is set — most credit cards, modern regulated loans.</summary>
    private static decimal PenaltyFor(PayoffDebt d, decimal overpayment)
    {
        if (d.OverpaymentInterestPenaltyDays is not (int days) || days <= 0 || overpayment <= 0m)
            return 0m;
        return Math.Round(
            overpayment * d.AprFraction * days / 365m,
            2,
            MidpointRounding.AwayFromZero);
    }

    private static SimState? Prioritise(IEnumerable<SimState> debts, PayoffStrategy strategy)
        => strategy switch
        {
            PayoffStrategy.Avalanche => debts.OrderByDescending(d => d.Source.AprFraction).FirstOrDefault(),
            PayoffStrategy.Snowball  => debts.OrderBy(d => d.Balance).FirstOrDefault(),
            _ => null,
        };

    private sealed class SimState(PayoffDebt source)
    {
        public PayoffDebt Source { get; } = source;
        public decimal Balance { get; set; } = source.Balance;
        public decimal InterestPaid { get; set; }
        /// <summary>True once this debt's cashflow has been handed to the rollover pool.</summary>
        public bool Freed { get; set; }
    }
}

public enum PayoffStrategy
{
    Avalanche = 0,  // highest APR first
    Snowball = 1,   // smallest balance first
}

/// <summary>Input to the simulator. All values are positive magnitudes.</summary>
public sealed record PayoffDebt(
    string Name,
    decimal Balance,
    decimal AprFraction,                      // 0.199 = 19.9%
    decimal MinimumMonthly,                   // contractual minimum (no penalty)
    decimal ScheduledOverpayment,             // fixed monthly overpayment (penalty applies)
    int? OverpaymentInterestPenaltyDays = null // null = no penalty; UK personal loans typically 60
)
{
    public decimal MinPlusOverpayment => MinimumMonthly + ScheduledOverpayment;
}

public sealed record DebtPayoffStep(int Month, decimal OutstandingBalance, decimal TotalInterestPaid);

public sealed record DebtPayoffPlan(
    PayoffStrategy Strategy,
    int MonthsToClear,            // 0 if input empty; MaxMonths if didn't clear
    decimal TotalInterestPaid,
    IReadOnlyList<DebtPayoffStep> Timeline);

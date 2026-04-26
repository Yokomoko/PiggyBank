using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

public interface IDebtRepository
{
    /// <summary>Returns non-archived debts ordered by <see cref="Debt.SortOrder"/>
    /// then name. Pass <paramref name="includeArchived"/>=true for the Settings
    /// page.</summary>
    Task<IReadOnlyList<Debt>> ListAsync(bool includeArchived = false, CancellationToken ct = default);

    Task<Debt?> FindAsync(Guid id, CancellationToken ct = default);
    Task<Debt> AddAsync(Debt debt, CancellationToken ct = default);
    Task UpdateAsync(Debt debt, CancellationToken ct = default);
    Task ArchiveAsync(Guid id, CancellationToken ct = default);

    /// <summary>Latest snapshot per debt — what the dashboard renders.
    /// Returns the opening balance when no snapshots exist so the UI never
    /// has a blank row.</summary>
    Task<IReadOnlyList<DebtWithLatestBalance>> ListWithLatestBalancesAsync(
        bool includeArchived = false, CancellationToken ct = default);

    Task<IReadOnlyList<DebtSnapshot>> ListSnapshotsAsync(Guid debtId, CancellationToken ct = default);
    Task<DebtSnapshot> AddSnapshotAsync(DebtSnapshot snapshot, CancellationToken ct = default);
    Task DeleteSnapshotAsync(Guid snapshotId, CancellationToken ct = default);
}

public sealed record DebtWithLatestBalance(
    Debt Debt,
    decimal LatestBalance,
    DateOnly? LatestSnapshotDate);

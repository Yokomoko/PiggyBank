using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

/// <summary>
/// Reads/writes the cross-profile joint tables. These bypass the
/// global tenant filter by design — joint data is shared across every
/// profile in the household. The repository is registered as scoped
/// like the others; it just doesn't depend on a particular profile
/// scope being open. Either profile's session sees the same rows.
/// </summary>
public interface IJointRepository
{
    // --- Accounts ---
    Task<IReadOnlyList<JointAccount>> ListAccountsAsync(
        bool includeArchived = false, CancellationToken ct = default);

    Task<JointAccount?> FindAccountAsync(Guid id, CancellationToken ct = default);
    Task<JointAccount> AddAccountAsync(JointAccount account, CancellationToken ct = default);
    Task UpdateAccountAsync(JointAccount account, CancellationToken ct = default);
    Task ArchiveAccountAsync(Guid id, CancellationToken ct = default);

    // --- Contributions ---
    Task<IReadOnlyList<JointContribution>> ListContributionsAsync(
        Guid accountId, CancellationToken ct = default);
    Task<JointContribution> AddContributionAsync(JointContribution contribution, CancellationToken ct = default);
    Task UpdateContributionAsync(JointContribution contribution, CancellationToken ct = default);
    Task DeleteContributionAsync(Guid id, CancellationToken ct = default);

    // --- Outgoings ---
    Task<IReadOnlyList<JointOutgoing>> ListOutgoingsAsync(
        Guid accountId, CancellationToken ct = default);
    Task<JointOutgoing> AddOutgoingAsync(JointOutgoing outgoing, CancellationToken ct = default);
    Task UpdateOutgoingAsync(JointOutgoing outgoing, CancellationToken ct = default);
    Task DeleteOutgoingAsync(Guid id, CancellationToken ct = default);
}

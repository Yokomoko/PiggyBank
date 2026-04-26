using Microsoft.EntityFrameworkCore;
using PiggyBank.Core.Entities;

namespace PiggyBank.Data.Repositories;

/// <summary>
/// Implementation of <see cref="IJointRepository"/>. None of these
/// queries call <c>IgnoreQueryFilters</c> because the joint entities
/// are NOT <see cref="ProfileOwnedEntity"/> and therefore have no
/// global filter to bypass. They're visible from every profile scope
/// (and the admin scope) by construction.
/// </summary>
public sealed class JointRepository(AppDbContext db, TimeProvider clock) : IJointRepository
{
    public async Task<IReadOnlyList<JointAccount>> ListAccountsAsync(
        bool includeArchived = false, CancellationToken ct = default)
    {
        var q = db.JointAccounts.AsQueryable();
        if (!includeArchived) q = q.Where(a => a.ArchivedAtUtc == null);
        return await q.OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToListAsync(ct);
    }

    public Task<JointAccount?> FindAccountAsync(Guid id, CancellationToken ct = default)
        => db.JointAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<JointAccount> AddAccountAsync(JointAccount account, CancellationToken ct = default)
    {
        db.JointAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account;
    }

    public async Task UpdateAccountAsync(JointAccount account, CancellationToken ct = default)
    {
        db.JointAccounts.Update(account);
        await db.SaveChangesAsync(ct);
    }

    public async Task ArchiveAccountAsync(Guid id, CancellationToken ct = default)
    {
        var a = await db.JointAccounts.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException($"JointAccount {id} not found.");
        a.ArchivedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<JointContribution>> ListContributionsAsync(
        Guid accountId, CancellationToken ct = default)
    {
        return await db.JointContributions
            .Where(c => c.JointAccountId == accountId)
            .ToListAsync(ct);
    }

    public async Task<JointContribution> AddContributionAsync(JointContribution contribution, CancellationToken ct = default)
    {
        contribution.ModifiedAtUtc = clock.GetUtcNow().UtcDateTime;
        db.JointContributions.Add(contribution);
        await db.SaveChangesAsync(ct);
        return contribution;
    }

    public async Task UpdateContributionAsync(JointContribution contribution, CancellationToken ct = default)
    {
        contribution.ModifiedAtUtc = clock.GetUtcNow().UtcDateTime;
        db.JointContributions.Update(contribution);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteContributionAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.JointContributions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return;
        db.JointContributions.Remove(c);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<JointOutgoing>> ListOutgoingsAsync(
        Guid accountId, CancellationToken ct = default)
    {
        return await db.JointOutgoings
            .Where(o => o.JointAccountId == accountId)
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Name)
            .ToListAsync(ct);
    }

    public async Task<JointOutgoing> AddOutgoingAsync(JointOutgoing outgoing, CancellationToken ct = default)
    {
        db.JointOutgoings.Add(outgoing);
        await db.SaveChangesAsync(ct);
        return outgoing;
    }

    public async Task UpdateOutgoingAsync(JointOutgoing outgoing, CancellationToken ct = default)
    {
        db.JointOutgoings.Update(outgoing);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteOutgoingAsync(Guid id, CancellationToken ct = default)
    {
        var o = await db.JointOutgoings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return;
        db.JointOutgoings.Remove(o);
        await db.SaveChangesAsync(ct);
    }
}

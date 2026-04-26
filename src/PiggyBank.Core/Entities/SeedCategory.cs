namespace PiggyBank.Core.Entities;

/// <summary>
/// System-wide read-only list of categories shown in the create-profile
/// wizard. Not profile-owned — the wizard COPIES checked rows into
/// <see cref="Category"/>, which is profile-owned.
/// </summary>
public sealed class SeedCategory
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required CategoryKind Kind { get; set; }
    public int SortOrder { get; set; }
    public bool DefaultEnabled { get; set; } = true;
}

public enum CategoryKind
{
    Spend = 0,
    Income = 1,
    Overpayment = 2,
    Savings = 3,
}

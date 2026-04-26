namespace PiggyBank.Core.Entities;

public sealed class Category : ProfileOwnedEntity
{
    public required string Name { get; set; }
    public required CategoryKind Kind { get; set; }
    public string ColourHex { get; set; } = "#64748B";
    public bool IsArchived { get; set; }
    public int? SourceSeedCategoryId { get; set; }
}

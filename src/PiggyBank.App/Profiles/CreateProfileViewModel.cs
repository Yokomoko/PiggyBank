using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.Core.Entities;
using PiggyBank.Data;
using PiggyBank.Data.Profiles;

namespace PiggyBank.App.Profiles;

public sealed partial class CreateProfileViewModel(IProfileSessionManager sessions) : ObservableObject
{
    private readonly IProfileSessionManager _sessions = sessions;

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _colourHex = "#3B82F6";   // default slate-blue

    [ObservableProperty]
    private string _iconKey = "person";

    [ObservableProperty]
    private int _primaryPaydayDayOfMonth = 25;

    [ObservableProperty]
    private bool _adjustPaydayForWeekendsAndBankHolidays = true;

    public ObservableCollection<SeedCategoryChoice> SeedCategories { get; } = [];

    /// <summary>Raised with the new profile id when creation succeeds.</summary>
    public event EventHandler<Guid>? ProfileCreated;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _sessions.EnsureInitialisedAsync(ct);
        using var admin = _sessions.OpenAdminScope();
        var db = admin.Services.GetRequiredService<AppDbContext>();
        var seeds = await db.SeedCategories.OrderBy(s => s.SortOrder).ToListAsync(ct);

        SeedCategories.Clear();
        foreach (var s in seeds)
        {
            SeedCategories.Add(new SeedCategoryChoice
            {
                Id = s.Id,
                Name = s.Name,
                Kind = s.Kind,
                IsSelected = s.DefaultEnabled,
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync(CancellationToken ct)
    {
        using var admin = _sessions.OpenAdminScope();
        var service = admin.Services.GetRequiredService<ProfileAdminService>();
        var selectedIds = SeedCategories.Where(s => s.IsSelected).Select(s => s.Id);

        var profile = await service.CreateAsync(
            DisplayName,
            ColourHex,
            IconKey,
            selectedIds,
            new ProfileSettingsInput(
                PrimaryPaydayDayOfMonth,
                AdjustPaydayForWeekendsAndBankHolidays),
            ct);

        ProfileCreated?.Invoke(this, profile.Id);
    }

    private bool CanCreate() => !string.IsNullOrWhiteSpace(DisplayName);

    partial void OnDisplayNameChanged(string value) => CreateCommand.NotifyCanExecuteChanged();
}

public sealed partial class SeedCategoryChoice : ObservableObject
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required CategoryKind Kind { get; init; }

    [ObservableProperty]
    private bool _isSelected;
}

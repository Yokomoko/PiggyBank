using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.Core.Entities;
using PiggyBank.Data.Profiles;

namespace PiggyBank.App.Profiles;

public sealed partial class ProfilePickerViewModel(IProfileSessionManager sessions) : ObservableObject
{
    private readonly IProfileSessionManager _sessions = sessions;

    public ObservableCollection<Profile> Profiles { get; } = [];

    [ObservableProperty]
    private Profile? _selected;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Raised when the user picks a profile — the View listens and closes itself.</summary>
    public event EventHandler<Guid>? ProfileChosen;

    /// <summary>Raised when the user asks to create a new profile.</summary>
    public event EventHandler? CreateProfileRequested;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            await _sessions.EnsureInitialisedAsync(ct);

            using var admin = _sessions.OpenAdminScope();
            var service = admin.Services.GetRequiredService<ProfileAdminService>();
            var list = await service.ListAsync(ct);

            Profiles.Clear();
            foreach (var p in list) Profiles.Add(p);
            Selected = Profiles.FirstOrDefault();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Open()
    {
        if (Selected is null) return;
        ProfileChosen?.Invoke(this, Selected.Id);
    }

    [RelayCommand]
    private void CreateProfile() => CreateProfileRequested?.Invoke(this, EventArgs.Empty);
}

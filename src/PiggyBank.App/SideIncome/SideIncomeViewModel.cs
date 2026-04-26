using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PiggyBank.App.Profiles;
using PiggyBank.Core.Budgeting;
using PiggyBank.Core.Entities;
using PiggyBank.Data;
using PiggyBank.Data.Repositories;
using PiggyBank.Data.Services;

namespace PiggyBank.App.SideIncome;

public sealed partial class SideIncomeViewModel(
    IProfileSessionManager sessions,
    TimeProvider clock) : ObservableObject
{
    private readonly IProfileSessionManager _sessions = sessions;
    private readonly TimeProvider _clock = clock;
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    public ObservableCollection<SideIncomeMonthRow> Months { get; } = [];
    public ObservableCollection<TaxYearSummary> TaxYears { get; } = [];
    public ObservableCollection<SideIncomeTemplate> Templates { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasEntries;
    [ObservableProperty] private bool _hasTemplates;
    [ObservableProperty] private decimal _totalEarned;
    [ObservableProperty] private decimal _totalUnallocated;

    // --- Tax band picker (saved to ProfileSettings) ---
    public IReadOnlyList<TaxBandOption> TaxBands { get; } = TaxBandOption.All;
    [ObservableProperty] private TaxBandOption _selectedTaxBand = TaxBandOption.All[0];
    [ObservableProperty] private decimal? _customTaxRatePercent;
    [ObservableProperty] private bool _isCustomBand;

    // --- Add-entry form ---
    [ObservableProperty] private DateOnly _newPaidOn = DateOnly.FromDateTime(DateTime.Today);
    [ObservableProperty] private string _newDescription = "";
    [ObservableProperty] private decimal? _newDurationHours;
    [ObservableProperty] private decimal? _newHourlyRate;
    [ObservableProperty] private decimal? _newTotal;

    /// <summary>Raised when the user asks to allocate an entry. View hosts the modal.</summary>
    public event EventHandler<SideIncomeEntryRow>? AllocateRequested;

    /// <summary>Raised when the user asks to bulk-allocate an entire calendar
    /// month's remaining. View hosts the modal.</summary>
    public event EventHandler<SideIncomeMonthRow>? AllocateMonthRequested;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        IsBusy = true;
        try
        {
            var scope = _sessions.Current.Services;
            var repo = scope.GetRequiredService<ISideIncomeRepository>();
            var entries = await repo.ListEntriesAsync(ct);
            var allocations = await repo.ListAllocationsAsync(ct);

            var groups = SideIncomeMath.GroupByCalendarMonth(entries, allocations);
            Months.Clear();
            foreach (var group in groups)
            {
                var row = new SideIncomeMonthRow(group);
                foreach (var entry in group.Entries)
                {
                    var remaining = SideIncomeMath.RemainingFor(entry, allocations);
                    row.Entries.Add(new SideIncomeEntryRow(entry, remaining));
                }
                Months.Add(row);
            }

            HasEntries = entries.Count > 0;
            TotalEarned = entries.Sum(e => e.Total);
            TotalUnallocated = groups.Sum(g => g.Remaining);

            Templates.Clear();
            foreach (var t in await repo.ListTemplatesAsync(ct))
                Templates.Add(t);
            HasTemplates = Templates.Count > 0;

            // Tax band: load from ProfileSettings (persisted choice).
            var db = scope.GetRequiredService<AppDbContext>();
            var settings = await db.ProfileSettings.FirstOrDefaultAsync(ct);
            var band = settings?.SideIncomeTaxBand ?? TaxBand.TradingAllowance;
            SelectedTaxBand = TaxBandOption.All.First(o => o.Band == band);
            CustomTaxRatePercent = settings?.SideIncomeTaxCustomRate is decimal r ? r * 100m : null;
            IsCustomBand = band == TaxBand.Custom;

            RecalculateTaxYears(entries);
        }
        finally { IsBusy = false; }
    }

    private void RecalculateTaxYears(IReadOnlyList<SideIncomeEntry> entries)
    {
        TaxYears.Clear();
        var customFraction = CustomTaxRatePercent is decimal pct ? pct / 100m : (decimal?)null;
        foreach (var s in TaxYearMath.SummariseByTaxYear(entries, SelectedTaxBand.Band, customFraction))
            TaxYears.Add(s);
    }

    /// <summary>Reapply tax estimate when the user picks a new band, and
    /// persist the choice to ProfileSettings so it sticks across sessions.</summary>
    partial void OnSelectedTaxBandChanged(TaxBandOption value)
    {
        IsCustomBand = value.Band == TaxBand.Custom;
        _ = PersistTaxBandAsync(value.Band, CustomTaxRatePercent);
    }

    partial void OnCustomTaxRatePercentChanged(decimal? value)
    {
        if (SelectedTaxBand.Band == TaxBand.Custom)
            _ = PersistTaxBandAsync(TaxBand.Custom, value);
    }

    private async Task PersistTaxBandAsync(TaxBand band, decimal? customPct)
    {
        if (_sessions.Current is null) return;
        var db = _sessions.Current.Services.GetRequiredService<AppDbContext>();
        var settings = await db.ProfileSettings.FirstOrDefaultAsync()
            ?? new ProfileSettings { ProfileId = _sessions.Current.ProfileId };
        var existed = settings.Id != Guid.Empty;
        settings.SideIncomeTaxBand = band;
        settings.SideIncomeTaxCustomRate = customPct is decimal pct ? pct / 100m : null;
        if (!existed) db.ProfileSettings.Add(settings);
        await db.SaveChangesAsync();

        // Refresh in-memory rollup so the cards show the new estimate.
        var repo = _sessions.Current.Services.GetRequiredService<ISideIncomeRepository>();
        var entries = await repo.ListEntriesAsync();
        RecalculateTaxYears(entries);
    }

    [RelayCommand(CanExecute = nameof(CanAddEntry))]
    public async Task AddEntryAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;
        var repo = _sessions.Current.Services.GetRequiredService<ISideIncomeRepository>();

        // If user filled both duration + rate, let those drive the total.
        // Otherwise the manually-entered Total is authoritative.
        var total = SideIncomeMath.SuggestedTotal(NewDurationHours, NewHourlyRate) ?? NewTotal ?? 0m;
        if (total <= 0m) return;

        await repo.AddEntryAsync(new SideIncomeEntry
        {
            PaidOn = NewPaidOn,
            Description = string.IsNullOrWhiteSpace(NewDescription) ? null : NewDescription.Trim(),
            DurationHours = NewDurationHours,
            HourlyRate = NewHourlyRate,
            Total = total,
        }, ct);

        NewDescription = "";
        NewDurationHours = null;
        NewHourlyRate = null;
        NewTotal = null;
        await LoadAsync(ct);
    }

    private bool CanAddEntry()
        => SideIncomeMath.SuggestedTotal(NewDurationHours, NewHourlyRate) is > 0m
           || NewTotal is > 0m;

    partial void OnNewDurationHoursChanged(decimal? value)
    {
        AutoFillTotal();
        AddEntryCommand.NotifyCanExecuteChanged();
    }
    partial void OnNewHourlyRateChanged(decimal? value)
    {
        AutoFillTotal();
        AddEntryCommand.NotifyCanExecuteChanged();
    }
    partial void OnNewTotalChanged(decimal? value) => AddEntryCommand.NotifyCanExecuteChanged();

    /// <summary>Live-compute Total when both Hours and Rate are filled.
    /// Leaves any user-typed Total alone when one input is cleared, since
    /// that's usually a mid-edit state, not "I want Total set to nothing".</summary>
    private void AutoFillTotal()
    {
        var computed = SideIncomeMath.SuggestedTotal(NewDurationHours, NewHourlyRate);
        if (computed is not null) NewTotal = computed;
    }

    [RelayCommand]
    public void Allocate(SideIncomeEntryRow? row)
    {
        if (row is null) return;
        AllocateRequested?.Invoke(this, row);
    }

    [RelayCommand]
    public void AllocateMonth(SideIncomeMonthRow? row)
    {
        if (row is null || row.Remaining <= 0m) return;
        AllocateMonthRequested?.Invoke(this, row);
    }

    /// <summary>Builds a plain-text invoice body for the given month and
    /// opens the user's default mail client via mailto:. Recipient name,
    /// To/Cc lists and subject prompted on click and persisted to
    /// <see cref="ProfileSettings"/> so subsequent months auto-fill.
    /// Signoff defaults to the current profile's DisplayName. mailto's
    /// body is plain text only — the HTML hours table goes via the
    /// separate "Copy hours" button onto the clipboard.</summary>
    [RelayCommand]
    public async Task EmailMonthAsync(SideIncomeMonthRow? row)
    {
        if (row is null || row.Entries.Count == 0) return;
        if (_sessions.Current is null) return;

        // Defaults come from ProfileSettings — first-time users get the
        // generic placeholders below; once they submit the dialog the
        // typed values overwrite the saved defaults.
        var db = _sessions.Current.Services.GetRequiredService<AppDbContext>();
        var settings = await db.ProfileSettings.FirstOrDefaultAsync();
        var savedRecipient = settings?.InvoiceRecipientName;
        var savedPrefix = settings?.InvoiceSubjectPrefix ?? "Hours invoice";
        var savedTo = settings?.InvoiceToEmails ?? "";
        var savedCc = settings?.InvoiceCcEmails ?? "";
        var defaultSubject = $"{savedPrefix} for {row.MonthLabel}";

        var dialog = new System.Windows.Window
        {
            Title = "Compose invoice email",
            Width = 480, Height = 360,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            Owner = System.Windows.Application.Current.MainWindow,
            ResizeMode = System.Windows.ResizeMode.NoResize,
        };
        var stack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(16) };

        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Recipient name:",
            Margin = new System.Windows.Thickness(0, 0, 0, 4),
        });
        var nameBox = new System.Windows.Controls.TextBox
        {
            Text = savedRecipient ?? "",
            Margin = new System.Windows.Thickness(0, 0, 0, 10),
        };
        stack.Children.Add(nameBox);

        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "To: (comma-separated)",
            Margin = new System.Windows.Thickness(0, 0, 0, 4),
        });
        var toBox = new System.Windows.Controls.TextBox
        {
            Text = savedTo,
            Margin = new System.Windows.Thickness(0, 0, 0, 10),
        };
        stack.Children.Add(toBox);

        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Cc: (comma-separated, optional)",
            Margin = new System.Windows.Thickness(0, 0, 0, 4),
        });
        var ccBox = new System.Windows.Controls.TextBox
        {
            Text = savedCc,
            Margin = new System.Windows.Thickness(0, 0, 0, 10),
        };
        stack.Children.Add(ccBox);

        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Subject:",
            Margin = new System.Windows.Thickness(0, 0, 0, 4),
        });
        var subjectBox = new System.Windows.Controls.TextBox { Text = defaultSubject };
        stack.Children.Add(subjectBox);

        var ok = new System.Windows.Controls.Button
        {
            Content = "Compose email",
            Margin = new System.Windows.Thickness(0, 12, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Padding = new System.Windows.Thickness(12, 4, 12, 4),
            IsDefault = true,
        };
        ok.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        stack.Children.Add(ok);
        dialog.Content = stack;
        nameBox.Focus();
        nameBox.SelectAll();
        if (dialog.ShowDialog() != true) return;

        var recipient = string.IsNullOrWhiteSpace(nameBox.Text) ? "there" : nameBox.Text.Trim();
        var subject = string.IsNullOrWhiteSpace(subjectBox.Text) ? defaultSubject : subjectBox.Text.Trim();
        var toAddrs = NormaliseEmailList(toBox.Text);
        var ccAddrs = NormaliseEmailList(ccBox.Text);

        // Persist what the user just used as the new defaults. Subject prefix
        // = subject minus the " for {MonthLabel}" suffix when it ends with it,
        // so next month auto-rebuilds correctly. Otherwise save the whole
        // string and accept that next month will pre-fill that as a starting
        // point (one-edit cost).
        var monthSuffix = $" for {row.MonthLabel}";
        var prefixToSave = subject.EndsWith(monthSuffix, StringComparison.Ordinal)
            ? subject[..^monthSuffix.Length]
            : subject;
        if (settings is null)
        {
            settings = new ProfileSettings { ProfileId = _sessions.Current.ProfileId };
            db.ProfileSettings.Add(settings);
        }
        settings.InvoiceRecipientName = recipient == "there" ? null : recipient;
        settings.InvoiceSubjectPrefix = prefixToSave;
        settings.InvoiceToEmails = toAddrs.Count == 0 ? null : string.Join(", ", toAddrs);
        settings.InvoiceCcEmails = ccAddrs.Count == 0 ? null : string.Join(", ", ccAddrs);
        await db.SaveChangesAsync();

        // Signoff = current profile DisplayName (or fallback).
        var signoff = db.Profiles
            .FirstOrDefault(p => p.Id == _sessions.Current.ProfileId)?.DisplayName ?? "";

        var body = ComposeInvoiceBody(row, recipient, signoff);
        var mailto = BuildMailtoUrl(toAddrs, ccAddrs, subject, body);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(mailto)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Could not open the default mail client.\n\n{ex.Message}",
                "Email", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>Splits a comma- or semicolon-separated list of email
    /// addresses, trims whitespace, drops blanks. Order preserved.
    /// No format validation — the user knows their own contacts and the
    /// mail client will reject bad addresses on send anyway.</summary>
    private static List<string> NormaliseEmailList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        return raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    /// <summary>Builds an RFC 6068 mailto: URL. Multiple <c>To:</c> live
    /// in the path (comma-separated, percent-encoded); <c>Cc:</c> rides
    /// in the query string. Subject and body are query parameters.</summary>
    private static string BuildMailtoUrl(
        IReadOnlyList<string> to,
        IReadOnlyList<string> cc,
        string subject,
        string body)
    {
        var path = string.Join(",", to.Select(Uri.EscapeDataString));
        var query = new List<string>(3);
        if (cc.Count > 0)
            query.Add("cc=" + string.Join(",", cc.Select(Uri.EscapeDataString)));
        query.Add("subject=" + Uri.EscapeDataString(subject));
        query.Add("body=" + Uri.EscapeDataString(body));
        return "mailto:" + path + "?" + string.Join("&", query);
    }

    private static string ComposeInvoiceBody(SideIncomeMonthRow row, string recipient, string signoff)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Hello {recipient},");
        sb.AppendLine();
        sb.AppendLine($"Hope you're well, please find my hours for {row.MonthLabel} below.");
        sb.AppendLine();
        sb.AppendLine("[paste hours table here]");
        sb.AppendLine();
        sb.AppendLine("Kind regards,");
        if (!string.IsNullOrWhiteSpace(signoff)) sb.AppendLine(signoff);
        return sb.ToString();
    }

    /// <summary>Builds an HTML hours table and pushes it onto the clipboard
    /// in the Windows CF_HTML format. Outlook / Gmail / Thunderbird all
    /// honour CF_HTML and paste the table preserving the row structure
    /// (and basic borders / cell padding).</summary>
    [RelayCommand]
    public void CopyHoursMonth(SideIncomeMonthRow? row)
    {
        if (row is null || row.Entries.Count == 0) return;

        var gb = CultureInfo.GetCultureInfo("en-GB");
        var html = new System.Text.StringBuilder();
        html.Append("<table style=\"border-collapse: collapse; font-family: Calibri, Arial, sans-serif; font-size: 11pt;\">");
        html.Append("<thead><tr style=\"background:#f3f3f3;\">");
        html.Append("<th style=\"border: 1px solid #ccc; padding: 6px 10px; text-align: left;\">Date</th>");
        html.Append("<th style=\"border: 1px solid #ccc; padding: 6px 10px; text-align: left;\">Duration</th>");
        html.Append("<th style=\"border: 1px solid #ccc; padding: 6px 10px; text-align: right;\">Amount</th>");
        html.Append("</tr></thead><tbody>");
        decimal total = 0m;
        foreach (var e in row.Entries.OrderBy(x => x.PaidOn))
        {
            var dur = e.DurationHours is decimal h ? $"{h:0.##} hrs" : "";
            html.Append("<tr>");
            html.Append($"<td style=\"border: 1px solid #ccc; padding: 6px 10px;\">{e.PaidOn.ToString("dd MMM yyyy", gb)}</td>");
            html.Append($"<td style=\"border: 1px solid #ccc; padding: 6px 10px;\">{dur}</td>");
            html.Append($"<td style=\"border: 1px solid #ccc; padding: 6px 10px; text-align: right;\">{e.Total.ToString("C2", gb)}</td>");
            html.Append("</tr>");
            total += e.Total;
        }
        html.Append("<tr style=\"font-weight: bold; background:#f9f9f9;\">");
        html.Append("<td style=\"border: 1px solid #ccc; padding: 6px 10px;\" colspan=\"2\">Total</td>");
        html.Append($"<td style=\"border: 1px solid #ccc; padding: 6px 10px; text-align: right;\">{total.ToString("C2", gb)}</td>");
        html.Append("</tr></tbody></table>");

        try
        {
            var data = new System.Windows.DataObject();
            data.SetData(System.Windows.DataFormats.Html, BuildCfHtml(html.ToString()));
            // Plain-text fallback for clients that don't honour CF_HTML.
            data.SetData(System.Windows.DataFormats.UnicodeText, BuildPlainTextFallback(row));
            System.Windows.Clipboard.SetDataObject(data, copy: true);
            System.Windows.MessageBox.Show(
                "Hours table copied to clipboard. Open your email and paste (Ctrl+V).",
                "Copied", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Could not copy to clipboard.\n\n{ex.Message}",
                "Copy hours", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>Wraps an HTML fragment in the Windows CF_HTML clipboard
    /// header. The byte offsets in the header MUST be exact — Outlook
    /// silently ignores the payload otherwise — so we build the header
    /// twice: once with placeholders to compute byte counts, then again
    /// with the real offsets substituted in.</summary>
    private static string BuildCfHtml(string fragmentHtml)
    {
        const string template =
            "Version:0.9\r\n" +
            "StartHTML:{0:D10}\r\n" +
            "EndHTML:{1:D10}\r\n" +
            "StartFragment:{2:D10}\r\n" +
            "EndFragment:{3:D10}\r\n" +
            "<html><body>\r\n<!--StartFragment-->{4}<!--EndFragment-->\r\n</body></html>";
        // First pass: compute lengths against zero offsets.
        var probe = string.Format(template, 0, 0, 0, 0, fragmentHtml);
        var bytes = System.Text.Encoding.UTF8;
        var startHtml = bytes.GetByteCount(probe.Substring(0, probe.IndexOf("<html>")));
        var startFragment = bytes.GetByteCount(probe.Substring(0, probe.IndexOf("<!--StartFragment-->") + "<!--StartFragment-->".Length));
        var endFragment = bytes.GetByteCount(probe.Substring(0, probe.IndexOf("<!--EndFragment-->")));
        var endHtml = bytes.GetByteCount(probe);
        return string.Format(template, startHtml, endHtml, startFragment, endFragment, fragmentHtml);
    }

    private static string BuildPlainTextFallback(SideIncomeMonthRow row)
    {
        var gb = CultureInfo.GetCultureInfo("en-GB");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Date         Duration     Amount");
        sb.AppendLine("----         --------     ------");
        decimal total = 0m;
        foreach (var e in row.Entries.OrderBy(x => x.PaidOn))
        {
            var date = e.PaidOn.ToString("dd MMM", gb).PadRight(13);
            var dur = (e.DurationHours is decimal h ? $"{h:0.##} hrs" : "").PadRight(13);
            sb.AppendLine($"{date}{dur}{e.Total.ToString("C2", gb)}");
            total += e.Total;
        }
        sb.AppendLine();
        sb.AppendLine($"Total: {total.ToString("C2", gb)}");
        return sb.ToString();
    }

    /// <summary>Pre-fills the add-entry form from a saved template so a
    /// recurring gig (e.g. "Cleaning shift, 2.5h × £18") is two clicks
    /// instead of four typed fields.</summary>
    [RelayCommand]
    public void ApplyTemplate(SideIncomeTemplate? template)
    {
        if (template is null) return;
        NewDescription = template.Description ?? "";
        NewDurationHours = template.DurationHours;
        NewHourlyRate = template.HourlyRate;
        NewTotal = template.FixedTotal
                   ?? SideIncomeMath.SuggestedTotal(template.DurationHours, template.HourlyRate);
        AddEntryCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Saves the current add-form values as a named template.
    /// Prompts for a name. Persists per-profile via the repository.</summary>
    [RelayCommand]
    public async Task SaveTemplateAsync(CancellationToken ct = default)
    {
        if (_sessions.Current is null) return;

        var dialog = new System.Windows.Window
        {
            Title = "Save template",
            Width = 360, Height = 160,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            Owner = System.Windows.Application.Current.MainWindow,
            ResizeMode = System.Windows.ResizeMode.NoResize,
        };
        var stack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(16) };
        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Template name (e.g. \"Cleaning shift\"):",
            Margin = new System.Windows.Thickness(0, 0, 0, 6),
        });
        var input = new System.Windows.Controls.TextBox { Text = NewDescription };
        stack.Children.Add(input);
        var ok = new System.Windows.Controls.Button
        {
            Content = "Save",
            Margin = new System.Windows.Thickness(0, 12, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Padding = new System.Windows.Thickness(12, 4, 12, 4),
            IsDefault = true,
        };
        ok.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        stack.Children.Add(ok);
        dialog.Content = stack;
        input.Focus();
        input.SelectAll();
        if (dialog.ShowDialog() != true) return;
        var name = string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim();
        if (name is null) return;

        var repo = _sessions.Current.Services.GetRequiredService<ISideIncomeRepository>();
        await repo.AddTemplateAsync(new SideIncomeTemplate
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(NewDescription) ? null : NewDescription.Trim(),
            DurationHours = NewDurationHours,
            HourlyRate = NewHourlyRate,
            // Only stash a fixed total if hours+rate aren't both filled —
            // otherwise the template re-derives it from the rate next time.
            FixedTotal = (NewDurationHours is null || NewHourlyRate is null) ? NewTotal : null,
            SortOrder = Templates.Count,
        }, ct);

        Templates.Clear();
        foreach (var t in await repo.ListTemplatesAsync(ct))
            Templates.Add(t);
        HasTemplates = Templates.Count > 0;
    }

    [RelayCommand]
    public async Task DeleteTemplateAsync(SideIncomeTemplate? template, CancellationToken ct = default)
    {
        if (template is null || _sessions.Current is null) return;
        var confirm = System.Windows.MessageBox.Show(
            $"Delete template \"{template.Name}\"?",
            "Delete template",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.OK) return;

        var repo = _sessions.Current.Services.GetRequiredService<ISideIncomeRepository>();
        await repo.DeleteTemplateAsync(template.Id, ct);
        Templates.Remove(template);
    }

    [RelayCommand]
    public async Task DeleteEntryAsync(SideIncomeEntryRow? row, CancellationToken ct = default)
    {
        if (row is null || _sessions.Current is null) return;
        var confirm = System.Windows.MessageBox.Show(
            $"Delete this side-income entry? Any existing allocations (pocket deposits / ledger transactions) stay — only the source record is removed.",
            "Delete entry",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.OK) return;

        var repo = _sessions.Current.Services.GetRequiredService<ISideIncomeRepository>();
        await repo.DeleteEntryAsync(row.Id, ct);
        await LoadAsync(ct);
    }
}

/// <summary>Bucket for a calendar month. Exposes totals + a collection
/// of entries so the view can render groups.</summary>
public sealed partial class SideIncomeMonthRow(SideIncomeCalendarMonth source) : ObservableObject
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    public string MonthLabel { get; } = source.PeriodStart.ToString("MMMM yyyy", EnGb);
    public decimal TotalEarned { get; } = source.TotalEarned;
    public decimal TotalAllocated { get; } = source.TotalAllocated;
    public decimal Remaining { get; } = source.Remaining;
    public string TotalEarnedDisplay => TotalEarned.ToString("C2", EnGb);
    public string RemainingDisplay => Remaining.ToString("C2", EnGb);
    public ObservableCollection<SideIncomeEntryRow> Entries { get; } = [];
}

/// <summary>ComboBox-friendly pairing of <see cref="TaxBand"/> with a
/// readable label so the picker shows "Basic 26%" rather than "Basic".</summary>
public sealed record TaxBandOption(TaxBand Band, string Label)
{
    public static IReadOnlyList<TaxBandOption> All { get; } =
    [
        new(TaxBand.TradingAllowance, "Trading allowance (£1k tax-free)"),
        new(TaxBand.Basic,             "Basic — 20% + 6% NI = 26%"),
        new(TaxBand.Higher,            "Higher — 40% + 2% NI = 42%"),
        new(TaxBand.Additional,        "Additional — 45% + 2% NI = 47%"),
        new(TaxBand.Custom,            "Custom rate"),
    ];

    public override string ToString() => Label;
}

public sealed partial class SideIncomeEntryRow : ObservableObject
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    public Guid Id { get; }
    public DateOnly PaidOn { get; }
    public string Description { get; }
    public decimal? DurationHours { get; }
    public decimal? HourlyRate { get; }
    public decimal Total { get; }
    public decimal Remaining { get; }

    public string DurationRateDisplay =>
        DurationHours is decimal h && HourlyRate is decimal r
            ? $"{h:0.##}h × {r.ToString("C2", EnGb)}"
            : "";

    public string TotalDisplay => Total.ToString("C2", EnGb);
    public string RemainingDisplay => Remaining.ToString("C2", EnGb);
    public bool HasRemaining => Remaining > 0m;

    public SideIncomeEntryRow(SideIncomeEntry source, decimal remaining)
    {
        Id = source.Id;
        PaidOn = source.PaidOn;
        Description = source.Description ?? "";
        DurationHours = source.DurationHours;
        HourlyRate = source.HourlyRate;
        Total = source.Total;
        Remaining = remaining;
    }
}

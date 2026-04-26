using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace PiggyBank.App.Updates;

/// <summary>
/// Background poller that checks GitHub Releases for a newer build and
/// downloads it for next-launch install. Designed to be aggressively
/// fail-safe: network failures, rate limits, malformed feeds, missing
/// Velopack metadata, etc. are all swallowed and logged at
/// <see cref="LogLevel.Information"/>. The host app must NEVER crash
/// because the update server is unreachable.
/// </summary>
/// <remarks>
/// Singleton. Registered with DI; first use is from <see cref="MainWindow"/>
/// after the shell loads, so update checking can never block the
/// startup path. Idempotent: <see cref="StartBackgroundCheck"/> is a
/// no-op after the first call so multiple Loaded events don't trigger
/// repeated downloads.
///
/// "Ready" event fires on a background thread — UI subscribers MUST
/// marshal back to the dispatcher before showing dialogs.
/// </remarks>
public sealed class UpdateService(ILogger<UpdateService> logger)
{
    private const string ReleasesUrl = "https://github.com/Yokomoko/PiggyBank";

    private readonly ILogger<UpdateService> _logger = logger;
    private int _started;

    /// <summary>Raised once per session when a downloaded update is ready
    /// to install. Subscribers should offer the user a "restart now" prompt.</summary>
    public event EventHandler<UpdateReadyEventArgs>? UpdateReady;

    /// <summary>Spawns the check on a background task and returns immediately.
    /// Subsequent calls are no-ops for the lifetime of the process.</summary>
    public void StartBackgroundCheck()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _ = Task.Run(CheckAsync);
    }

    private async Task CheckAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(ReleasesUrl, null, false));

            // Running from a dev build (no Velopack install metadata) —
            // skip silently. IsInstalled itself can throw if no locator
            // is wired, hence the broader try/catch around everything.
            if (!mgr.IsInstalled)
            {
                _logger.LogInformation("Update check skipped: app is not running from a Velopack install.");
                return;
            }

            var info = await mgr.CheckForUpdatesAsync();
            if (info is null)
            {
                _logger.LogInformation("Update check: already up to date.");
                return;
            }

            _logger.LogInformation(
                "Update available ({Version}); downloading in the background.",
                info.TargetFullRelease.Version);
            await mgr.DownloadUpdatesAsync(info);
            _logger.LogInformation("Update downloaded; will apply on next launch (or sooner if user opts in).");

            UpdateReady?.Invoke(this, new UpdateReadyEventArgs(mgr, info));
        }
        catch (Exception ex)
        {
            // Network down, GitHub rate-limited, malformed feed, anything.
            // The user keeps using the app; the update will land on the
            // next session that can reach GitHub. Log at Information so
            // it's visible in diagnostics without spamming Warning/Error.
            _logger.LogInformation(ex, "Background update check failed; ignored.");
        }
    }
}

/// <summary>Carries the bits a UI subscriber needs to apply an update:
/// the manager (used to call <c>ApplyUpdatesAndRestart</c>) and the info
/// (carries the target version for display).</summary>
public sealed record UpdateReadyEventArgs(UpdateManager Manager, UpdateInfo Info);

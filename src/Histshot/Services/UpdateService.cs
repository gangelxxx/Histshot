using Histshot.Core.Services;
using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Histshot.Services;

/// <summary>
/// In-app auto-updater backed by Velopack and GitHub Releases.
///
/// Flow: at startup we silently check the latest GitHub release and, if newer, download it in the
/// background. The tray "Update" item is only enabled once a downloaded update is ready; clicking it
/// then just applies the staged files and relaunches — to the user, pressing "Update" only restarts
/// the app. When running un-installed (e.g. <c>dotnet run</c> in development), Velopack reports the
/// app as not installed and every method here becomes a safe no-op.
/// </summary>
public sealed class UpdateService
{
    // Full URL to the GitHub repository whose Releases host the Velopack packages.
    // The release CI publishes here; the updater reads from the same place.
    private const string RepoUrl = "https://github.com/gangelxxx/Histshot";

    private readonly UpdateManager? _manager;
    private UpdateInfo? _pendingUpdate;

    /// <summary>Raised on the thread that completed the background download once an update is staged
    /// and ready to apply. Use it to enable the tray "Update" item.</summary>
    public event EventHandler? UpdateReady;

    /// <summary>True once an update has been downloaded and is ready for <see cref="ApplyAndRestart"/>.</summary>
    public bool IsUpdateReady => _pendingUpdate != null;

    public UpdateService()
    {
        try
        {
            var source = new GithubSource(RepoUrl, accessToken: null, prerelease: false);
            _manager = new UpdateManager(source);
        }
        catch (Exception ex)
        {
            // A malformed source shouldn't take the app down — just disable updating.
            DebugLogger.Log($"UpdateService init failed: {ex}");
            _manager = null;
        }
    }

    /// <summary>Silently checks for a newer release and, if found, downloads it in the background.
    /// Safe to call fire-and-forget at startup; never throws.</summary>
    public async Task CheckAndDownloadAsync()
    {
        // Not packaged by Velopack (dev run / plain build) → nothing to update.
        if (_manager is not { IsInstalled: true })
            return;

        try
        {
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update == null)
            {
                DebugLogger.Log("Update check: already up to date.");
                return;
            }

            DebugLogger.Log($"Update check: {update.TargetFullRelease.Version} available, downloading.");
            await _manager.DownloadUpdatesAsync(update).ConfigureAwait(false);

            _pendingUpdate = update;
            UpdateReady?.Invoke(this, EventArgs.Empty);
            DebugLogger.Log($"Update {update.TargetFullRelease.Version} downloaded and ready.");
        }
        catch (Exception ex)
        {
            // Offline, rate-limited, no releases yet, etc. — fail quietly, retry next launch.
            DebugLogger.Log($"Update check/download failed: {ex}");
        }
    }

    /// <summary>Applies the staged update and relaunches the app. Does not return on success
    /// (the process exits and a new one starts). No-op if no update is ready.</summary>
    public void ApplyAndRestart()
    {
        if (_manager is not { IsInstalled: true } || _pendingUpdate == null)
            return;

        DebugLogger.Log($"Applying update {_pendingUpdate.TargetFullRelease.Version} and restarting.");
        _manager.ApplyUpdatesAndRestart(_pendingUpdate);
    }
}

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace AzerothCoreCreator
{
    internal static class UpdateService
    {
        // Must match your GitHub repo exactly
        private const string GithubOwner = "KrowtennetworK";
        private const string GithubRepo = "AzerothCoreCreator";

        public static async Task CheckAndUpdateAsync(bool includePrereleases)
        {
            try
            {
                Debug.WriteLine($"[Velopack] Checking updates for {GithubOwner}/{GithubRepo} (includePrereleases={includePrereleases})...");

                // Public repo => no token needed.
                // NOTE: With Velopack 0.0.1298, GithubSource does not take a channel string.
                // The channel is determined by the installed app's sq.version (created by vpk pack -c <channel>).
                var source = new GithubSource(GithubOwner, GithubRepo, includePrereleases);
                var mgr = new UpdateManager(source);

                var update = await mgr.CheckForUpdatesAsync();
                if (update == null)
                {
                    Debug.WriteLine("[Velopack] No update found.");
                    return;
                }

                Debug.WriteLine($"[Velopack] Update found: {update.TargetFullRelease.Version}");

                var res = MessageBox.Show(
                    $"Update found: {update.TargetFullRelease.Version}\n\nDownload and restart to apply it?",
                    "AzerothCore Creator Update",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (res != MessageBoxResult.Yes)
                    return;

                await mgr.DownloadUpdatesAsync(update);
                mgr.ApplyUpdatesAndRestart(update);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Velopack] Update check failed: " + ex);
            }
        }
    }
}

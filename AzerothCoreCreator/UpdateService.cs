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
        // EXACTLY match your repo in the browser
        private const string GithubOwner = "KrowtennetworK";
        private const string GithubRepo = "AzerothCoreCreator";
        // Must match the channel you pack/upload with in GitHub Actions (CHANNEL env var)
        private const string ReleaseChannel = "win";

        public static async Task CheckAndUpdateAsync(bool includePrereleases)
        {
            try
            {
                Debug.WriteLine($"[Velopack] Checking updates for {GithubOwner}/{GithubRepo} (channel='{ReleaseChannel}', prerelease={includePrereleases})...");

                // IMPORTANT: for public repos, no token needed
                // Channel MUST match your workflow/channel used by vpk pack/upload.
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
                Debug.WriteLine("Update check failed: " + ex);
            }
        }
    }
}

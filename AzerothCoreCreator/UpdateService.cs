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
        // This must point to the SAME repo that contains your Velopack release assets (.nupkg, RELEASES, releases.win.json, etc.)
        private const string GithubOwner = "KrowtennetworK";
        private const string GithubRepo = "AzerothCoreCreator";

        /// <summary>
        /// Check GitHub releases for updates, download, and restart to apply.
        /// </summary>
        public static async Task CheckAndUpdateAsync(bool includePrereleases)
        {
            try
            {
                // For a PUBLIC repo, token is not required.
                // If you ever make the repo private, you'll need a GitHub token with repo access.
                var source = new GithubSource(GithubOwner, GithubRepo, includePrereleases);
                var mgr = new UpdateManager(source);

                var update = await mgr.CheckForUpdatesAsync();
                if (update == null)
                {
                    Debug.WriteLine("No update available.");
                    return;
                }

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


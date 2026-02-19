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
        // CHANGE THESE to match your releases repo EXACTLY
        private const string GithubOwner = "KrowtennetworK";
        private const string GithubRepo = "AzerothCoreCreator";

        /// <summary>
        /// Checks GitHub releases for updates and applies them.
        /// Set includePrereleases=true for beta channels.
        /// </summary>
        public static async Task CheckAndUpdateAsync(bool includePrereleases)
        {
            try
            {
                // If your repo is PUBLIC, token can be null.
                // If PRIVATE, you'll need a GitHub token with repo access.
                string? token = null;

                var source = new GithubSource(GithubOwner, GithubRepo, includePrereleases);
                var mgr = new UpdateManager(source);

                var update = await mgr.CheckForUpdatesAsync();
                if (update == null)
                    return;

                // Optional prompt (remove if you want silent updates)
                var res = MessageBox.Show(
                    $"Update found: {update.TargetFullRelease.Version}\n\nDownload and restart to apply it?",
                    "AzerothCore Creator Update",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (res != MessageBoxResult.Yes)
                    return;

                await mgr.DownloadUpdatesAsync(update);

                // Velopack 0.0.1298+ requires the update object passed in
                mgr.ApplyUpdatesAndRestart(update);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Update check failed: " + ex);
            }
        }
    }
}

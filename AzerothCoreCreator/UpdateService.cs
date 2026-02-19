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
        // CHANGE THIS to your real repo
        private const string RepoUrl = "https://github.com/krowtennetwork/AzerothCoreCreator";

        public static async Task CheckAndUpdateAsync(bool includePrereleases)
        {
            try
            {
                string? token = null;

                var source = new GithubSource(RepoUrl, token, includePrereleases);
                var mgr = new UpdateManager(source);

                var update = await mgr.CheckForUpdatesAsync();
                if (update == null) return;

                var res = MessageBox.Show(
                    $"Update found: {update.TargetFullRelease.Version}\n\nDownload and restart to apply it?",
                    "AzerothCore Creator Update",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (res != MessageBoxResult.Yes) return;

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

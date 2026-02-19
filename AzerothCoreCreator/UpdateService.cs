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

        public static async Task CheckAndUpdateAsync(bool includePrereleases)
        {
            try
            {
                // IMPORTANT: for public repos, no token needed
                var source = new GithubSource("KrowtennetworK", "AzerothCoreCreator", includePrereleases);
                var mgr = new UpdateManager(source);

                var update = await mgr.CheckForUpdatesAsync();
                if (update == null)
                    return;

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

using System.Windows;
using Velopack;

namespace AzerothCoreCreator
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Required for Velopack install/update lifecycle
            VelopackApp.Build().Run();

            try
            {
                // Point this to your releases feed location
                var mgr = new UpdateManager("https://github.com/KrowtennetworK/AzerothCore-Creator-Releases/releases/latest/download");

                var update = await mgr.CheckForUpdatesAsync();

                if (update != null)
                {
                    await mgr.DownloadUpdatesAsync(update);
                    mgr.ApplyUpdatesAndRestart(update);
                }
            }
            catch
            {
                // Optional: log if you want
            }
        }
    }
}

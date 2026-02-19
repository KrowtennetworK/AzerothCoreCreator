using System;
using System.Windows;
using Velopack;

namespace AzerothCoreCreator
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // REQUIRED for Velopack install/update lifecycle
            VelopackApp.Build().Run();

            try
            {
                // IMPORTANT:
                // Replace with your actual releases repo URL
                var mgr = new UpdateManager("https://github.com/Krowtennetwork/AzerothCore-Creator-Releases/releases/latest/download");

                var update = await mgr.CheckForUpdatesAsync();

                if (update != null)
                {
                    await mgr.DownloadUpdatesAsync(update);
                    mgr.ApplyUpdatesAndRestart();
                }
            }
            catch
            {
                // Optional: log error if needed
            }
        }
    }
}



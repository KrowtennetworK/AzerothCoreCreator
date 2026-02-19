using System;
using System.Diagnostics;
using System.Windows;
using Velopack;

namespace AzerothCoreCreator
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Required for Velopack install/update lifecycle. Call this as early as possible.
            VelopackApp.Build().Run();

            try
            {
                // Beta channel: include pre-releases (e.g., v0.1.5-beta)
                await UpdateService.CheckAndUpdateAsync(includePrereleases: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Startup update check failed: " + ex);
            }
        }
    }
}


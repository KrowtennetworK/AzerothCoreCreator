using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Velopack;

namespace AzerothCoreCreator
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Required for Velopack install/update lifecycle
            try
            {
                VelopackApp.Build().Run();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Velopack init failed: " + ex);
            }

            // Always show the UI first (never "won't open")
            try
            {
                if (MainWindow == null)
                    MainWindow = new MainWindow();

                MainWindow.Show();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("MainWindow failed to show: " + ex);
                Shutdown();
                return;
            }

            // Run update check after UI is visible (beta channel enabled)
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await UpdateService.CheckAndUpdateAsync(includePrereleases: true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Startup update check failed: " + ex);
                }
            }), DispatcherPriority.Background);
        }
    }
}


using System.Windows;
using Velopack;

namespace AzerothCoreCreator
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Initialize Velopack updater
            // This must run early in the app lifecycle
            VelopackApp.Build().Run();

            base.OnStartup(e);
        }
    }
}


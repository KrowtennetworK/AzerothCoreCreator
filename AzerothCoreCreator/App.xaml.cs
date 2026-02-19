using System.Windows;
using Velopack;

namespace AzerothCoreCreator
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Required for Velopack install/update lifecycle (must be called early)
            VelopackApp.Build().Run();

            // Use GithubSource-based updater (supports beta/prerelease properly)
            await UpdateService.CheckAndUpdateAsync(includePrereleases: true);
        }
    }
}


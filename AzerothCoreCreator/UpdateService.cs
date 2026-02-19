using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
using Velopack;
using Velopack.Sources;

namespace AzerothCoreCreator
{
    internal static class UpdateService
    {
        // Must match your GitHub repo exactly
        private const string GithubOwner = "KrowtennetworK";
        private const string GithubRepo = "AzerothCoreCreator";

        private static readonly object _logLock = new object();

        private sealed record InstallInfo(
            string PackageId,
            string InstalledVersion,
            string Channel,
            string MainExe,
            string SqVersionPath);

        private static InstallInfo ReadInstallInfo()
        {
            // Velopack stores sq.version alongside the installed app (eg ...\current\sq.version).
            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidates = new[]
            {
                Path.Combine(baseDir, "sq.version"),
                Path.Combine(baseDir, "..", "sq.version"),
                Path.Combine(baseDir, "..", "current", "sq.version"),
            };

            foreach (var p in candidates)
            {
                try
                {
                    var full = Path.GetFullPath(p);
                    if (!File.Exists(full))
                        continue;

                    var doc = XDocument.Load(full);
                    var meta = doc.Root?.Element("metadata");

                    string Get(string name, string fallback) => meta?.Element(name)?.Value?.Trim() ?? fallback;

                    return new InstallInfo(
                        PackageId: Get("id", "(unknown)"),
                        InstalledVersion: Get("version", "(unknown)"),
                        Channel: Get("channel", "(unknown)"),
                        MainExe: Get("mainExe", "(unknown)"),
                        SqVersionPath: full
                    );
                }
                catch
                {
                    // keep trying other candidates
                }
            }

            return new InstallInfo(
                PackageId: "(unknown)",
                InstalledVersion: "(unknown)",
                Channel: "(unknown)",
                MainExe: "(unknown)",
                SqVersionPath: "(not found)"
            );
        }

        private static string GetLogPath(InstallInfo info)
        {
            // Keep logs in %LOCALAPPDATA%\<PackageId>\update-debug.log
            // If we can't read PackageId, fall back to your intended ID.
            var pkg = string.IsNullOrWhiteSpace(info.PackageId) || info.PackageId == "(unknown)"
                ? "KrowtennetworK.AzerothCoreCreator"
                : info.PackageId;

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), pkg);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "update-debug.log");
        }

        private static void Log(string message)
        {
            try
            {
                var info = ReadInstallInfo();
                var path = GetLogPath(info);
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";

                lock (_logLock)
                {
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Never let logging crash the app.
            }
        }

        private static void LogHeader(InstallInfo info, bool includePrereleases)
        {
            Log("================ Velopack Update Check ================");
            Log($"ProcessId={Environment.ProcessId}");
            Log($"Exe={Process.GetCurrentProcess().MainModule?.FileName}");
            Log($"BaseDirectory={AppContext.BaseDirectory}");
            Log($"OS={Environment.OSVersion}");
            Log($".NET={Environment.Version}");
            Log($"VelopackAssemblyVersion={typeof(VelopackApp).Assembly.GetName().Version}");
            Log($"AppAssemblyVersion={Assembly.GetExecutingAssembly().GetName().Version}");
            Log($"sq.version={info.SqVersionPath}");
            Log($"PackageId={info.PackageId}");
            Log($"InstalledVersion={info.InstalledVersion}");
            Log($"Channel={info.Channel}");
            Log($"MainExe={info.MainExe}");
            Log($"Repo={GithubOwner}/{GithubRepo}");
            Log($"includePrereleases={includePrereleases}");
        }

        public static async Task CheckAndUpdateAsync(bool includePrereleases)
        {
            var install = ReadInstallInfo();
            LogHeader(install, includePrereleases);

            try
            {
                // Also write to Debug output for local dev.
                Debug.WriteLine($"[Velopack] Checking updates for {GithubOwner}/{GithubRepo} (includePrereleases={includePrereleases})...");

                // Public repo => no token needed.
                // NOTE: With Velopack 0.0.1298, GithubSource does not take a channel string.
                // The channel is determined by the installed app's sq.version (created by vpk pack -c <channel>).
                var repoUrl = $"https://github.com/{GithubOwner}/{GithubRepo}";
                var source = new GithubSource(repoUrl, null, includePrereleases);
                var mgr = new UpdateManager(source);

                Log("Calling CheckForUpdatesAsync()...");
                var update = await mgr.CheckForUpdatesAsync();

                if (update == null)
                {
                    Debug.WriteLine("[Velopack] No update found.");
                    Log("No update found (update == null).");
                    return;
                }

                Debug.WriteLine($"[Velopack] Update found: {update.TargetFullRelease.Version}");
                Log($"Update found: {update.TargetFullRelease.Version}");

                var res = MessageBox.Show(
                    $"Update found: {update.TargetFullRelease.Version}\n\nDownload and restart to apply it?",
                    "AzerothCore Creator Update",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (res != MessageBoxResult.Yes)
                {
                    Log("User declined update prompt.");
                    return;
                }

                Log("Calling DownloadUpdatesAsync()...");
                await mgr.DownloadUpdatesAsync(update);
                Log("Download complete. Calling ApplyUpdatesAndRestart()...");

                mgr.ApplyUpdatesAndRestart(update);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Velopack] Update check failed: " + ex);
                Log("Update check failed (exception): " + ex);

                // Show where the log is so you can grab it immediately.
                try
                {
                    var path = GetLogPath(ReadInstallInfo());
                    MessageBox.Show(
                        $"Update check failed.\n\nLog written to:\n{path}",
                        "AzerothCore Creator Update",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { }
            }
        }
    }
}

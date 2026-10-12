using System.Windows;
using PartsPortal.Services;
using Syncfusion.Licensing;

namespace PartsPortal
{
    public partial class App : Application
    {
        public App()
        {
            // Register Syncfusion license key
            SyncfusionLicenseProvider.RegisterLicense("NxYtFisQPR08Cit/VkR+XU9Ff1RDX3xKf0x/TGpQb19xflBPallYVBYiSV9jS3hTd0ZjWHpccXdVQmlaVk91XQ==");
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var settings = new SettingsService().Load();
            ThemeService.Apply(settings.IsDarkMode);

            var w = new WarehouseSelectWindow();
            var updateCheckStarted = false;

            w.ContentRendered += async (_, _) =>
            {
                if (updateCheckStarted)
                    return;

                updateCheckStarted = true;
                await CheckForUpdatesAsync(w);
            };

            w.Show();
        }

        private static async Task CheckForUpdatesAsync(Window owner)
        {
            try
            {
                UpdateLog.Write("Automatic update check started.");

                var update = await new UpdateService().CheckAsync();

                if (update is null)
                {
                    UpdateLog.Write("Automatic update check did not return version information.");
                    return;
                }

                UpdateLog.Write(
                    $"Automatic update check: installed={update.CurrentVersion}, latest={update.LatestVersion}, minimum={update.MinimumSupportedVersion}.");

                if (!update.IsUpdateAvailable)
                    return;

                await owner.Dispatcher.InvokeAsync(() =>
                {
                    if (!owner.IsVisible)
                        return;

                    var dialog = new UpdateAvailableWindow(update)
                    {
                        Owner = owner
                    };

                    dialog.ShowDialog();
                });
            }
            catch (Exception ex)
            {
                // Update checks remain non-blocking, but failures are now logged so
                // they can be diagnosed instead of disappearing silently.
                UpdateLog.Write("Automatic update check failed.", ex);
            }
        }
    }
}

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
            w.Show();

            _ = CheckForUpdatesAsync(w);
        }

        private static async Task CheckForUpdatesAsync(Window owner)
        {
            try
            {
                var update = await new UpdateService().CheckAsync();

                if (update is null || !update.IsUpdateAvailable)
                    return;

                await owner.Dispatcher.InvokeAsync(() =>
                {
                    if (!owner.IsLoaded || !owner.IsVisible)
                        return;

                    var dialog = new UpdateAvailableWindow(update)
                    {
                        Owner = owner
                    };

                    dialog.ShowDialog();
                });
            }
            catch
            {
                // Update checks are intentionally non-blocking. PartsPortal must still
                // launch normally if the VM/API is unavailable.
            }
        }
    }
}

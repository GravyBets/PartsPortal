using System.Windows;
using MaterialReqAppV3.Services;
using Syncfusion.Licensing;

namespace MaterialReqAppV3
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
        }
    }
}
using System.Windows;
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
    }
}
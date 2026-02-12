using PartsPortal.Models;
using PartsPortal.Services;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace PartsPortal
{
    public partial class BugFeatureWindow : Window
    {
        private readonly UserSettings _settings;

        public BugFeatureWindow(UserSettings settings)
        {
            InitializeComponent();
            _settings = settings;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

     

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            // HARD-CODED recipients (not from settings)
            const string to = "smartgridradio@centerpointenergy.com";
            const string cc = "alexander.pletan@centerpointenergy.com";

            string type =
                (TypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString()
                ?? "Bug";

            string details = (DetailsBox.Text ?? "").Trim();

            // If you’re using watermark text inside the textbox, ignore it if user never typed
            // (only needed if your watermark inserts actual text into DetailsBox.Text)
            if (string.IsNullOrWhiteSpace(details))
                {
                    MessageBox.Show("Please enter details before creating the email draft.",
                        "Bug / Feature", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

            string subject = $"Material Req App - {type} - {DateTime.Now:yyyy-MM-dd HH:mm}";

            string body = BuildBugFeatureEmailBody(type, details);

            object? outlookApp = null;
            object? mailItem = null;

            try
            {
                outlookApp = OutlookCom.GetOrStartOutlook();

                // 0 = olMailItem
                mailItem = outlookApp.GetType().InvokeMember(
                    "CreateItem",
                    BindingFlags.InvokeMethod,
                    null,
                    outlookApp,
                    new object[] { 0 });

            if (mailItem == null)
                throw new InvalidOperationException("Outlook CreateItem returned null.");

                SetComProperty(mailItem, "To", to);
                SetComProperty(mailItem, "CC", cc);
                SetComProperty(mailItem, "Subject", subject);
                SetComProperty(mailItem, "Body", body);

                // Open as draft (non-modal)
                mailItem.GetType().InvokeMember(
                    "Display",
                    BindingFlags.InvokeMethod,
                    null,
                    mailItem,
                    new object[] { false });

                Close();
                }
            catch (Exception ex)
            {
                 MessageBox.Show("Failed to open Outlook draft:\n\n" + ex.Message,
                    "Bug / Feature", MessageBoxButton.OK, MessageBoxImage.Error);
            }
                finally
            {
                SafeReleaseComObject(mailItem);
                SafeReleaseComObject(outlookApp);
            }
        }

    private static string BuildBugFeatureEmailBody(string type, string details)
    {
        // App version info
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        string appVersion = asm.GetName().Version?.ToString() ?? "(unknown)";
        string exePath = asm.Location ?? "";
        string fileVersion = "(unknown)";

        try
        {
            if (!string.IsNullOrWhiteSpace(exePath))
                fileVersion = FileVersionInfo.GetVersionInfo(exePath).FileVersion ?? "(unknown)";
        }
        catch { /* ignore */ }

        // Machine/user info
        string machine = Environment.MachineName;
        string user = Environment.UserName;
        string domain = Environment.UserDomainName;
        string os = Environment.OSVersion.ToString();
        string dotnet = Environment.Version.ToString(); // runtime version

        string timestamp = DateTime.Now.ToString("MM/dd/yyyy hh:mm tt");

        return
            $"{details}\n\n" +
            "----------------------------------------\n" +
            $"Type: {type}\n" +
            $"Time: {timestamp}\n" +
            $"PC: {machine}\n" +
            $"User: {domain}\\{user}\n" +
            $"OS: {os}\n" +
            $".NET: {dotnet}\n" +
            $"App Version: {appVersion}\n" +
            $"File Version: {fileVersion}\n" +
            $"App Path: {exePath}\n" +
            "----------------------------------------\n";
    }

    // Helpers (keep these if you already have them)
    private static void SetComProperty(object target, string name, object? value)
    {
        target.GetType().InvokeMember(
            name,
            BindingFlags.SetProperty,
            null,
            target,
            new object?[] { value });
    }

    private static void SafeReleaseComObject(object? obj)
    {
        try
        {
            if (obj != null && System.Runtime.InteropServices.Marshal.IsComObject(obj))
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(obj);
        }
        catch { }
    }


}
}

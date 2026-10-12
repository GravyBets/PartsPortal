using System.Diagnostics;
using System.Windows;
using PartsPortal.Models;

namespace PartsPortal;

public partial class UpdateAvailableWindow : Window
{
    private readonly UpdateCheckResult _update;

    public UpdateAvailableWindow(UpdateCheckResult update)
    {
        _update = update;
        InitializeComponent();

        TitleText.Text = update.IsUpdateRequired
            ? "PartsPortal update required"
            : "A new PartsPortal version is available";

        VersionText.Text =
            $"Installed: {Format(update.CurrentVersion)}    Available: {Format(update.LatestVersion)}";

        RequiredBanner.Visibility = update.IsUpdateRequired
            ? Visibility.Visible
            : Visibility.Collapsed;

        LaterButton.Content = update.IsUpdateRequired ? "Exit PartsPortal" : "Later";

        ReleaseNotesList.ItemsSource = update.ReleaseNotes.Count > 0
            ? update.ReleaseNotes
            : new[] { "General improvements and fixes." };
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        if (_update.IsUpdateRequired)
        {
            Application.Current.Shutdown();
            return;
        }

        Close();
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_update.DownloadUrl))
        {
            MessageBox.Show(
                "The update download location is not configured yet.",
                "PartsPortal Update",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_update.DownloadUrl)
            {
                UseShellExecute = true
            });

            if (_update.IsUpdateRequired)
                Application.Current.Shutdown();
            else
                Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"PartsPortal could not open the update download.\n\n{ex.Message}",
                "PartsPortal Update",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string Format(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
}

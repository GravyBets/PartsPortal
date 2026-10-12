using System.Diagnostics;
using System.Reflection;
using System.Windows;
using PartsPortal.Services;

namespace PartsPortal
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            LoadVersionText();
        }

        private void LoadVersionText()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();

                string? infoVersion = asm
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;

                if (!string.IsNullOrWhiteSpace(infoVersion))
                {
                    int plus = infoVersion.IndexOf('+');
                    if (plus > 0)
                        infoVersion = infoVersion.Substring(0, plus);

                    VersionText.Text = $"Version {infoVersion}";
                    return;
                }

                Version? asmVersion = asm.GetName().Version;
                if (asmVersion != null)
                {
                    VersionText.Text =
                        $"Version {asmVersion.Major}.{asmVersion.Minor}.{asmVersion.Build}";
                    return;
                }

                string exePath = asm.Location;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    var fv = FileVersionInfo.GetVersionInfo(exePath).FileVersion;
                    if (!string.IsNullOrWhiteSpace(fv))
                    {
                        VersionText.Text = $"Version {fv}";
                        return;
                    }
                }

                VersionText.Text = "Version (unknown)";
            }
            catch
            {
                VersionText.Text = "Version (unknown)";
            }
        }

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdatesButton.IsEnabled = false;
            UpdateStatusText.Text = "Checking...";

            try
            {
                UpdateLog.Write("Manual update check started from About window.");

                var update = await new UpdateService().CheckAsync();

                if (update is null)
                {
                    UpdateStatusText.Text = "Could not check for updates.";
                    MessageBox.Show(
                        $"PartsPortal could not reach or read the update service.\n\nDiagnostic log:\n{UpdateLog.LogPath}",
                        "PartsPortal Update",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (!update.IsUpdateAvailable)
                {
                    UpdateStatusText.Text = "You're up to date.";
                    MessageBox.Show(
                        $"PartsPortal {update.CurrentVersion.Major}.{update.CurrentVersion.Minor}.{update.CurrentVersion.Build} is up to date.",
                        "PartsPortal Update",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                UpdateStatusText.Text =
                    $"Version {update.LatestVersion.Major}.{update.LatestVersion.Minor}.{update.LatestVersion.Build} available.";

                var dialog = new UpdateAvailableWindow(update)
                {
                    Owner = this
                };

                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                UpdateLog.Write("Manual update check failed.", ex);
                UpdateStatusText.Text = "Update check failed.";

                MessageBox.Show(
                    $"The update check failed.\n\nDiagnostic log:\n{UpdateLog.LogPath}",
                    "PartsPortal Update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                CheckUpdatesButton.IsEnabled = true;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

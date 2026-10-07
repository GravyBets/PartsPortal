using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;

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

                // Prefer InformationalVersion (shows "1.2.3" or "1.2.3+commit" if you use it)
                string? infoVersion = asm
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;

                if (!string.IsNullOrWhiteSpace(infoVersion))
                {
                    // If it contains "+..." (common in CI builds), show only before '+'
                    int plus = infoVersion.IndexOf('+');
                    if (plus > 0) infoVersion = infoVersion.Substring(0, plus);

                    VersionText.Text = $"Version {infoVersion}";
                    return;
                }

                // Next: AssemblyVersion (from project properties / AssemblyInfo)
                Version? asmVersion = asm.GetName().Version;
                if (asmVersion != null)
                {
                    VersionText.Text = $"Version {asmVersion.Major}.{asmVersion.Minor}.{asmVersion.Build}";
                    return;
                }

                // Fallback: FileVersion (from Windows file metadata)
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

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

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

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

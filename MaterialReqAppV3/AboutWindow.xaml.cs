using System.Reflection;
using System.Windows;

namespace MaterialReqAppV3
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"Version: {v}";
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}

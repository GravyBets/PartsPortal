using System.Windows.Controls;

namespace PartsPortal.Views
{
    public partial class PartsBrowserView : UserControl
    {
        public PartsBrowserView()
        {
            InitializeComponent();
        }

        private void PartsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ClearPartsSearchButton != null)
            {
                ClearPartsSearchButton.Visibility =
                    string.IsNullOrEmpty(PartsSearchBox.Text)
                        ? System.Windows.Visibility.Collapsed
                        : System.Windows.Visibility.Visible;
            }
        }

        private void ClearPartsSearchButton_Click(
            object sender,
            System.Windows.RoutedEventArgs e)
        {
            PartsSearchBox.Clear();
            PartsSearchBox.Focus();
        }
    }
}

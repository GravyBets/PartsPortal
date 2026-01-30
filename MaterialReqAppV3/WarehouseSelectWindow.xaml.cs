using System.Windows;
using System.Windows.Controls;

namespace MaterialReqAppV3
{
    public partial class WarehouseSelectWindow : Window
    {
        public WarehouseSelectWindow()
        {
            InitializeComponent();
        }

        private void Warehouse_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            string selectedWarehouse = btn.Content?.ToString() ?? "";

            var main = new MainWindow(selectedWarehouse);
            main.Show();

            Close();
        }
    }
}

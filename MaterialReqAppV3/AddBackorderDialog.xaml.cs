using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MaterialReqAppV3
{
    public partial class AddBackorderDialog : Window
    {
        public class EntryLine
        {
            public int Qty { get; set; } = 1;
            public string WorkOrder { get; set; } = "";
            public string SiteReason { get; set; } = "";
        }

        public ObservableCollection<EntryLine> Lines { get; } = new();

        // BackordersWindow expects this
        public IReadOnlyList<EntryLine> ResultLines => Lines.ToList();

        public AddBackorderDialog(string description, string material)
        {
            InitializeComponent();

            PartDesc.Text = description ?? "";
            PartMat.Text = material ?? "";

            LinesList.ItemsSource = Lines;

            Loaded += (_, __) =>
            {
                WorkOrderBox.Focus();
                WorkOrderBox.SelectAll();
            };
        }

        private void AddLine_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBuildLine(out var line))
                return;

            Lines.Add(line);

            // Reset fields for next entry
            QtyBox.Text = "1";
            SiteReasonBox.Text = "";

            WorkOrderBox.Focus();
            WorkOrderBox.SelectAll();
        }

        private bool TryBuildLine(out EntryLine line)
        {
            line = new EntryLine();

            // Qty
            if (!int.TryParse(QtyBox.Text?.Trim(), out int qty) || qty < 1)
            {
                MessageBox.Show("Qty must be 1 or more.", "Invalid Qty", MessageBoxButton.OK, MessageBoxImage.Warning);
                QtyBox.Focus();
                QtyBox.SelectAll();
                return false;
            }

            // Work Order required
            var wo = (WorkOrderBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(wo))
            {
                MessageBox.Show("Work Order is required.", "Missing Work Order", MessageBoxButton.OK, MessageBoxImage.Warning);
                WorkOrderBox.Focus();
                return false;
            }

            line.Qty = qty;
            line.WorkOrder = wo;
            line.SiteReason = (SiteReasonBox.Text ?? "").Trim();
            return true;
        }

        private void RemoveLine_Click(object sender, RoutedEventArgs e)
        {
            // Remove the line tied to the clicked button
            if (sender is FrameworkElement fe && fe.DataContext is EntryLine line)
            {
                Lines.Remove(line);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            // If they didn’t click "Add Line" but filled fields, we can auto-add one.
            bool hasTypedAnything =
                !string.IsNullOrWhiteSpace(WorkOrderBox.Text) ||
                !string.IsNullOrWhiteSpace(SiteReasonBox.Text) ||
                (QtyBox.Text?.Trim() != "1" && !string.IsNullOrWhiteSpace(QtyBox.Text));

            if (Lines.Count == 0 && hasTypedAnything)
            {
                if (!TryBuildLine(out var line))
                    return;

                Lines.Add(line);
            }

            if (Lines.Count == 0)
            {
                MessageBox.Show("Add at least one line before continuing.", "No Lines", MessageBoxButton.OK, MessageBoxImage.Information);
                WorkOrderBox.Focus();
                return;
            }

            DialogResult = true;
            Close();
        }

        // Enter key adds a line (tech-friendly)
        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddLine_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        // Only digits in Qty box
        private void QtyBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = e.Text.Any(ch => !char.IsDigit(ch));
        }
    }
}

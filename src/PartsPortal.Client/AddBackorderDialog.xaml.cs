using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace PartsPortal
{
    public partial class AddBackorderDialog : Window
    {
        public class EntryLine
        {
            public int Qty { get; set; } = 1;
            public string WorkOrder { get; set; } = "";
            public string SiteReason { get; set; } = "";
        }

        private EntryLine _result = new();

        // BackordersWindow expects this shape
        public IReadOnlyList<EntryLine> ResultLines => new[] { _result };

        // ✅ Base constructor (ADD mode)
        public AddBackorderDialog(string description, string material)
        {
            InitializeComponent();

            PartDesc.Text = description ?? "";
            PartMat.Text = material ?? "";

            QtyBox.Text = "1";
            
            Loaded += (_, __) =>
            {
                SiteReasonBox.Focus();
                SiteReasonBox.SelectAll();
            };
        }

        // ✅ Overload constructor (EDIT mode) - prefill fields
        public AddBackorderDialog(string description, string material, EntryLine prefill)
            : this(description, material)
        {
            if (prefill == null) return;

            SiteReasonBox.Text = prefill.SiteReason ?? "";
            WorkOrderBox.Text = prefill.WorkOrder ?? "";

            QtyBox.Text = Math.Max(1, prefill.Qty).ToString();
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse((QtyBox.Text ?? "").Trim(), out var qty) ||
                qty < 1 || qty > 999999)
            {
                MessageBox.Show(
                    "Enter a quantity between 1 and 999999.",
                    "Add Backorder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                QtyBox.Focus();
                QtyBox.SelectAll();
                return;
            }

            _result = new EntryLine
            {
                Qty = qty,
                SiteReason = (SiteReasonBox.Text ?? "").Trim(),
                WorkOrder = (WorkOrderBox.Text ?? "").Trim()
            };

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Enter submits (tech-friendly)
        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Add_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void QtyBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = e.Text.Any(ch => !char.IsDigit(ch));
        }

        private void QtyBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            var data = e.SourceDataObject;
            var format = data.GetDataPresent(DataFormats.UnicodeText)
                ? DataFormats.UnicodeText
                : DataFormats.Text;

            if (!data.GetDataPresent(format) ||
                data.GetData(format) is not string text ||
                text.Length == 0 ||
                text.Any(ch => !char.IsDigit(ch)))
            {
                e.CancelCommand();
            }
        }

    }
}

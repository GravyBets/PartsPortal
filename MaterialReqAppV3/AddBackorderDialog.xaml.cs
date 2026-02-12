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

            // Qty 1..10
            QtyCombo.ItemsSource = Enumerable.Range(1, 10).ToList();
            QtyCombo.SelectedIndex = 0;
            
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

            int q = prefill.Qty;
            if (q < 1) q = 1;
            if (q > 10) q = 10;

            // ItemsSource is ints 1..10
            QtyCombo.SelectedItem = q;
            if (QtyCombo.SelectedItem == null)
                QtyCombo.SelectedIndex = q - 1;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            _result = BuildLine();
            DialogResult = true;
            Close();
        }

        private EntryLine BuildLine()
        {
            int qty = 1;
            if (QtyCombo.SelectedItem is int q) qty = q;

            return new EntryLine
            {
                Qty = qty,
                SiteReason = (SiteReasonBox.Text ?? "").Trim(),
                WorkOrder = (WorkOrderBox.Text ?? "").Trim() // optional
            };
        }

        private int GetSelectedQty()
        {
            // 1) typed text wins
            if (int.TryParse((QtyCombo.Text ?? "").Trim(), out int typed) && typed > 0)
                return typed;

            // 2) dropdown selection
            if (QtyCombo.SelectedItem is int q && q >= 1)
                return q;

            if (QtyCombo.SelectedIndex >= 0)
                return QtyCombo.SelectedIndex + 1;

            return 1;
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

        private void QtyCombo_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // digits only
            e.Handled = e.Text.Any(ch => !char.IsDigit(ch));
        }

        private void QtyCombo_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            NormalizeQtyText();
        }

        private void NormalizeQtyText()
        {
            var t = (QtyCombo.Text ?? "").Trim();

            if (!int.TryParse(t, out int qty) || qty < 1)
            {
                QtyCombo.Text = "1";
                return;
            }

            // Keep what they typed (supports > 10)
            QtyCombo.Text = qty.ToString();
        }

    }
}

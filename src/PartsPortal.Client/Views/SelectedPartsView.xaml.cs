using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PartsPortal.Views
{
    public partial class SelectedPartsView : UserControl
    {
        private static readonly Regex NonDigits = new(@"\D+");

        public SelectedPartsView()
        {
            InitializeComponent();
        }

        private void QtyTextBox_GotKeyboardFocus(
            object sender,
            KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox box)
                box.SelectAll();
        }

        private void QtyTextBox_PreviewTextInput(
            object sender,
            TextCompositionEventArgs e)
        {
            e.Handled = NonDigits.IsMatch(e.Text);
        }

        private void QtyTextBox_Pasting(
            object sender,
            DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                e.CancelCommand();
                return;
            }

            var text = e.SourceDataObject.GetData(
                DataFormats.UnicodeText) as string;

            if (string.IsNullOrWhiteSpace(text) ||
                NonDigits.IsMatch(text))
            {
                e.CancelCommand();
            }
        }

        private void QtyTextBox_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (sender is not TextBox box)
                return;

            if (e.Key == Key.Enter)
            {
                CommitQuantity(box);
                Keyboard.ClearFocus();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                box.GetBindingExpression(TextBox.TextProperty)
                    ?.UpdateTarget();

                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private static void CommitQuantity(TextBox box)
        {
            if (!int.TryParse(box.Text, out var quantity) ||
                quantity < 1)
            {
                box.Text = "1";
            }

            box.GetBindingExpression(TextBox.TextProperty)
                ?.UpdateSource();
        }
    }
}

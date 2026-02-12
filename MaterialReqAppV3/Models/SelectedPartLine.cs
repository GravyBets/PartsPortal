using System.ComponentModel;

namespace PartsPortal
{
    public class SelectedPartLine : INotifyPropertyChanged
    {
        public Part Part { get; }

        public string Description => Part.Description;
        public string Material => Part.Material;

        private int _qty = 1;
        public int Qty
        {
            get => _qty;
            set
            {
                if (_qty != value)
                {
                    _qty = value < 1 ? 1 : value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Qty)));
                }
            }
        }

        public SelectedPartLine(Part part) => Part = part;

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

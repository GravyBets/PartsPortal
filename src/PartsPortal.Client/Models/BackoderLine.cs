using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

public class BackorderLine : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime DateAdded { get; set; } = DateTime.Now;

    public int Qty { get; set; } = 1;
    public string WorkOrder { get; set; } = "";
    public string SiteReason { get; set; } = "";

    public string Description { get; set; } = "";
    public string Material { get; set; } = "";

    // workflow
    public bool IsOrdered { get; set; } = false;
    public DateTime? DateOrdered { get; set; }

    private bool _isChecked;

    [JsonIgnore] // IMPORTANT: don't persist selection state
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged();
        }
    }
}



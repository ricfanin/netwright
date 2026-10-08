using System.ComponentModel;

namespace Netwright.Fixtures.Wpf;

/// <summary>View model behind txtBound and lblBound (see FIXTURE-CONTRACT.md).</summary>
public sealed class BoundModel : INotifyPropertyChanged
{
    private string _value = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
    }
}

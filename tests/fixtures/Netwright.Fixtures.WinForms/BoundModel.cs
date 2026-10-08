using System.ComponentModel;

namespace Netwright.Fixtures.WinForms
{
    /// <summary>Data source behind txtBound and lblBound (see FIXTURE-CONTRACT.md).</summary>
    internal sealed class BoundModel : INotifyPropertyChanged
    {
        private string _value = string.Empty;

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
}

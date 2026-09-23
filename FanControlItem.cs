using System;
using System.ComponentModel;
using System.Windows.Input;
using LibreHardwareMonitor.Hardware;

namespace TinyFanControl
{
    public class FanControlItem : INotifyPropertyChanged
    {
        public IControl Control { get; set; } = null!;

        // Kept so a reading can be refreshed on demand (one register read per
        // user action) without any background polling.
        public ISensor Sensor { get; set; } = null!;
        public IHardware Hardware { get; set; } = null!;

        public string Name { get; set; } = string.Empty;

        private double _value;
        public double Value
        {
            get => _value;
            set { _value = Math.Clamp(value, 0, 100); OnPropertyChanged(nameof(Value)); }
        }

        private bool _isManual;
        public bool IsManual
        {
            get => _isManual;
            set { _isManual = value; OnPropertyChanged(nameof(IsManual)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        public RelayCommand(Action<T?> execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => _execute((T?)p);
    }

    public class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer c)   { c.Traverse(this); }
        public void VisitHardware(IHardware h)   { h.Update(); foreach (var s in h.SubHardware) s.Accept(this); }
        public void VisitSensor(ISensor s)        { }
        public void VisitParameter(IParameter p) { }
    }
}

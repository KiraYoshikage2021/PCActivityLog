using System.Windows.Input;

namespace PCActivityLog.Services;

/// <summary>轻量 ICommand 实现（托盘点击等场景）。</summary>
public class RelayCommand : ICommand
{
    private readonly Action _action;
    public RelayCommand(Action action) => _action = action;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _action();
}

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace PCActivityLog.ViewModels;

/// <summary>
/// 支持批量替换/追加的 ObservableCollection。
/// DataGrid 对每次 Add/Remove 都会做行布局，普通写法（Clear + 循环 Add）
/// 对几百条数据会触发几百次集合变更通知，导致明显卡顿。
/// 本类在批量操作时绕过逐条通知，结束后只发一次 Reset。
/// </summary>
public class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// 整体替换集合内容（Clear + Add 只发一次 Reset 通知）。
    /// </summary>
    public void ReplaceRange(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        RaiseReset();
    }

    /// <summary>批量追加（只发一次 Reset 通知）。</summary>
    public void AppendRange(IEnumerable<T> items)
    {
        CheckReentrancy();
        var added = false;
        foreach (var item in items)
        {
            Items.Add(item);
            added = true;
        }
        if (added) RaiseReset();
    }

    /// <summary>发出一次"整体已重置"通知（含 Count/Item[] 属性变更）。</summary>
    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

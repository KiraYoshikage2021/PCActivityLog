using Microsoft.UI.Xaml.Controls;
using PCActivityLog.Models;

namespace PCActivityLog.Views;

/// <summary>
/// 添加记录对话框（ContentDialog 版）—— 手动补录事件。
/// 保存后通过 <see cref="Result"/> 返回给调用方入库。
/// </summary>
public sealed partial class AddEventDialog : ContentDialog
{
    /// <summary>用户确认后的结果（取消时为 null）。</summary>
    public ActivityEvent? Result { get; private set; }

    private readonly ComboBox _typeBox = new();
    private readonly TextBox _nameBox = new() { PlaceholderText = "必填，如软件名 / 文件名" };
    private readonly TextBox _pathBox = new() { PlaceholderText = "可选，如安装目录或文件路径" };
    private readonly DatePicker _datePicker = new();
    private readonly TimePicker _timePicker = new();
    private readonly TextBox _noteBox = new() { AcceptsReturn = true, Height = 70, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap };

    public AddEventDialog()
    {
        Title = "添加记录";
        PrimaryButtonText = "保存";
        CloseButtonText = "取消";
        DefaultButton = ContentDialogButton.Primary;

        // 每个下拉项直接携带其 EventType（存在 ComboBoxItem.Tag），
        // 避免"显示顺序"与"索引→类型映射表"两套逻辑不一致导致写错类型。
        var allTypes = Enum.GetValues<EventType>();
        foreach (var t in allTypes)
            _typeBox.Items.Add(new ComboBoxItem { Content = TypeDisplay(t), Tag = t });
        // 默认选中"安装"
        _typeBox.SelectedIndex = Math.Max(0, Array.IndexOf(allTypes, EventType.Install));

        _datePicker.SelectedDate = DateTimeOffset.Now;
        _timePicker.SelectedTime = DateTimeOffset.Now.TimeOfDay;

        var panel = new StackPanel { Spacing = 10, MinWidth = 380 };
        panel.Children.Add(Labeled("类型", _typeBox));
        panel.Children.Add(Labeled("名称 *", _nameBox));
        panel.Children.Add(Labeled("路径", _pathBox));
        var timeRow = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
        timeRow.Children.Add(_datePicker);
        timeRow.Children.Add(_timePicker);
        panel.Children.Add(Labeled("时间", timeRow));
        panel.Children.Add(Labeled("备注", _noteBox));
        Content = panel;

        PrimaryButtonClick += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(_nameBox.Text))
            {
                args.Cancel = true; // 名称必填
                return;
            }
            var date = _datePicker.Date.Date;
            var time = _timePicker.Time;
            Result = new ActivityEvent
            {
                Type = (_typeBox.SelectedItem as ComboBoxItem)?.Tag as EventType? ?? EventType.Manual,
                Name = _nameBox.Text.Trim(),
                Path = string.IsNullOrWhiteSpace(_pathBox.Text) ? null : _pathBox.Text.Trim(),
                Note = string.IsNullOrWhiteSpace(_noteBox.Text) ? null : _noteBox.Text.Trim(),
                Source = "manual",
                OccurredAt = date + time,
            };
        };
    }

    private static StackPanel Labeled(string label, Microsoft.UI.Xaml.UIElement control)
    {
        var p = new StackPanel { Spacing = 4 };
        p.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = Services.ThemeService.FindBrush("TextSecondaryBrush"),
        });
        p.Children.Add(control);
        return p;
    }

    /// <summary>事件类型的中文名（与 EventType 枚举一一对应，缺项会退回枚举名而非"手动"）。</summary>
    private static string TypeDisplay(EventType t) => t switch
    {
        EventType.Download => "下载",
        EventType.FileDelete => "删除",
        EventType.FileRename => "重命名",
        EventType.ImFile => "IM文件",
        EventType.Install => "安装",
        EventType.Update => "更新",
        EventType.Uninstall => "卸载",
        EventType.Boot => "开机",
        EventType.Shutdown => "关机",
        EventType.Restart => "重启",
        EventType.Sleep => "睡眠",
        EventType.Wake => "唤醒",
        EventType.Browse => "浏览",
        EventType.Manual => "手动",
        _ => t.ToString(),
    };
}

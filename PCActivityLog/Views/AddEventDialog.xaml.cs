using PCActivityLog.Services;
using System.Windows;
using PCActivityLog.Models;

namespace PCActivityLog.Views;

/// <summary>添加记录对话框 —— 手动补录事件。保存后通过 <see cref="Result"/> 返回给调用方入库。</summary>
public partial class AddEventDialog : Window
{
    /// <summary>用户确认后的结果（取消时为 null）。</summary>
    public ActivityEvent? Result { get; private set; }

    public AddEventDialog()
    {
        InitializeComponent();

        // 类型下拉：手动补录可选的所有事件类型
        foreach (EventType t in Enum.GetValues(typeof(EventType)))
            TypeBox.Items.Add(t.ToString() switch
            {
                nameof(EventType.Download) => "下载",
                nameof(EventType.FileDelete) => "删除",
                nameof(EventType.FileRename) => "重命名",
                nameof(EventType.Install) => "安装",
                nameof(EventType.Update) => "更新",
                nameof(EventType.Uninstall) => "卸载",
                nameof(EventType.Boot) => "开机",
                nameof(EventType.Shutdown) => "关机",
                nameof(EventType.Browse) => "浏览",
                _ => "手动",
            });
        TypeBox.SelectedIndex = 3; // 默认"安装"

        DatePicker.SelectedDate = DateTime.Now;
        TimeBox.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    /// <summary>标题栏跟随当前主题（浅/深）。</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeService.ApplyTitleBar(this);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, "名称不能为空。", "添加记录",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }

        var time = DatePicker.SelectedDate ?? DateTime.Now;
        if (TimeSpan.TryParse(TimeBox.Text, out var tod))
            time = time.Date + tod;
        else
            time = time.Date + DateTime.Now.TimeOfDay;

        var typeIndex = TypeBox.SelectedIndex;
        var type = typeIndex switch
        {
            0 => EventType.Download,
            1 => EventType.FileDelete,
            2 => EventType.FileRename,
            3 => EventType.Install,
            4 => EventType.Update,
            5 => EventType.Uninstall,
            6 => EventType.Boot,
            7 => EventType.Shutdown,
            8 => EventType.Browse,
            _ => EventType.Manual,
        };

        Result = new ActivityEvent
        {
            Type = type,
            Name = NameBox.Text.Trim(),
            Path = string.IsNullOrWhiteSpace(PathBox.Text) ? null : PathBox.Text.Trim(),
            Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim(),
            Source = "manual",
            OccurredAt = time,
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

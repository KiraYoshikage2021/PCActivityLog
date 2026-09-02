using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PCActivityLog.Services;

namespace PCActivityLog.Views;

/// <summary>
/// 备注编辑对话框 —— 纯代码构建的小窗口（多行文本 + 确定/取消）。
/// 颜色从当前主题资源取，浅/深主题下都正常显示。
/// 返回 null 表示用户取消。
/// </summary>
public static class NoteDialog
{
    public static string? Prompt(string title, string current, string tip)
    {
        var secondary = ThemeService.FindBrush("TextSecondaryBrush");

        var textBox = new TextBox
        {
            Text = current,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 96,
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8, 6, 8, 6),
        };

        var ok = new Button { Content = "确定", Padding = new Thickness(20, 6, 20, 6), IsDefault = true, MinWidth = 84,
                              Style = (Style)Application.Current.FindResource("PrimaryButton") };
        var cancel = new Button { Content = "取消", Padding = new Thickness(20, 6, 20, 6), IsCancel = true, MinWidth = 84, Margin = new Thickness(8, 0, 0, 0) };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(18) };
        if (!string.IsNullOrEmpty(tip))
        {
            panel.Children.Add(new TextBlock
            {
                Text = tip,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap,
                Foreground = secondary,
            });
        }
        panel.Children.Add(textBox);
        panel.Children.Add(new Border { Height = 14 });
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = title,
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive),
            Content = panel,
            SizeToContent = SizeToContent.Height,
            Width = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
        window.SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(window);

        string? result = null;
        ok.Click += (_, _) => { result = textBox.Text; window.Close(); };
        cancel.Click += (_, _) => window.Close();
        textBox.Focus();
        textBox.SelectAll();
        window.ShowDialog();
        return result;
    }
}

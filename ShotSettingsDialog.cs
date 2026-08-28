using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PcCheck;

/// <summary>截图设置对话框（移植浮窗 ShotSettingsDialog 核心项）：热键/格式/隐藏自身/保存目录。</summary>
public class ShotSettingsDialog : Window
{
    readonly MonitorConfig _cfg;
    readonly TextBox _hotkeyBox = new() { Width = 220 };
    readonly ComboBox _formatBox = new() { Width = 220 };
    readonly CheckBox _hideSelf = new() { Content = "截图时隐藏浮窗自身", Margin = new Thickness(0, 4, 0, 4) };
    readonly TextBox _dirBox = new() { Width = 220 };

    public ShotSettingsDialog(MonitorConfig cfg)
    {
        _cfg = cfg;
        Title = "截图设置";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        FontSize = 13;

        _hotkeyBox.Text = cfg.ShotHotkey;
        _formatBox.Items.Add("png"); _formatBox.Items.Add("jpg"); _formatBox.Items.Add("bmp");
        _formatBox.SelectedItem = ScreenshotUtil.NormalizeExt(cfg.ShotFormat);
        _hideSelf.IsChecked = cfg.ShotHideSelf;
        _dirBox.Text = cfg.ShotDir;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "截图热键（留空 = 禁用，格式如 Ctrl+Alt+S）", Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(_hotkeyBox);
        root.Children.Add(new TextBlock { Text = "保存格式", Margin = new Thickness(0, 12, 0, 4) });
        root.Children.Add(_formatBox);
        root.Children.Add(_hideSelf);
        root.Children.Add(new TextBlock { Text = "保存目录（空 = 桌面\\截图）", Margin = new Thickness(0, 8, 0, 4) });
        var dirRow = new StackPanel { Orientation = Orientation.Horizontal };
        dirRow.Children.Add(_dirBox);
        var browse = new Button { Content = "浏览…", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (s, e) =>
        {
            var dlg = new OpenFolderDialog { Title = "选择截图保存目录" };
            if (dlg.ShowDialog() == true) _dirBox.Text = dlg.FolderName;
        };
        dirRow.Children.Add(browse);
        root.Children.Add(dirRow);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var ok = new Button { Content = "确定", Width = 90, Height = 32, Margin = new Thickness(0, 0, 10, 0) };
        ok.Click += (s, e) => { DialogResult = true; };
        var cancel = new Button { Content = "取消", Width = 90, Height = 32 };
        cancel.Click += (s, e) => { DialogResult = false; };
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        root.Children.Add(btnRow);

        Content = root;
    }

    public void ApplyToConfig()
    {
        _cfg.ShotHotkey = _hotkeyBox.Text.Trim();
        _cfg.ShotFormat = _formatBox.SelectedItem?.ToString() ?? "png";
        _cfg.ShotHideSelf = _hideSelf.IsChecked == true;
        _cfg.ShotDir = _dirBox.Text.Trim();
    }
}

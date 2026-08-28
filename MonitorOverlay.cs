using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PcCheck;

/// <summary>完整版内置悬浮窗（移植硬件监测浮窗全部功能）：
/// 右键菜单（锁定/位置/字号/透明度/校准/显示项/截图/截图设置/开机自启/说明/退出）、
/// 拖动 + 双击锁定、不抢焦点、全局截图热键、功耗估算、FPS 1% low、配置持久化 config.json。</summary>
public class MonitorOverlay : Window
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const int FpsHistorySize = 120;

    readonly MonitorConfig _cfg = MonitorConfig.Load();
    readonly TextBlock _body = new();
    readonly TextBlock _status = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Queue<float> _fpsHistory = new();

    HwndSource _source;
    (uint mods, uint vk)? _hotkey;
    bool _shooting;
    bool _dragging;
    Point _dragOffset;
    (string Text, DateTime Until)? _statusUntil;

    readonly MenuItem _miLock = new();
    readonly Dictionary<string, MenuItem> _showItems = new();
    readonly MenuItem _miCalAuto = new();
    readonly Dictionary<int, MenuItem> _calItems = new();
    readonly MenuItem _miAutoStart = new();
    readonly MenuItem _miShot = new();

    // 主窗口共享的最新数据（主窗采集，本窗只读展示）
    public LiveData Latest { get; set; } = new();
    public float? LatestFps { get; set; }

    static readonly Color CMain = Color.FromRgb(0xEB, 0xEB, 0xEB);
    static readonly Color CDim = Color.FromRgb(0xAA, 0xAA, 0xAA);
    static readonly Color CRed = Color.FromRgb(0xFF, 0x5A, 0x5A);
    static readonly Color COrange = Color.FromRgb(0xFF, 0xAA, 0x3C);

    public MonitorOverlay()
    {
        Title = "小白电脑体检 · 悬浮窗";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x21, 0x25, 0x2B));
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = Math.Clamp(_cfg.OpacityPercent, 20, 100) / 100.0;

        if (_cfg.X >= 0 && _cfg.Y >= 0) { Left = _cfg.X; Top = _cfg.Y; }
        else { Left = SystemParameters.WorkArea.Right - 270; Top = SystemParameters.WorkArea.Bottom - 170; }

        var root = new StackPanel { Margin = new Thickness(6, 5, 6, 5) };
        _body.FontFamily = new FontFamily("Consolas");
        _body.FontSize = _cfg.FontSize; // 字号档位保持原样
        _body.FontWeight = FontWeights.Bold; // 保留加粗
        _body.Foreground = new SolidColorBrush(CMain);
        _body.TextWrapping = TextWrapping.NoWrap;
        _body.LineHeight = _cfg.FontSize * 1.2; // 压行距
        _body.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        root.Children.Add(_body);
        _status.Foreground = new SolidColorBrush(CDim);
        _status.FontFamily = new FontFamily("Consolas");
        _status.FontSize = Math.Max(10, _cfg.FontSize - 2);
        _status.Visibility = Visibility.Collapsed; // 无提示时不占空间
        root.Children.Add(_status);
        var hint = new TextBlock
        {
            Text = "可以右键我",
            Foreground = new SolidColorBrush(CDim),
            FontSize = 10,
            Margin = new Thickness(0, 3, 0, 0)
        };
        root.Children.Add(hint);
        Content = root;

        BuildMenu();

        MouseLeftButtonDown += OnOverlayMouseDown;
        MouseMove += OnOverlayMouseMove;
        MouseLeftButtonUp += OnOverlayMouseUp;

        SourceInitialized += (s, e) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(hwnd);
            _source.AddHook(WndProc);
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE); // 不抢焦点
            ApplyHotkey();
        };

        _timer.Tick += (s, e) => Refresh();
        _timer.Start();
        Closed += (s, e) =>
        {
            _timer.Stop();
            try { HotkeyUtil.Unregister(new WindowInteropHelper(this).Handle); } catch { }
            _source?.RemoveHook(WndProc);
            _cfg.Save();
        };
    }

    // ============ 显示刷新 ============
    void Refresh()
    {
        var d = Latest;
        AddFpsSample(LatestFps);
        float? low = ComputeOnePercentLow();
        var inl = new List<Inline>();
        bool first = true;

        void Row(Action<List<Inline>> fill)
        {
            if (!first) inl.Add(new LineBreak());
            first = false;
            fill(inl);
        }

        if (_cfg.ShowFps)
        {
            if (LatestFps is float fps && fps > 0)
            {
                Row(ins =>
                {
                    ins.Add(NewRun("FPS", CMain));
                    ins.Add(NewRun(" " + fps.ToString("F0"), CMain));
                    if (low is float l) ins.Add(NewRun(" 1% " + l.ToString("F0"), CDim));
                });
            }
            else if (low is float l2)
            {
                Row(ins => { ins.Add(NewRun("FPS", CMain)); ins.Add(NewRun(" --", CDim)); ins.Add(NewRun(" 1% " + l2.ToString("F0"), CDim)); });
            }
        }

        if (_cfg.ShowCpuTemp || _cfg.ShowCpuLoad)
        {
            Row(ins =>
            {
                ins.Add(NewRun("CPU", CMain));
                if (_cfg.ShowCpuTemp) { float? t = ApplyOffset(d.CpuTemp); ins.Add(NewRun(" " + FormatTemp(t), TempColor(t))); }
                if (_cfg.ShowCpuLoad) ins.Add(NewRun(" " + FormatPercent(d.CpuLoad), LoadColor(d.CpuLoad)));
                if (d.CpuClock is float clk && clk > 0) ins.Add(NewRun(" " + FormatClock(clk), CDim));
            });
        }

        if (_cfg.ShowGpuTemp || _cfg.ShowGpuLoad || _cfg.ShowGpuMem)
        {
            Row(ins =>
            {
                ins.Add(NewRun("GPU", CMain));
                if (_cfg.ShowGpuTemp) ins.Add(NewRun(" " + FormatTemp(d.GpuTemp), TempColor(d.GpuTemp)));
                if (_cfg.ShowGpuLoad) ins.Add(NewRun(" " + FormatPercent(d.GpuLoad), LoadColor(d.GpuLoad)));
                if (_cfg.ShowGpuMem) ins.Add(NewRun(" " + FormatGpuMem(d.GpuMemUsedMb, d.GpuMemTotalMb), CDim));
            });
        }

        if (_cfg.ShowRam && d.RamTotalGb is float rt && rt > 0)
        {
            Row(ins =>
            {
                ins.Add(NewRun("RAM", CMain));
                ins.Add(NewRun(" " + FormatMemPair(d.RamUsedGb, rt), CMain));
                if (d.RamUsedGb is float ru) ins.Add(NewRun(" " + FormatPercent(ru / rt * 100), LoadColor(ru / rt * 100)));
            });
        }

        if (_cfg.ShowFan && d.FanRpm is float rpm && rpm > 0)
        {
            Row(ins => ins.Add(NewRun($"FAN {rpm:F0} RPM", CDim)));
        }

        if (_cfg.ShowPower)
        {
            float? sys = EstimateSystemPower(d.CpuPower, d.GpuPower);
            if (sys is float s)
            {
                Row(ins =>
                {
                    ins.Add(NewRun($"功耗≈{s:F0}W", CMain));
                    var parts = new List<string>();
                    if (d.CpuPower is float cp) parts.Add($"CPU {cp:F0}");
                    if (d.GpuPower is float gp) parts.Add($"GPU {gp:F0}");
                    if (_cfg.ShowPowerDetail && parts.Count > 0) ins.Add(NewRun(" " + string.Join(" ", parts), CDim));
                });
            }
        }

        if (inl.Count == 0) // 全部显示项关闭时的兜底
        {
            inl.Add(NewRun("CPU", CMain));
            inl.Add(NewRun(" " + FormatTemp(ApplyOffset(d.CpuTemp)), TempColor(ApplyOffset(d.CpuTemp))));
        }

        _body.Inlines.Clear();
        _body.Inlines.AddRange(inl);

        if (_statusUntil is { } st && DateTime.Now >= st.Until)
        {
            _status.Text = "";
            _statusUntil = null;
        }
        _status.Visibility = string.IsNullOrEmpty(_status.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    static Run NewRun(string text, Color c) => new(text) { Foreground = new SolidColorBrush(c) };

    float? ApplyOffset(float? v) => v is float t ? t + Calibration.Offset : null;

    float? EstimateSystemPower(float? cpu, float? gpu)
    {
        float sum = 0; bool any = false;
        if (cpu is float c) { sum += c; any = true; }
        if (gpu is float g) { sum += g; any = true; }
        return any ? sum + (float)_cfg.PowerBaseWatts : null;
    }

    void AddFpsSample(float? fps)
    {
        if (fps is float f && f > 0)
        {
            _fpsHistory.Enqueue(f);
            if (_fpsHistory.Count > FpsHistorySize) _fpsHistory.Dequeue();
        }
    }

    float? ComputeOnePercentLow()
    {
        if (_fpsHistory.Count < 10) return null;
        var arr = _fpsHistory.ToArray();
        Array.Sort(arr);
        int take = Math.Max(1, arr.Length / 100);
        float sum = 0;
        for (int i = 0; i < take; i++) sum += arr[i];
        return sum / take;
    }

    static string FormatTemp(float? v) => v is float t ? $"{t:F0}°C" : "--";
    static string FormatPercent(float? v) => v is float p ? $"{p:F0}%" : "--";
    static string FormatClock(float mhz) => mhz >= 1000 ? $"{mhz / 1000:F1}G" : $"{mhz:F0}M";
    static string FormatMemPair(float? used, float? total)
    {
        if (used is not float u || u <= 0) return "--";
        if (total is float t && t > 0) return $"{u:F1}/{t:F1}G";
        return $"{u:F1}G";
    }
    static string FormatGpuMem(float? usedMb, float? totalMb)
    {
        if (usedMb is not float u || u <= 0) return "--";
        var usedG = u / 1024f;
        if (totalMb is float t && t > 0) return $"{usedG:F1}/{t / 1024f:F1}G";
        return $"{usedG:F1}G";
    }
    static Color TempColor(float? v)
    {
        if (v is not float t) return CDim;
        if (t >= 80) return CRed;
        if (t >= 60) return COrange;
        return CMain;
    }
    static Color LoadColor(float? v)
    {
        if (v is not float p) return CDim;
        if (p >= 90) return CRed;
        if (p >= 70) return COrange;
        return CMain;
    }

    // ============ 右键菜单 ============
    void BuildMenu()
    {
        var menu = new ContextMenu();

        _miLock.Header = "锁定位置";
        _miLock.IsCheckable = true;
        _miLock.IsChecked = _cfg.Locked;
        _miLock.Click += (s, e) => { _cfg.Locked = _miLock.IsChecked; _cfg.Save(); ShowStatus(_cfg.Locked ? "已锁定（双击解锁）" : "已解锁"); };

        var miPos = new MenuItem { Header = "位置" };
        AddSub(miPos, "左上角", 0); AddSub(miPos, "右上角", 1); AddSub(miPos, "左下角", 2); AddSub(miPos, "右下角", 3); AddSub(miPos, "屏幕中央", 4);

        var miFont = new MenuItem { Header = "字号" };
        AddSub(miFont, "小", 11); AddSub(miFont, "中", 13); AddSub(miFont, "大", 16);

        var miOp = new MenuItem { Header = "透明度" };
        AddSub(miOp, "60%", 60); AddSub(miOp, "80%", 80); AddSub(miOp, "100%", 100);

        var miCal = new MenuItem { Header = "CPU温度校准" };
        _miCalAuto.Header = "自动检测(推荐)";
        _miCalAuto.IsCheckable = true;
        _miCalAuto.Click += (s, e) =>
        {
            Calibration.AutoDetect(MainWindow.CachedCpuName);
            _cfg.CpuTempOffset = (int)Calibration.Offset;
            _cfg.Calibrated = true;
            _cfg.Save();
            ShowStatus($"已自动校准（偏移 {Calibration.Offset:+0;-0}°C）");
        };
        miCal.Items.Add(_miCalAuto);
        miCal.Items.Add(new Separator());
        foreach (var off in new[] { 0, -5, -11, -20 })
        {
            var item = new MenuItem { Header = off == 0 ? "不校准(0°C)" : $"{off:+0;-0}°C", IsCheckable = true, Tag = off };
            item.Click += (s, e) =>
            {
                int v = (int)((MenuItem)s).Tag;
                Calibration.SetManual(v);
                _cfg.CpuTempOffset = v;
                _cfg.Calibrated = true;
                _cfg.Save();
                ShowStatus($"校准偏移已设为 {v:+0;-0}°C");
            };
            _calItems[off] = item;
            miCal.Items.Add(item);
        }

        var miShow = new MenuItem { Header = "显示项" };
        AddShowItem(miShow, "帧率 FPS", "fps");
        AddShowItem(miShow, "CPU 温度", "cpuTemp");
        AddShowItem(miShow, "CPU 占用", "cpuLoad");
        AddShowItem(miShow, "GPU 温度", "gpuTemp");
        AddShowItem(miShow, "GPU 占用", "gpuLoad");
        AddShowItem(miShow, "显存使用", "gpuMem");
        AddShowItem(miShow, "内存", "ram");
        AddShowItem(miShow, "风扇转速", "fan");
        AddShowItem(miShow, "整机功耗", "power");
        AddShowItem(miShow, "功耗构成", "powerDetail");

        _miShot.Header = "立即截图";
        _miShot.Click += (s, e) => _ = TakeScreenshotAsync();
        var miShotSettings = new MenuItem { Header = "截图设置…" };
        miShotSettings.Click += (s, e) => ShowShotSettings();

        _miAutoStart.Header = "开机自启";
        _miAutoStart.IsCheckable = true;
        _miAutoStart.IsChecked = _cfg.AutoStart;
        _miAutoStart.Click += (s, e) =>
        {
            _cfg.AutoStart = _miAutoStart.IsChecked;
            ApplyAutoStart(_cfg.AutoStart);
            _cfg.Save();
            ShowStatus(_cfg.AutoStart ? "已设置开机自启" : "已取消开机自启");
        };

        var miHelp = new MenuItem { Header = "使用说明" };
        miHelp.Click += (s, e) => ShowHelp();
        var miExit = new MenuItem { Header = "退出" };
        miExit.Click += (s, e) => Close();

        menu.Items.Add(_miLock);
        menu.Items.Add(miPos);
        menu.Items.Add(miFont);
        menu.Items.Add(miOp);
        menu.Items.Add(miCal);
        menu.Items.Add(miShow);
        menu.Items.Add(_miShot);
        menu.Items.Add(miShotSettings);
        menu.Items.Add(new Separator());
        menu.Items.Add(_miAutoStart);
        menu.Items.Add(new Separator());
        menu.Items.Add(miHelp);
        menu.Items.Add(miExit);

        ContextMenu = menu;
        ContextMenuOpening += (s, e) => UpdateMenuChecks();
    }

    void AddSub(MenuItem parent, string text, object tag)
    {
        var item = new MenuItem { Header = text, Tag = tag };
        item.Click += (s, e) =>
        {
            object t = ((MenuItem)s).Tag;
            if (parent.Header.ToString() == "位置") MovePreset((int)t);
            else if (parent.Header.ToString() == "字号") SetFontSize((int)t);
            else if (parent.Header.ToString() == "透明度") SetOpacity((int)t);
        };
        parent.Items.Add(item);
    }

    void AddShowItem(MenuItem parent, string text, string key)
    {
        var item = new MenuItem { Header = text, IsCheckable = true, Tag = key };
        item.Click += (s, e) =>
        {
            bool v = item.IsChecked;
            switch (key)
            {
                case "fps": _cfg.ShowFps = v; break;
                case "cpuTemp": _cfg.ShowCpuTemp = v; break;
                case "cpuLoad": _cfg.ShowCpuLoad = v; break;
                case "gpuTemp": _cfg.ShowGpuTemp = v; break;
                case "gpuLoad": _cfg.ShowGpuLoad = v; break;
                case "gpuMem": _cfg.ShowGpuMem = v; break;
                case "ram": _cfg.ShowRam = v; break;
                case "fan": _cfg.ShowFan = v; break;
                case "power": _cfg.ShowPower = v; break;
                case "powerDetail": _cfg.ShowPowerDetail = v; break;
            }
            _cfg.Save();
            Refresh();
        };
        _showItems[key] = item;
        parent.Items.Add(item);
    }

    void UpdateMenuChecks()
    {
        _miLock.IsChecked = _cfg.Locked;
        _miAutoStart.IsChecked = _cfg.AutoStart;
        _miCalAuto.IsChecked = _cfg.Calibrated && _cfg.CpuTempOffset == (int)Calibration.Offset;
        _miShot.Header = string.IsNullOrWhiteSpace(_cfg.ShotHotkey) ? "立即截图" : $"立即截图 ({_cfg.ShotHotkey})";
        foreach (var (off, item) in _calItems) item.IsChecked = _cfg.CpuTempOffset == off;
        foreach (var (key, item) in _showItems)
        {
            item.IsChecked = key switch
            {
                "fps" => _cfg.ShowFps,
                "cpuTemp" => _cfg.ShowCpuTemp,
                "cpuLoad" => _cfg.ShowCpuLoad,
                "gpuTemp" => _cfg.ShowGpuTemp,
                "gpuLoad" => _cfg.ShowGpuLoad,
                "gpuMem" => _cfg.ShowGpuMem,
                "ram" => _cfg.ShowRam,
                "fan" => _cfg.ShowFan,
                "power" => _cfg.ShowPower,
                "powerDetail" => _cfg.ShowPowerDetail,
                _ => item.IsChecked
            };
        }
    }

    void SetFontSize(int size)
    {
        _cfg.FontSize = size;
        _body.FontSize = size;
        _status.FontSize = Math.Max(10, size - 2);
        _cfg.Save();
    }

    void SetOpacity(int percent)
    {
        _cfg.OpacityPercent = percent;
        Opacity = Math.Clamp(percent, 20, 100) / 100.0;
        _cfg.Save();
    }

    void MovePreset(int preset)
    {
        var wa = SystemParameters.WorkArea;
        double w = ActualWidth, h = ActualHeight;
        (double x, double y) = preset switch
        {
            0 => (wa.Left + 20, wa.Top + 20),
            1 => (wa.Right - w - 20, wa.Top + 20),
            2 => (wa.Left + 20, wa.Bottom - h - 20),
            3 => (wa.Right - w - 20, wa.Bottom - h - 20),
            _ => (wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2)
        };
        Left = x; Top = y;
        SavePosition();
    }

    void SavePosition()
    {
        _cfg.X = (int)Left;
        _cfg.Y = (int)Top;
        _cfg.Save();
    }

    void ApplyAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (key == null) return;
            if (enable) key.SetValue("PcCheck", "\"" + Environment.ProcessPath + "\"");
            else key.DeleteValue("PcCheck", false);
        }
        catch { }
    }

    // ============ 拖动 & 双击锁定 ============
    void OnOverlayMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleLock(); return; }
        if (!_cfg.Locked)
        {
            _dragging = true;
            _dragOffset = e.GetPosition(this);
            CaptureMouse();
        }
    }

    void OnOverlayMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            var p = PointToScreen(e.GetPosition(this));
            Left = p.X - _dragOffset.X;
            Top = p.Y - _dragOffset.Y;
        }
    }

    void OnOverlayMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            SavePosition();
        }
    }

    void ToggleLock()
    {
        _cfg.Locked = !_cfg.Locked;
        _cfg.Save();
        ShowStatus(_cfg.Locked ? "已锁定位置（双击解锁）" : "已解锁");
    }

    // ============ 截图 ============
    void ApplyHotkey()
    {
        try { HotkeyUtil.Unregister(new WindowInteropHelper(this).Handle); } catch { }
        _hotkey = HotkeyUtil.Parse(_cfg.ShotHotkey);
        if (_hotkey is { } hk)
        {
            if (!HotkeyUtil.Register(new WindowInteropHelper(this).Handle, hk.mods, hk.vk))
                ShowStatus("热键注册失败，可能被其它程序占用");
        }
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (HotkeyUtil.IsHotkeyMessage(msg, wParam))
        {
            _ = TakeScreenshotAsync();
            handled = true;
        }
        return IntPtr.Zero;
    }

    async Task TakeScreenshotAsync()
    {
        if (_shooting) return;
        _shooting = true;
        bool hide = _cfg.ShotHideSelf;
        try
        {
            if (hide)
            {
                Visibility = Visibility.Hidden;
                await Task.Delay(150);
            }
            using var bmp = ScreenshotUtil.CaptureVirtualScreen();
            string path = ScreenshotUtil.Save(bmp, _cfg);
            try { Clipboard.SetText(path); } catch { }
            ShowStatus($"已保存: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            ShowStatus($"截图失败: {ex.Message}");
        }
        finally
        {
            if (hide) Visibility = Visibility.Visible;
            _shooting = false;
        }
    }

    void ShowShotSettings()
    {
        var dlg = new ShotSettingsDialog(_cfg);
        if (dlg.ShowDialog() == true)
        {
            dlg.ApplyToConfig();
            _cfg.Save();
            ApplyHotkey();
            ShowStatus("截图设置已保存");
        }
    }

    void ShowStatus(string text)
    {
        _status.Text = text;
        _statusUntil = (text, DateTime.Now.AddSeconds(4));
    }

    void ShowHelp()
    {
        MessageBox.Show(this,
            "小白电脑体检 · 悬浮窗\n\n" +
            "• 左键按住拖动 = 移动位置\n" +
            "• 双击 = 锁定 / 解锁位置\n" +
            "• 右键 = 主菜单（位置/字号/透明度/显示项/校准/截图/退出）\n" +
            "• 截图：右键 → 立即截图，或按全局热键（默认 Ctrl+Alt+S）\n" +
            "  可在 截图设置… 里自定义热键、格式（PNG/JPG/BMP）、\n" +
            "  是否隐藏浮窗自身、保存位置\n\n" +
            "• 整机功耗为软件估算：CPU/GPU 真实读数 + 底噪常数\n" +
            "  （默认 45W，可在 config.json 的 PowerBaseWatts 调整）\n\n" +
            "• 帧率（FPS）检测基于 RTSS（MSI Afterburner 组件）\n\n" +
            "提示：程序需要管理员权限读取 CPU 温度，启动弹 UAC 属正常。",
            "使用说明", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}

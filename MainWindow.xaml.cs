using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PcCheck;

public partial class MainWindow : Window
{
    /// <summary>本机 CPU 型号缓存（悬浮窗校准用）。</summary>
    public static string CachedCpuName = "";

    readonly HardwareSensors _sensors = new();
    readonly DispatcherTimer _monTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly TextBlock[] _monValues = new TextBlock[11];
    MonitorOverlay _overlay;
    LiveData _latest = new();
    float? _latestFps;
    string _lastReport = "";
    string _lastDeep = "";

    static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(0x8A, 0x91, 0x9E));
    static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x2F, 0x9E, 0x44));
    static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xE8, 0x59, 0x0C));
    static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xE0, 0x31, 0x31));
    static readonly Brush Dark = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));

    public MainWindow()
    {
        InitializeComponent();
        BuildMonitorRows();
        BuildSites();
        LogService.Append("程序启动");

        Loaded += async (s, e) =>
        {
            await RefreshReportAsync();
            RefreshDeep();
            logText.Text = LogService.ReadAll();
        };
        _monTimer.Tick += async (s, e) => await PollOnceAsync();
        _monTimer.Start();
        // 退出逻辑加固：Dispose 可能卡（LHM Close 偶发挂起），带超时 + 兜底强制退出
        Closed += (s, e) =>
        {
            _monTimer.Stop();
            try { LogService.Append("程序退出"); } catch { }
            try
            {
                Task.Run(() => { try { _sensors.Dispose(); } catch { } }).Wait(2000);
            }
            catch { }
            try { Environment.Exit(0); } catch { } // 兜底：保证进程一定退出
        };
    }

    // ============ 实时监控行 ============
    void BuildMonitorRows()
    {
        string[] keys =
        {
            "CPU 温度", "CPU 占用", "CPU 频率", "CPU 功耗",
            "GPU 温度", "GPU 占用", "GPU 显存", "GPU 功耗",
            "内存", "风扇", "帧率 FPS"
        };
        for (int i = 0; i < keys.Length; i++)
        {
            monGrid.RowDefinitions.Add(new RowDefinition());
            var k = new TextBlock
            {
                Text = keys[i],
                Foreground = Gray,
                FontSize = 13,
                Margin = new Thickness(0, 8, 0, 8),
                VerticalAlignment = VerticalAlignment.Center
            };
            var v = new TextBlock
            {
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Dark,
                Margin = new Thickness(0, 4, 0, 8),
                VerticalAlignment = VerticalAlignment.Center
            };
            _monValues[i] = v;
            Grid.SetRow(k, i); Grid.SetColumn(k, 0);
            Grid.SetRow(v, i); Grid.SetColumn(v, 1);
            monGrid.Children.Add(k);
            monGrid.Children.Add(v);
        }
    }

    static string F(float? v, string fmt) => v.HasValue ? v.Value.ToString(fmt) : "—";

    async Task PollOnceAsync()
    {
        var d = await Task.Run(() => _sensors.Collect());
        if (!d.SensorsOk)
        {
            monStatus.Text = "无法读取传感器：请以管理员身份运行（双击弹窗点“是”）";
            monStatus.Foreground = Orange;
            for (int i = 0; i < _monValues.Length; i++) _monValues[i].Text = "—";
            return;
        }
        monStatus.Text = "实时监控中 ✓（每 2 秒刷新）";
        monStatus.Foreground = Green;

        float? cpuTemp = d.CpuTemp.HasValue ? d.CpuTemp.Value + Calibration.Offset : (float?)null;
        _monValues[0].Text = F(cpuTemp, "0.0") + " °C";
        _monValues[1].Text = F(d.CpuLoad, "0") + " %";
        _monValues[2].Text = F(d.CpuClock, "0") + " MHz";
        _monValues[3].Text = F(d.CpuPower, "0") + " W";
        _monValues[4].Text = F(d.GpuTemp, "0.0") + " °C";
        _monValues[5].Text = F(d.GpuLoad, "0") + " %";
        _monValues[6].Text = (d.GpuMemUsedMb.HasValue ? (d.GpuMemUsedMb.Value / 1024).ToString("0.0") : "—")
            + " / " + (d.GpuMemTotalMb.HasValue ? (d.GpuMemTotalMb.Value / 1024).ToString("0.0") : "—") + " GB";
        _monValues[7].Text = F(d.GpuPower, "0") + " W";
        _monValues[8].Text = (d.RamUsedGb.HasValue ? d.RamUsedGb.Value.ToString("0.0") : "—")
            + " / " + (d.RamTotalGb.HasValue ? d.RamTotalGb.Value.ToString("0.0") : "—") + " GB"
            + (d.RamTotalGb.HasValue && d.RamUsedGb.HasValue ? "（" + (d.RamUsedGb.Value * 100 / d.RamTotalGb.Value).ToString("0") + "%）" : "");
        _monValues[9].Text = d.FanRpm.HasValue ? d.FanRpm.Value.ToString("0") + " RPM" : "—（此主板无风扇传感器）";
        // FPS：基于 RTSS 共享内存（RTSS 需在运行）
        float? fps = await Task.Run(() => RtssReader.GetCurrentFps());
        _monValues[10].Text = fps.HasValue ? fps.Value.ToString("0") + " FPS" : "—（需 RTSS 运行）";

        // 共享给悬浮窗（悬浮窗只读展示，不重复采集）
        _latest = d;
        _latestFps = fps;
        if (_overlay != null)
        {
            _overlay.Latest = d;
            _overlay.LatestFps = fps;
        }

        SetTempColor(_monValues[0], cpuTemp);
        SetTempColor(_monValues[4], d.GpuTemp);
    }

    static void SetTempColor(TextBlock t, float? v)
    {
        if (!v.HasValue) { t.Foreground = Dark; return; }
        t.Foreground = v.Value >= 85 ? Red : v.Value >= 75 ? Orange : Green;
    }

    // ============ 基础体检 ============
    async Task RefreshReportAsync()
    {
        btnRefresh.IsEnabled = false;
        statusText.Text = "体检中…（读取本机信息，约 1 秒）";
        var info = await Task.Run(() => SysInfo.Collect());
        if (string.IsNullOrEmpty(info.CpuName) || info.RamTotalMb == 0)
        {
            await Task.Delay(500);
            info = await Task.Run(() => SysInfo.Collect());
        }
        _lastReport = Reporter.Build(info);
        reportText.Text = _lastReport;
        statusText.Text = "体检完成 ✓ 信息仅本机读取，不上传任何数据";
        btnRefresh.IsEnabled = true;
        // 初始化 CPU 温度校准（统一供主监控页与悬浮窗使用）
        CachedCpuName = info.CpuName;
        var cfg = MonitorConfig.Load();
        if (cfg.Calibrated) Calibration.SetManual(cfg.CpuTempOffset);
        else Calibration.AutoDetect(info.CpuName);
        // 记日志
        int warns = _lastReport.Count(c => c == '⚠');
        LogService.Append($"基础体检：CPU {info.CpuCores}核{info.CpuThreads}线程 · 内存占用 {info.RamPct}% · 磁盘 {info.Disks.Count} 个 · 警告 {warns} 项");
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshReportAsync();

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastReport)) { statusText.Text = "还没有报告，先点“重新体检”"; return; }
        Clipboard.SetText(_lastReport);
        statusText.Text = "已复制到剪贴板 ✓";
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastReport)) { statusText.Text = "还没有报告，先点“重新体检”"; return; }
        var dlg = new SaveFileDialog
        {
            Title = "保存体检报告",
            Filter = "文本文件 (*.txt)|*.txt",
            FileName = $"体检报告-{DateTime.Now:yyyyMMdd-HHmm}.txt"
        };
        if (dlg.ShowDialog(this) != true) return;
        File.WriteAllText(dlg.FileName, _lastReport, new UTF8Encoding(true));
        statusText.Text = $"已保存：{dlg.FileName} ✓";
    }

    // ============ 深度参数 ============
    void RefreshDeep()
    {
        deepStatus.Text = "采集中…";
        _lastDeep = DeepInfo.Build();
        deepText.Text = _lastDeep;
        deepStatus.Text = "深度参数采集完成 ✓";
        LogService.Append("深度参数已刷新");
    }

    void Deep_Click(object sender, RoutedEventArgs e) => RefreshDeep();

    // ============ 体检日志 ============
    void LogRefresh_Click(object sender, RoutedEventArgs e)
    {
        logText.Text = LogService.ReadAll();
        statusText.Text = "日志已刷新";
    }

    void LogOpen_Click(object sender, RoutedEventArgs e)
    {
        LogService.OpenFile();
        statusText.Text = "已打开日志文件";
    }

    void LogClear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "确定清除全部体检日志？（清除前会自动备份一份 .bak）", "清除日志", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        LogService.Clear();
        logText.Text = LogService.ReadAll();
        statusText.Text = "日志已清除（备份在日志目录）";
    }

    // ============ 实时监控：内置完整版悬浮窗 ============
    void Overlay_Click(object sender, RoutedEventArgs e)
    {
        if (_overlay == null)
        {
            _overlay = new MonitorOverlay();
            _overlay.Closed += (s, ev) => _overlay = null;
            _overlay.Latest = _latest;
            _overlay.LatestFps = _latestFps;
            _overlay.Show();
            monStatus.Text = "悬浮窗已开启（完整版：右键菜单/校准/截图热键）";
            monStatus.Foreground = Green;
        }
        else
        {
            _overlay.Activate();
            monStatus.Text = "悬浮窗已在运行";
        }
    }

    // ============ 实用官网 ============
    void BuildSites()
    {
        var groups = new (string Group, (string Name, string Url, string Desc)[] Items)[]
        {
            ("驱动下载", new (string, string, string)[]
            {
                ("NVIDIA 驱动下载", "https://www.nvidia.cn/drivers/", "N 卡官方驱动"),
                ("AMD 驱动下载", "https://www.amd.com/zh-cn/resources/support-articles/faqs/GPU-Driver-Autodetect.html", "A 卡官方驱动（自动检测工具）"),
                ("Intel 驱动与支持", "https://www.intel.cn/content/www/cn/zh/support.html", "Intel 官方驱动/支持"),
                ("微软官网", "https://www.microsoft.com/zh-cn/", "Windows/Office 官方下载"),
            }),
            ("检测与跑分", new (string, string, string)[]
            {
                ("图吧工具箱", "https://www.tbtool.cn/", "装机/验机工具箱合集（验机烤机一条龙）"),
                ("CPU-Z", "https://www.cpuid.com/softwares/cpu-z.html", "处理器专业检测"),
                ("GPU-Z", "https://www.techpowerup.com/gpuz/", "显卡专业检测"),
                ("HWiNFO", "https://www.hwinfo.com/", "传感器/温度专业监控"),
                ("AIDA64", "https://www.aida64.com/", "硬件检测与稳定性测试"),
                ("Cinebench", "https://www.maxon.net/cinebench", "CPU/GPU 跑分基准"),
                ("RTSS", "https://www.guru3d.com/files-details/rtss-rivatuner-statistics-server-download.html", "帧率统计/OSD（本工具 FPS 基于它）"),
                ("MSI Afterburner", "https://www.guru3d.com/files-details/msi-afterburner-beta-download.html", "显卡超频/监控，自带 RTSS"),
            }),
            ("装机软件", new (string, string, string)[]
            {
                ("7-Zip", "https://www.7-zip.org/", "开源解压工具"),
                ("Notepad++", "https://notepad-plus-plus.org/", "开源文本编辑器"),
                ("火绒安全", "https://www.huorong.cn/", "干净无广告的杀毒软件"),
                ("向日葵远程", "https://sunlogin.oray.com/", "免费远程控制"),
                ("Steam", "https://store.steampowered.com/", "游戏平台"),
                ("VirusTotal", "https://www.virustotal.com/", "文件在线查毒"),
            }),
            ("社区与资源", new (string, string, string)[]
            {
                ("GitHub", "https://github.com/", "开源代码托管"),
                ("哔哩哔哩", "https://www.bilibili.com/", "装机教程/硬件评测视频"),
            }),
        };

        foreach (var g in groups)
        {
            var head = new TextBlock
            {
                Text = "▍" + g.Group,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Dark,
                Margin = new Thickness(0, 14, 0, 8)
            };
            sitesPanel.Children.Add(head);
            foreach (var s in g.Items)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
                var btn = new Button
                {
                    Content = s.Name,
                    Width = 180,
                    Height = 34,
                    FontSize = 12.5,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                btn.Click += (sender, e) => OpenSite(s.Url);
                var desc = new TextBlock
                {
                    Text = s.Desc,
                    Foreground = Gray,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                row.Children.Add(btn);
                row.Children.Add(desc);
                sitesPanel.Children.Add(row);
            }
        }
        var note = new TextBlock
        {
            Text = "提示：点击按钮用默认浏览器打开；装机/装驱动/验机/查毒一站搞定。",
            Foreground = Gray,
            FontSize = 12,
            Margin = new Thickness(0, 14, 0, 0)
        };
        sitesPanel.Children.Add(note);
    }

    void OpenSite(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { statusText.Text = "打开链接失败"; }
    }
}

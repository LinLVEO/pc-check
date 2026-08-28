using System.IO;
using System.Text.Json;

namespace PcCheck;

/// <summary>悬浮窗配置（移植自硬件监测浮窗 AppConfig）：位置/字号/透明度/显示项/校准/截图设置，自动保存加载。</summary>
public class MonitorConfig
{
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public int FontSize { get; set; } = 13;
    public int OpacityPercent { get; set; } = 85;
    public bool Locked { get; set; }

    public bool ShowFps { get; set; } = true;
    public bool ShowCpuTemp { get; set; } = true;
    public bool ShowCpuLoad { get; set; } = true;
    public bool ShowGpuTemp { get; set; } = true;
    public bool ShowGpuLoad { get; set; } = true;
    public bool ShowGpuMem { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowFan { get; set; } = true;
    public bool ShowPower { get; set; } = true;
    public bool ShowPowerDetail { get; set; } = true;

    public double PowerBaseWatts { get; set; } = 45.0;

    public int CpuTempOffset { get; set; }
    public bool Calibrated { get; set; }

    public bool AutoStart { get; set; }

    public string ShotHotkey { get; set; } = "Ctrl+Alt+S";
    public string ShotFormat { get; set; } = "png";
    public bool ShotHideSelf { get; set; } = true;
    public int ShotJpegQuality { get; set; } = 90;
    public string ShotDir { get; set; } = "";

    static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "config.json");

    public static MonitorConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<MonitorConfig>(File.ReadAllText(ConfigPath));
                if (cfg != null) return cfg;
            }
        }
        catch { /* 配置损坏用默认值 */ }
        return new MonitorConfig();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

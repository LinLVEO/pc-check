using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace PcCheck;

/// <summary>深度参数采集（全缓存实测、代号工艺推断、内存条、显卡规格库、驱动、主板/BIOS）——全内置。</summary>
public static class DeepInfo
{
    // IsProcessorFeaturePresent 常量（winnt.h）
    const int PF_SSE = 6, PF_SSE2 = 7, PF_SSE3 = 13, PF_SSSE3 = 36, PF_SSE41 = 37, PF_SSE42 = 38,
              PF_AVX = 39, PF_AVX2 = 40, PF_FMA3 = 42, PF_AES = 43, PF_RDRAND = 44;

    [DllImport("kernel32.dll")]
    static extern bool IsProcessorFeaturePresent(int feature);

    public static string Build()
    {
        var sb = new StringBuilder();
        sb.AppendLine("════════ 深度参数 ════════");
        sb.AppendLine($"采集时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        // ---------- 处理器 ----------
        string cpuName = "";
        int cores = 0, threads = 0;
        double maxGhz = 0;
        long extClock = 0;
        string socket = "";
        try
        {
            using var mos = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (var o in mos.Get())
            {
                cpuName = o["Name"]?.ToString()?.Trim() ?? "";
                cores = Convert.ToInt32(o["NumberOfCores"]);
                threads = Convert.ToInt32(o["NumberOfLogicalProcessors"]);
                if (o["MaxClockSpeed"] != null) maxGhz = Math.Round(Convert.ToDouble(o["MaxClockSpeed"]) / 1000.0, 2);
                long.TryParse(o["ExtClock"]?.ToString(), out extClock);
                socket = o["SocketDesignation"]?.ToString()?.Trim() ?? "";
                break;
            }
        }
        catch { }

        sb.AppendLine("【处理器 CPU】");
        sb.AppendLine($"  型号：{cpuName}");
        sb.AppendLine($"  核心 / 线程：{cores} 核 / {threads} 线程");
        if (maxGhz > 0)
            sb.AppendLine($"  主频：{maxGhz:0.00} GHz" + (extClock > 0 ? $"（外频 {extClock} MHz × 倍频 {Math.Round(maxGhz * 1000 / extClock):0}）" : ""));
        var (code, proc) = CpuSpecs.Infer(cpuName);
        sb.AppendLine($"  代号 / 工艺：{code} · {proc}（按型号推断）");
        sb.AppendLine($"  插槽：{socket}");

        var caches = CpuSpecs.ReadCaches();
        if (caches.Count > 0)
        {
            sb.AppendLine("  缓存（系统实测）:");
            foreach (var g in caches.GroupBy(c => (c.Level, c.Type)).OrderBy(g => g.Key.Level).ThenBy(g => g.Key.Type))
            {
                var c = g.First();
                string type = c.Type == 1 ? "指令" : c.Type == 2 ? "数据" : "统一";
                string assoc = c.Associativity == 0xFF ? "全关联" : c.Associativity.ToString();
                sb.AppendLine($"    L{c.Level} {type}：{c.SizeKb} KB/核 · {assoc} 路 · 行 {c.LineSize} B");
            }
        }

        var isa = new List<string>();
        if (Environment.Is64BitProcess) { isa.Add("SSE"); isa.Add("SSE2"); }
        else
        {
            if (IsProcessorFeaturePresent(PF_SSE)) isa.Add("SSE");
            if (IsProcessorFeaturePresent(PF_SSE2)) isa.Add("SSE2");
        }
        if (IsProcessorFeaturePresent(PF_SSE3)) isa.Add("SSE3");
        if (IsProcessorFeaturePresent(PF_SSSE3)) isa.Add("SSSE3");
        if (IsProcessorFeaturePresent(PF_SSE41)) isa.Add("SSE4.1");
        if (IsProcessorFeaturePresent(PF_SSE42)) isa.Add("SSE4.2");
        if (IsProcessorFeaturePresent(PF_AVX)) isa.Add("AVX");
        if (IsProcessorFeaturePresent(PF_AVX2)) isa.Add("AVX2");
        if (IsProcessorFeaturePresent(PF_FMA3)) isa.Add("FMA3");
        if (IsProcessorFeaturePresent(PF_AES)) isa.Add("AES-NI");
        if (IsProcessorFeaturePresent(PF_RDRAND)) isa.Add("RDRAND");
        sb.AppendLine($"  指令集：{(isa.Count > 0 ? string.Join(" / ", isa) : "读取失败")}");
        sb.AppendLine();

        // ---------- 内存条 ----------
        sb.AppendLine("【内存 Memory】");
        int sticks = 0;
        try
        {
            using var mos = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
            foreach (var o in mos.Get())
            {
                sticks++;
                double gb = o["Capacity"] != null ? Math.Round(Convert.ToDouble(o["Capacity"]) / 1024 / 1024 / 1024, 0) : 0;
                string speed = o["Speed"]?.ToString() ?? "?";
                string ddr = "";
                if (int.TryParse(speed, out int s))
                    ddr = s >= 2133 ? "DDR4" : s >= 1600 ? "DDR3" : s >= 1066 ? "DDR3" : "DDR2";
                sb.AppendLine($"  插槽 {o["DeviceLocator"] ?? "?"}：{gb:0} GB · {speed} MHz（{ddr}）· {o["Manufacturer"]?.ToString()?.Trim() ?? "?"} · {o["PartNumber"]?.ToString()?.Trim() ?? "?"}");
            }
            if (sticks == 0) sb.AppendLine("  未读取到内存条");
        }
        catch { sb.AppendLine("  读取失败"); }
        sb.AppendLine("  （时序 CL-tRCD-tRP 等需读 SPD，系统接口未开放）");
        sb.AppendLine();

        // ---------- 显卡 ----------
        sb.AppendLine("【显卡 GPU】");
        try
        {
            using var mos = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (var o in mos.Get())
            {
                string n = o["Name"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(n) || n.Contains("Basic Display")) continue;
                double ramGb = 0;
                if (o["AdapterRAM"] != null)
                {
                    try { ramGb = Math.Round(Convert.ToDouble(o["AdapterRAM"]) / 1024 / 1024 / 1024, 1); }
                    catch { }
                }
                string drvDate = o["DriverDate"]?.ToString() ?? "";
                string dh = drvDate.Length >= 8 ? drvDate.Substring(0, 8) : drvDate;
                if (dh.Length == 8 && dh.All(char.IsDigit))
                    drvDate = $"{dh.Substring(0, 4)}-{dh.Substring(4, 2)}-{dh.Substring(6, 2)}";
                sb.AppendLine($"  型号：{n}");
                sb.AppendLine($"  规格（内置库）：{GpuSpecs.Lookup(n)}");
                sb.AppendLine($"  显存：{(ramGb > 0 ? ramGb.ToString("0.0") + " GB（驱动报告值）" : "读取失败")}");
                sb.AppendLine($"  驱动版本：{o["DriverVersion"] ?? "?"}");
                sb.AppendLine($"  驱动日期：{drvDate}");
                sb.AppendLine($"  当前分辨率：{o["CurrentHorizontalResolution"]} × {o["CurrentVerticalResolution"]} @ {o["CurrentRefreshRate"]} Hz");
                break;
            }
        }
        catch { sb.AppendLine("  读取失败"); }
        sb.AppendLine();

        // ---------- 主板 / BIOS ----------
        sb.AppendLine("【主板 / BIOS】");
        try
        {
            using var mos = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard");
            foreach (var o in mos.Get())
            {
                sb.AppendLine($"  主板：{o["Manufacturer"]?.ToString()?.Trim()} {o["Product"]?.ToString()?.Trim()}".Trim());
                break;
            }
        }
        catch { sb.AppendLine("  读取失败"); }
        try
        {
            using var mos = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
            foreach (var o in mos.Get())
            {
                string date = o["ReleaseDate"]?.ToString() ?? "";
                string head = date.Length >= 8 ? date.Substring(0, 8) : date;
                if (head.Length == 8 && head.All(char.IsDigit))
                    date = $"{head.Substring(0, 4)}-{head.Substring(4, 2)}-{head.Substring(6, 2)}";
                sb.AppendLine($"  BIOS：{o["SMBIOSBIOSVersion"]?.ToString()?.Trim()}（{date}）");
                break;
            }
        }
        catch { sb.AppendLine("  读取失败"); }
        sb.AppendLine();

        sb.AppendLine("（代号/工艺/显卡规格按型号推断；位宽带宽等为公开规格，供参考）");
        return sb.ToString();
    }
}

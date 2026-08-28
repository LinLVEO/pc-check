using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace PcCheck;

/// <summary>一次体检采集到的全部信息。</summary>
public class PcInfo
{
    public string CpuName = "";
    public int CpuCores;
    public int CpuThreads;
    public double CpuClockGhz;
    public bool Avx2;
    public string GpuName = "";
    public string Motherboard = "";
    public string Bios = "";
    public string OsCaption = "";
    public string OsVersion = "";
    public string SystemDrive = "C:";
    public ulong RamTotalMb;
    public ulong RamFreeMb;
    public List<DiskInfo> Disks = new();

    public string RamTotalGb => (RamTotalMb / 1024.0).ToString("0.0");
    public string RamUsedGb => ((RamTotalMb - RamFreeMb) / 1024.0).ToString("0.0");
    public string RamPct => RamTotalMb == 0 ? "?" : ((RamTotalMb - RamFreeMb) * 100.0 / RamTotalMb).ToString("0");
}

/// <summary>一个本地磁盘分区。</summary>
public class DiskInfo
{
    public string Name = "";
    public string FileSystem = "";
    public ulong TotalMb;
    public ulong FreeMb;

    public string TotalGb => (TotalMb / 1024.0).ToString("0.0");
    public string FreeGb => (FreeMb / 1024.0).ToString("0.0");
    public int PctFree => TotalMb == 0 ? 0 : (int)(FreeMb * 100.0 / TotalMb);
}

/// <summary>信息采集：全部走 Windows 标准接口，失败单项跳过，绝不拖垮整个体检。</summary>
public static class SysInfo
{
    // IsProcessorFeaturePresent 常量：39=AVX，40=AVX2（写错用 39 会误判支持 AVX2）
    const int PF_AVX2_INSTRUCTIONS_AVAILABLE = 40;

    [DllImport("kernel32.dll")]
    static extern bool IsProcessorFeaturePresent(int feature);

    public static PcInfo Collect()
    {
        var info = new PcInfo();

        try
        {
            using var mos = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (var o in mos.Get())
            {
                info.CpuName = Str(o, "Name");
                info.CpuCores = Int(o, "NumberOfCores");
                info.CpuThreads = Int(o, "NumberOfLogicalProcessors");
                var mhz = (o["MaxClockSpeed"] is null) ? 0.0 : Convert.ToDouble(o["MaxClockSpeed"]);
                info.CpuClockGhz = mhz / 1000.0;
                break;
            }
        }
        catch { /* 单项失败不致命 */ }

        try { info.Avx2 = IsProcessorFeaturePresent(PF_AVX2_INSTRUCTIONS_AVAILABLE); }
        catch { info.Avx2 = false; }

        try { info.SystemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:"; }
        catch { info.SystemDrive = "C:"; }

        try
        {
            using var mos = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (var o in mos.Get())
            {
                string n = Str(o, "Name");
                if (!string.IsNullOrEmpty(n) && !n.Contains("Basic Display"))
                {
                    info.GpuName = n;
                    break;
                }
            }
        }
        catch { }

        try
        {
            using var mos = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard");
            foreach (var o in mos.Get())
            {
                info.Motherboard = $"{Str(o, "Manufacturer")} {Str(o, "Product")}".Trim();
                break;
            }
        }
        catch { }

        try
        {
            using var mos = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
            foreach (var o in mos.Get())
            {
                // 原始日期形如 20230627000000.000000+000，只取前 8 位转成 2023-06-27
                string date = Str(o, "ReleaseDate");
                string head = date.Length >= 8 ? date.Substring(0, 8) : date;
                if (head.Length == 8 && head.All(char.IsDigit))
                    date = $"{head.Substring(0, 4)}-{head.Substring(4, 2)}-{head.Substring(6, 2)}";
                info.Bios = $"{Str(o, "SMBIOSBIOSVersion")}（{date}）".Trim();
                break;
            }
        }
        catch { }

        try
        {
            using var mos = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
            foreach (var o in mos.Get())
            {
                info.OsCaption = Str(o, "Caption");
                info.OsVersion = Str(o, "Version");
                info.RamTotalMb = (ulong)(Convert.ToUInt64(o["TotalVisibleMemorySize"]) / 1024); // KB -> MB
                info.RamFreeMb = (ulong)(Convert.ToUInt64(o["FreePhysicalMemory"]) / 1024);
                break;
            }
        }
        catch { }

        try
        {
            using var mos = new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDisk WHERE DriveType=3");
            foreach (var o in mos.Get())
            {
                info.Disks.Add(new DiskInfo
                {
                    Name = Str(o, "DeviceID"),
                    FileSystem = Str(o, "FileSystem"),
                    TotalMb = (ulong)(Convert.ToUInt64(o["Size"]) / 1024 / 1024),
                    FreeMb = (ulong)(Convert.ToUInt64(o["FreeSpace"]) / 1024 / 1024)
                });
            }
        }
        catch { }

        return info;
    }

    static string Str(ManagementBaseObject o, string p) => o[p]?.ToString()?.Trim() ?? "";
    static int Int(ManagementBaseObject o, string p) { try { return Convert.ToInt32(o[p]); } catch { return 0; } }
}

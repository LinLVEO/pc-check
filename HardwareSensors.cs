using System.Management;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace PcCheck;

/// <summary>一次实时采样（直接读本机传感器，无需任何外部程序）。</summary>
public class LiveData
{
    public bool SensorsOk;                       // 传感器是否可用（需管理员权限）
    public float? CpuTemp, CpuLoad, CpuClock, CpuPower;   // 温度已含校准偏移
    public float? CpuVcore;                      // CPU 核心电压（V）
    public float? GpuTemp, GpuLoad, GpuMemUsedMb, GpuMemTotalMb, GpuPower;
    public float? GpuCoreClock, GpuMemClock;     // GPU 核心/显存频率（MHz）
    public float? RamUsedGb, RamTotalGb;
    public float? FanRpm;
}

/// <summary>遍历硬件树刷新数据（LibreHardwareMonitor 要求）。</summary>
public class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);
    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware) sub.Accept(this);
    }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}

/// <summary>内置传感器读取（逻辑与硬件监测浮窗同源，需管理员权限，已由 manifest 申请）。</summary>
public sealed class HardwareSensors : IDisposable
{
    readonly Computer _computer;
    readonly UpdateVisitor _visitor = new();

    public bool SensorsAvailable { get; }

    public HardwareSensors()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true
        };
        try { _computer.Open(); SensorsAvailable = true; }
        catch { SensorsAvailable = false; }
    }

    public LiveData Collect()
    {
        var d = new LiveData { SensorsOk = SensorsAvailable };
        if (!SensorsAvailable) return d;
        try { _computer.Accept(_visitor); } catch { }
        try
        {
            foreach (var hw in _computer.Hardware)
                VisitAll(hw, (h, s) =>
                {
                    switch (h.HardwareType)
                    {
                        case HardwareType.Cpu:
                            if (s.SensorType == SensorType.Temperature && (s.Name.Contains("Package") || s.Name.Contains("TjMax") || s.Name.Contains("Core")))
                                d.CpuTemp = Max(d.CpuTemp, s.Value); // 原始温度，校准偏移由 Calibration 统一处理
                            else if (s.SensorType == SensorType.Load && s.Name.Contains("Total"))
                                d.CpuLoad = Max(d.CpuLoad, s.Value);
                            else if (s.SensorType == SensorType.Clock && s.Name.Contains("Core") && !s.Name.Contains("Bus"))
                                d.CpuClock = Max(d.CpuClock, s.Value);
                            else if (s.SensorType == SensorType.Power && s.Name.Contains("Package"))
                                d.CpuPower = s.Value;
                            else if (s.SensorType == SensorType.Voltage && (s.Name.Contains("Vcore") || s.Name.Contains("VCore") || s.Name.Contains("Core Voltage") || s.Name.Contains("CPU Core")))
                                d.CpuVcore = Max(d.CpuVcore, s.Value);
                            break;

                        case HardwareType.GpuAmd:
                        case HardwareType.GpuNvidia:
                        case HardwareType.GpuIntel:
                            if (s.SensorType == SensorType.Temperature && s.Name.Contains("GPU Core"))
                                d.GpuTemp = Max(d.GpuTemp, s.Value);
                            else if (s.SensorType == SensorType.Load && s.Name.Contains("GPU Core"))
                                d.GpuLoad = Max(d.GpuLoad, s.Value);
                            else if (s.Name.Contains("Memory Used")) // 兼容 "Dedicated Memory Used" 等命名
                                d.GpuMemUsedMb = Max(d.GpuMemUsedMb, s.Value);
                            else if (s.Name.Contains("Memory Total")) // 兼容 "GPU Memory Total" / "Dedicated Memory Total"
                                d.GpuMemTotalMb = Max(d.GpuMemTotalMb, s.Value);
                            else if (s.SensorType == SensorType.Power)
                                d.GpuPower = Max(d.GpuPower, s.Value);
                            else if (s.SensorType == SensorType.Clock)
                            {
                                if (s.Name.Contains("Core")) d.GpuCoreClock = Max(d.GpuCoreClock, s.Value);
                                else if (s.Name.Contains("Memory")) d.GpuMemClock = Max(d.GpuMemClock, s.Value);
                            }
                            break;

                        case HardwareType.Motherboard:
                            if (s.SensorType == SensorType.Fan)
                            {
                                if (s.Name.Contains("CPU")) d.FanRpm = Max(d.FanRpm, s.Value);
                                else if (d.FanRpm == null) d.FanRpm = s.Value;
                            }
                            break;
                    }
                });
        }
        catch { }

        // 显存总量兜底：传感器没读到就用 WMI 驱动报告值（字节 → MB，如 RX 560 = 4.0GB）
        if (!d.GpuMemTotalMb.HasValue)
        {
            try
            {
                using var mos = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                foreach (var o in mos.Get())
                {
                    string n = o["Name"]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(n) || n.Contains("Basic Display")) continue;
                    if (o["AdapterRAM"] != null)
                    {
                        double b = Convert.ToDouble(o["AdapterRAM"]);
                        if (b > 0) { d.GpuMemTotalMb = (float)(b / 1024 / 1024); break; }
                    }
                }
            }
            catch { }
        }

        var (total, avail) = SystemMemory.PhysicalGB();
        d.RamTotalGb = total;
        if (total > 0) d.RamUsedGb = total - avail;
        return d;
    }

    public void Dispose()
    {
        try { _computer.Close(); } catch { }
    }

    static float? Max(float? a, float? b)
    {
        if (a == null) return b;
        if (b == null) return a;
        return Math.Max(a.Value, b.Value);
    }

    static void VisitAll(IHardware hw, Action<IHardware, ISensor> action)
    {
        foreach (var s in hw.Sensors) action(hw, s);
        foreach (var sub in hw.SubHardware) VisitAll(sub, action);
    }
}

/// <summary>物理内存总量/可用（系统 API，无需驱动）。</summary>
public static class SystemMemory
{
    [StructLayout(LayoutKind.Sequential)]
    class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    public static (float totalGB, float availGB) PhysicalGB()
    {
        var m = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(m))
            return (m.ullTotalPhys / 1024f / 1024f / 1024f, m.ullAvailPhys / 1024f / 1024f / 1024f);
        return (0, 0);
    }
}

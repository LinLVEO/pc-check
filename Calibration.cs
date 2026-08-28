using System.Text.RegularExpressions;

namespace PcCheck;

/// <summary>CPU 温度校准（移植自浮窗逻辑）：Xeon E5 v1 的 TjMax 实为 91°C，LHM 按 102°C 算偏高约 11°C。
/// 主监控页与悬浮窗统一从这里取偏移，避免重复叠加。</summary>
public static class Calibration
{
    public static float Offset { get; private set; }
    public static bool AutoDone { get; private set; }

    /// <summary>按 CPU 型号自动检测偏移：E5 v1 → -11，其余 → 0。</summary>
    public static void AutoDetect(string cpuName)
    {
        try
        {
            if (!string.IsNullOrEmpty(cpuName)
                && cpuName.Contains("Xeon")
                && Regex.IsMatch(cpuName, @"E5-\d{4}")
                && !cpuName.Contains(" v2") && !cpuName.Contains(" v3") && !cpuName.Contains(" v4"))
                Offset = -11;
            else
                Offset = 0;
        }
        catch { Offset = 0; }
        AutoDone = true;
    }

    public static void SetManual(float offset)
    {
        Offset = offset;
        AutoDone = true;
    }
}

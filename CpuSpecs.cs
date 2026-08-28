using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PcCheck;

/// <summary>CPU 深度规格：缓存（系统 API 实测）+ 代号/工艺（按型号推断）。</summary>
public static class CpuSpecs
{
    [StructLayout(LayoutKind.Sequential)]
    struct SLPI
    {
        public UIntPtr ProcessorMask;
        public int Relationship; // 0=Core, 1=Numa, 2=Cache, 3=Package
        public uint _pad;        // union 8 字节对齐（偏移 16）
        public byte Cache_Level;
        public byte Cache_Associativity;
        public ushort Cache_LineSize;
        public uint Cache_Size;
        public uint Cache_Type;  // 1=指令 2=数据 3=统一
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetLogicalProcessorInformation(IntPtr buffer, ref int returnedLength);

    public class CacheInfo
    {
        public int Level;
        public int Type;
        public int SizeKb;
        public int Associativity;
        public int LineSize;
    }

    /// <summary>读取本机 CPU 缓存（每核容量），失败返回空列表。</summary>
    public static List<CacheInfo> ReadCaches()
    {
        var result = new List<CacheInfo>();
        try
        {
            int len = 0;
            GetLogicalProcessorInformation(IntPtr.Zero, ref len);
            IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                if (!GetLogicalProcessorInformation(buf, ref len)) return result;
                int size = Marshal.SizeOf<SLPI>();
                int n = len / size;
                for (int i = 0; i < n; i++)
                {
                    var si = Marshal.PtrToStructure<SLPI>(buf + i * size);
                    if (si.Relationship != 2) continue;
                    result.Add(new CacheInfo
                    {
                        Level = si.Cache_Level,
                        Type = (int)si.Cache_Type,
                        SizeKb = (int)(si.Cache_Size / 1024),
                        Associativity = si.Cache_Associativity,
                        LineSize = si.Cache_LineSize
                    });
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { }
        return result;
    }

    /// <summary>按型号推断代号与工艺（标注"按型号推断"）。</summary>
    public static (string CodeName, string Process) Infer(string name)
    {
        if (string.IsNullOrEmpty(name)) return ("型号库未收录", "?");
        // 型号名形如 "Intel(R) Xeon(R) CPU E5-2680 0 @ 2.70GHz"
        if (name.Contains("Xeon") && Regex.IsMatch(name, @"E5-\d{4}"))
        {
            if (name.Contains(" v2")) return ("Ivy Bridge-EP", "22 nm");
            if (name.Contains(" v3")) return ("Haswell-EP", "22 nm");
            if (name.Contains(" v4")) return ("Broadwell-EP", "14 nm");
            return ("Sandy Bridge-EP", "32 nm"); // v1 或未标注
        }
        if (name.Contains("Xeon E3")) return ("Haswell 世代（服务器）", "22 nm");
        var m = Regex.Match(name, @"i[357]-(\d{4})");
        if (m.Success)
        {
            string g = m.Groups[1].Value;
            if (g.Length < 4) return ("型号库未收录", "?");
            char gen = g[0];
            return gen switch
            {
                '1' when g[1] == '2' => ("Alder Lake", "Intel 7（10nm ESF）"),
                '1' => ("Rocket Lake", "14 nm"),
                '0' => ("Comet Lake", "14 nm"),
                '9' or '8' => ("Coffee Lake", "14 nm"),
                '7' => ("Kaby Lake", "14 nm"),
                '6' => ("Skylake", "14 nm"),
                '5' => ("Broadwell", "14 nm"),
                '4' => ("Haswell", "22 nm"),
                '3' => ("Ivy Bridge", "22 nm"),
                '2' => ("Sandy Bridge", "32 nm"),
                _ => ("型号库未收录", "?")
            };
        }
        var ry = Regex.Match(name, @"Ryzen\s*\d");
        if (ry.Success)
        {
            var rm = Regex.Match(name, @"Ryzen\s*(\d)");
            if (rm.Success)
            {
                return rm.Groups[1].Value switch
                {
                    "9" => ("Zen 5", "4 nm"),
                    "8" => ("Zen 4", "5 nm"),
                    "7" => ("Zen 4", "5 nm"),
                    "5" => ("Zen 3", "7 nm"),
                    "3" => ("Zen 2", "7 nm"),
                    _ => ("Zen", "7 nm")
                };
            }
        }
        if (name.Contains("Pentium") || name.Contains("Celeron")) return ("入门级", "14 nm 或更新");
        return ("型号库未收录", "?");
    }
}

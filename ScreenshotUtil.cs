using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace PcCheck;

/// <summary>截图工具 + 全局热键（移植自硬件监测浮窗 Screenshot.cs/Hotkey，适配 WPF）。</summary>
public static class ScreenshotUtil
{
    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);

    /// <summary>捕获所有显示器的整个虚拟屏幕。</summary>
    public static Bitmap CaptureVirtualScreen()
    {
        int x = GetSystemMetrics(76), y = GetSystemMetrics(77);
        int w = GetSystemMetrics(78), h = GetSystemMetrics(79);
        if (w <= 0 || h <= 0) { w = 1920; h = 1080; }
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public static string Save(Bitmap bmp, MonitorConfig cfg)
    {
        var dir = string.IsNullOrWhiteSpace(cfg.ShotDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "截图")
            : cfg.ShotDir;
        Directory.CreateDirectory(dir);
        var ext = NormalizeExt(cfg.ShotFormat);
        var path = Path.Combine(dir, $"截图-{DateTime.Now:yyyyMMdd-HHmmss}.{ext}");
        switch (ext)
        {
            case "jpg": SaveJpeg(bmp, path, cfg.ShotJpegQuality); break;
            case "bmp": bmp.Save(path, ImageFormat.Bmp); break;
            default: bmp.Save(path, ImageFormat.Png); break;
        }
        return path;
    }

    public static string NormalizeExt(string fmt) => (fmt ?? "png").Trim().ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => "jpg",
        "bmp" => "bmp",
        _ => "png"
    };

    static void SaveJpeg(Bitmap bmp, string path, int quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(quality, 1, 100));
        bmp.Save(path, codec, ep);
    }
}

/// <summary>全局热键（RegisterHotKey），供悬浮窗截图使用。</summary>
public static class HotkeyUtil
{
    public const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    public const int HotkeyId = 0x484D;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static (uint mods, uint vk)? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        uint mods = 0;
        string key = "";
        foreach (var p in parts)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win": mods |= MOD_WIN; break;
                default: key = p; break;
            }
        }
        if (key.Length == 0 || mods == 0) return null;
        uint vk = KeyToVk(key);
        if (vk == 0) return null;
        return (mods, vk);
    }

    static uint KeyToVk(string key)
    {
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z') return (uint)c;
            if (c is >= '0' and <= '9') return (uint)c;
            return 0;
        }
        if (key.StartsWith("F", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key[1..], out int n) && n is >= 1 and <= 24)
            return (uint)(0x70 + n - 1);
        return 0;
    }

    public static bool Register(IntPtr hWnd, uint mods, uint vk)
        => RegisterHotKey(hWnd, HotkeyId, mods | MOD_NOREPEAT, vk);

    public static void Unregister(IntPtr hWnd)
        => UnregisterHotKey(hWnd, HotkeyId);

    public static bool IsHotkeyMessage(int msg, IntPtr wParam)
        => msg == WM_HOTKEY && (int)wParam == HotkeyId;
}

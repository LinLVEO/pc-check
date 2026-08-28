using System.IO;
using System.Text;

namespace PcCheck;

/// <summary>体检日志：记录每次体检/操作历史，存本机（%LOCALAPPDATA%\PcCheck\），纯本地。</summary>
public static class LogService
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcCheck");

    public static string LogPath => Path.Combine(Dir, "体检日志.txt");

    public static void Append(string content)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm}] {content}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { }
    }

    public static string ReadAll()
    {
        try
        {
            if (!File.Exists(LogPath)) return "（暂无日志，体检后会记录）";
            string t = File.ReadAllText(LogPath, Encoding.UTF8).TrimEnd('\r', '\n');
            return string.IsNullOrEmpty(t) ? "（日志为空）" : t;
        }
        catch { return "（读取失败）"; }
    }

    /// <summary>清空日志：清空前自动备份一份 .bak，可找回。</summary>
    public static void Clear()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (File.Exists(LogPath))
            {
                string bak = Path.Combine(Dir, $"体检日志-{DateTime.Now:yyyyMMdd-HHmmss}.bak");
                File.Copy(LogPath, bak, true);
            }
            File.WriteAllText(LogPath, "", Encoding.UTF8);
        }
        catch { }
    }

    public static void OpenFile()
    {
        try
        {
            if (!File.Exists(LogPath)) Append("日志文件已创建");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LogPath) { UseShellExecute = true });
        }
        catch { }
    }
}

using System.Text;

namespace PcCheck;

/// <summary>把采集结果转成「小白能看懂」的体检报告。</summary>
public static class Reporter
{
    public static string Build(PcInfo i)
    {
        var sb = new StringBuilder();
        var warns = new List<string>();
        var goods = new List<string>();

        sb.AppendLine("════════ 小白电脑体检报告 ════════");
        sb.AppendLine($"体检时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        // ---- 处理器 ----
        sb.AppendLine("【处理器】");
        sb.AppendLine($"  型号：{i.CpuName}");
        sb.AppendLine($"  核心 / 线程：{i.CpuCores} 核 {i.CpuThreads} 线程（主频约 {i.CpuClockGhz:0.00} GHz）");
        sb.AppendLine();

        // ---- AI 能力（AVX2）----
        sb.AppendLine("【AI 能力检测（AVX2 指令集）】");
        if (i.Avx2)
        {
            sb.AppendLine("  ✅ 支持 AVX2：能正常跑 Ollama / LM Studio 等现代 AI 工具。");
            goods.Add("支持 AVX2，可跑现代 AI 工具");
        }
        else
        {
            sb.AppendLine("  ❌ 不支持 AVX2：跑不了 Ollama / LM Studio 等新 AI 工具（老 CPU 通病）。");
            sb.AppendLine("     本地 AI 只能用兼容老指令集的版本（如 koboldcpp 的 oldpc 版）。");
            warns.Add("CPU 无 AVX2，新 AI 工具装不了（老 CPU 正常现象，不影响日常用）");
        }
        sb.AppendLine();

        // ---- 内存 ----
        sb.AppendLine("【内存】");
        sb.AppendLine($"  总量 {i.RamTotalGb} GB ｜ 已用 {i.RamUsedGb} GB（{i.RamPct}%）");
        if (i.RamTotalMb == 0)
        {
            sb.AppendLine("  内存信息读取失败，稍后重试。");
        }
        else
        {
            double pct = (i.RamTotalMb - i.RamFreeMb) * 100.0 / i.RamTotalMb;
            if (pct >= 90) { sb.AppendLine("  🔴 内存占用超过 90%，电脑会明显卡顿，建议关掉不用的程序或加内存。"); warns.Add("内存占用过高（>90%），建议关程序或加内存"); }
            else if (pct >= 80) { sb.AppendLine("  🟡 内存占用接近 80%，多开程序时可能变慢，留意一下。"); warns.Add("内存占用偏高（80%~90%）"); }
            else { sb.AppendLine("  ✅ 内存占用正常。"); goods.Add("内存占用正常"); }
        }
        sb.AppendLine();

        // ---- 显卡 ----
        sb.AppendLine("【显卡】");
        sb.AppendLine(string.IsNullOrEmpty(i.GpuName) ? "  未读取到（可能驱动异常，建议装官方驱动）" : $"  {i.GpuName}");
        sb.AppendLine();

        // ---- 主板 / BIOS ----
        sb.AppendLine("【主板 / BIOS】");
        sb.AppendLine($"  主板：{i.Motherboard}");
        sb.AppendLine($"  BIOS：{i.Bios}");
        sb.AppendLine();

        // ---- 磁盘 ----
        sb.AppendLine("【磁盘】");
        if (i.Disks.Count == 0)
        {
            sb.AppendLine("  未读取到磁盘信息。");
        }
        else
        {
            string sysLetter = i.SystemDrive.TrimEnd('\\', ':');
            foreach (var d in i.Disks)
            {
                bool isSystem = string.Equals(d.Name.TrimEnd('\\', ':'), sysLetter, StringComparison.OrdinalIgnoreCase);
                sb.AppendLine($"  {d.Name}（{d.FileSystem}）：共 {d.TotalGb} GB ｜ 剩余 {d.FreeGb} GB（{d.PctFree}%）");
                if (isSystem)
                {
                    if (d.PctFree < 10) { sb.AppendLine($"     🔴 系统盘剩余不足 10%！会拖慢整台电脑，赶紧清理或搬走大文件。"); warns.Add($"系统盘 {d.Name} 剩余不足 10%，需立即清理"); }
                    else if (d.PctFree < 20) { sb.AppendLine($"     🟡 系统盘剩余不足 20%，建议清理，留 20% 以上才不卡。"); warns.Add($"系统盘 {d.Name} 剩余不足 20%，建议清理"); }
                    else { goods.Add($"系统盘 {d.Name} 空间充足"); }
                }
                else if (d.PctFree < 10) { sb.AppendLine("     🟡 剩余空间不足 10%，快满了，注意别存太满。"); warns.Add($"{d.Name} 剩余不足 10%"); }
            }
        }
        sb.AppendLine();

        // ---- 系统 ----
        sb.AppendLine("【操作系统】");
        sb.AppendLine($"  {i.OsCaption}（版本 {i.OsVersion}）");
        sb.AppendLine();

        // ---- 小结 ----
        sb.AppendLine("════════ 健康小结 ════════");
        if (warns.Count == 0 && goods.Count > 0)
        {
            sb.AppendLine("  🟢 各项指标正常，电脑状态健康，放心用！");
        }
        foreach (var g in goods) sb.AppendLine($"  ✅ {g}");
        foreach (var w in warns) sb.AppendLine($"  ⚠️ {w}");
        if (warns.Count == 0 && goods.Count == 0) sb.AppendLine("  体检数据读取不全，建议稍后重试。");

        sb.AppendLine();
        sb.AppendLine("（本报告由开源工具“小白电脑体检”生成，仅读本机信息，不上传任何数据）");
        return sb.ToString();
    }
}

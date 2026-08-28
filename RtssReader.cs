using System.IO.MemoryMappedFiles;

namespace PcCheck;

/// <summary>读取 RTSS（RivaTuner Statistics Server，MSI Afterburner 组件）共享内存获取帧率。
/// 帧率检测基于 RTSS：需 RTSS 在运行且游戏被钩住（商业游戏/烤鸡工具默认被钩）。</summary>
public static class RtssReader
{
    const string MapName = "RTSSSharedMemoryV2";
    const uint RtssSignature = 0x52545353; // 'RTSS'
    const int OffFlags = 264;      // dwFlags（低 16 位=API 类型）
    const int OffTime0 = 268;
    const int OffTime1 = 272;
    const int OffFrames = 276;
    const int OffFrameTime = 280;  // dwFrameTime（微秒）
    const uint ApiUsageMask = 0x0000FFFF;

    /// <summary>当前被 RTSS 统计的最活跃应用帧率；RTSS 未运行或无游戏时返回 null。</summary>
    public static float? GetCurrentFps()
    {
        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            using var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            if (view.ReadUInt32(0) != RtssSignature) return null;
            uint entrySize = view.ReadUInt32(8);
            uint arrOffset = view.ReadUInt32(12);
            uint arrSize = view.ReadUInt32(16);
            if (entrySize < 400 || arrOffset == 0) return null;

            uint lastFg = view.ReadUInt32(64); // v2.16+ 前台应用条目
            float? fromFg = lastFg < arrSize ? ReadEntryFps(view, arrOffset + lastFg * entrySize) : null;
            if (fromFg != null) return fromFg;

            float? best = null;
            for (uint i = 0; i < arrSize && i < 256; i++)
            {
                float? fps = ReadEntryFps(view, arrOffset + i * entrySize);
                if (fps != null && (best == null || fps > best)) best = fps;
            }
            return best;
        }
        catch
        {
            return null; // RTSS 未运行或共享内存不可用
        }
    }

    static float? ReadEntryFps(MemoryMappedViewAccessor view, long off)
    {
        uint flags = view.ReadUInt32(off + OffFlags);
        if ((flags & ApiUsageMask) == 0) return null; // 无图形 API 不统计
        uint frameTime = view.ReadUInt32(off + OffFrameTime);
        if (frameTime >= 1000 && frameTime <= 1000000)
        {
            float fps = 1000000f / frameTime;
            if (fps > 1) return fps;
        }
        uint t0 = view.ReadUInt32(off + OffTime0);
        uint t1 = view.ReadUInt32(off + OffTime1);
        uint frames = view.ReadUInt32(off + OffFrames);
        if (t1 > t0 && frames > 0)
        {
            float fps = 1000f * frames / (t1 - t0);
            if (fps > 1) return fps;
        }
        return null;
    }
}

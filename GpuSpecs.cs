namespace PcCheck;

/// <summary>显卡规格库：按型号查 GPU 代号/工艺/显存/位宽/带宽/着色器数（CPU-Z/GPU-Z 同级别信息，内置表）。</summary>
public static class GpuSpecs
{
    static readonly (string Key, string Spec)[] Table =
    {
        // AMD RX 系列
        ("RX 560",  "AMD Polaris 21 · 14 nm · 约 30 亿晶体管 · GDDR5 128-bit · 带宽 112 GB/s · 1024 着色器 · 64 TMUs · 16 ROPs"),
        ("RX 550",  "AMD Lexa · 14 nm · GDDR5 128-bit · 512 着色器"),
        ("RX 570",  "AMD Polaris 20 · 14 nm · GDDR5 256-bit · 带宽 224 GB/s · 2048 着色器"),
        ("RX 580",  "AMD Polaris 20 · 14 nm · 约 57 亿晶体管 · GDDR5 256-bit · 带宽 256 GB/s · 2304 着色器"),
        ("RX 590",  "AMD Polaris 30 · 12 nm · GDDR5 256-bit · 2304 着色器"),
        ("RX 6600", "AMD Navi 23 · 7 nm · GDDR6 128-bit · 带宽 224 GB/s · 1792 着色器"),
        ("RX 6700 XT", "AMD Navi 22 · 7 nm · GDDR6 192-bit · 2560 着色器"),
        ("RX 6800", "AMD Navi 21 · 7 nm · GDDR6 256-bit · 3840 着色器"),
        ("RX 6900 XT", "AMD Navi 21 · 7 nm · GDDR6 256-bit · 5120 着色器"),
        ("RX 7600", "AMD Navi 33 · 6 nm · GDDR6 128-bit · 2048 着色器"),
        ("RX 7800 XT", "AMD Navi 32 · 5 nm · GDDR6 256-bit · 3840 着色器"),
        ("RX 7900 XTX", "AMD Navi 31 · 5 nm · GDDR6 384-bit · 6144 着色器"),
        // NVIDIA GTX 系列
        ("GT 1030",  "NVIDIA GP108 · 14 nm · GDDR5 64-bit · 384 着色器"),
        ("GTX 1050 Ti", "NVIDIA GP107 · 14 nm · GDDR5 128-bit · 768 着色器"),
        ("GTX 1060", "NVIDIA GP106 · 16 nm · GDDR5 192-bit · 1280 着色器"),
        ("GTX 1070", "NVIDIA GP104 · 16 nm · GDDR5 256-bit · 1920 着色器"),
        ("GTX 1080", "NVIDIA GP104 · 16 nm · GDDR5X 256-bit · 2560 着色器"),
        ("GTX 1650", "NVIDIA TU117 · 12 nm · GDDR5 128-bit · 896 着色器"),
        ("GTX 1660", "NVIDIA TU116 · 12 nm · GDDR5 192-bit · 1408 着色器"),
        ("GTX 1660 Ti", "NVIDIA TU116 · 12 nm · GDDR6 192-bit · 1536 着色器"),
        // NVIDIA RTX 系列
        ("RTX 2060", "NVIDIA TU106 · 12 nm · GDDR6 192-bit · 1920 着色器"),
        ("RTX 2070", "NVIDIA TU106 · 12 nm · GDDR6 256-bit · 2304 着色器"),
        ("RTX 2080", "NVIDIA TU104 · 12 nm · GDDR6 256-bit · 2944 着色器"),
        ("RTX 3050", "NVIDIA GA107 · 8 nm · GDDR6 128-bit · 2560 着色器"),
        ("RTX 3060", "NVIDIA GA106 · 8 nm · GDDR6 192-bit · 3584 着色器"),
        ("RTX 3070", "NVIDIA GA104 · 8 nm · GDDR6 256-bit · 5888 着色器"),
        ("RTX 3080", "NVIDIA GA102 · 8 nm · GDDR6X 320-bit · 8704 着色器"),
        ("RTX 3090", "NVIDIA GA102 · 8 nm · GDDR6X 384-bit · 10496 着色器"),
        ("RTX 4060", "NVIDIA AD107 · 5 nm · GDDR6 128-bit · 3072 着色器"),
        ("RTX 4070", "NVIDIA AD104 · 5 nm · GDDR6X 192-bit · 5888 着色器"),
        ("RTX 4080", "NVIDIA AD103 · 5 nm · GDDR6X 256-bit · 9728 着色器"),
        ("RTX 4090", "NVIDIA AD102 · 5 nm · GDDR6X 384-bit · 16384 着色器"),
    };

    /// <summary>按显卡名查规格；未收录返回提示。</summary>
    public static string Lookup(string gpuName)
    {
        foreach (var (key, spec) in Table)
            if (gpuName.Contains(key, StringComparison.OrdinalIgnoreCase)) return spec;
        return "内置规格库未收录此型号（可在开源仓库提交型号数据）";
    }
}

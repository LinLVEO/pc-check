# 小白电脑体检（PC Check）

> 开源 · 干净 · 无广告 —— 一键看懂你的电脑（WPF 版 v2.1）

双击即用的一体式体检工具。**只读本机信息，不上传任何数据，不依赖任何外部程序。**

## 功能

- **基础体检**：硬件总览（处理器/内存/显卡/主板/BIOS/操作系统）+ AVX2 检测 + 磁盘体检 + 健康小结，一键复制/保存报告
- **实时监控**：内置传感器读取——CPU/GPU 温度、占用、频率、功耗、显存、内存、风扇，每 2 秒刷新，温度高自动变红；Xeon E5 v1 自动校准 -11°C
- **深度参数（CPU-Z/GPU-Z 同级别）**：
  - CPU：核心/线程、主频+外频×倍频、代号/工艺（型号推断）、L1/L2/L3 全缓存（系统实测，含路数/行大小）、完整指令集
  - 内存条：插槽/容量/频率/DDR 代数/厂商/型号
  - 显卡：GPU 代号/工艺/晶体管数/显存类型/位宽/带宽/着色器数（内置规格库）、驱动/分辨率
  - 主板/BIOS

## 使用

双击 `PcCheck.exe`，弹窗点“是”（需要管理员权限读取 CPU 温度），自动体检。

## 构建

```bash
dotnet publish PcCheck.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o 发布
```

## 技术说明

- C# / .NET 8 WPF，极简界面（顶部 Tab）
- 数据来源：WMI（Win32_*）+ `GetLogicalProcessorInformation`（缓存实测）+ `IsProcessorFeaturePresent`（指令集）+ LibreHardwareMonitorLib 0.9.2（传感器）
- 显卡规格为内置公开规格库（表外型号提示可在仓库补充数据）
- 依赖：System.Management、LibreHardwareMonitorLib

## 许可

MIT

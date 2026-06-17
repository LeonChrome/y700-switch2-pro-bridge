using System;
using System.Collections.Generic;
using System.Linq;

namespace Y700Switch2V60Viiper;

public enum ViiperPushRateMode
{
    Hz66,
    Hz125,
    Hz250
}

public enum ViiperGyroMode
{
    Hold250Hz,
    Source60Hz,
    Scaled250Hz,
    Filtered250Hz
}

public enum VirtualBackendMode
{
    ViiperServer,
    LibViiperExperimental,
    EmbeddedUsbipExperimental
}

public sealed record ViiperPushRateOption(
    ViiperPushRateMode Mode,
    string Label,
    double Hz,
    TimeSpan Interval)
{
    public static IReadOnlyList<ViiperPushRateOption> All { get; } =
    [
        new(ViiperPushRateMode.Hz125, "125Hz（推荐）", 125.0, TimeSpan.FromMilliseconds(8)),
        new(ViiperPushRateMode.Hz66, "66Hz / source-paced", 1000.0 / 15.0, TimeSpan.FromMilliseconds(15)),
        new(ViiperPushRateMode.Hz250, "250Hz（高性能）", 250.0, TimeSpan.FromMilliseconds(4))
    ];

    public static ViiperPushRateOption Default => All[0];

    public static ViiperPushRateOption FromLabel(string? label)
    {
        return All.FirstOrDefault(o => string.Equals(o.Label, label, StringComparison.Ordinal)) ?? Default;
    }
}

public sealed record ViiperGyroModeOption(
    ViiperGyroMode Mode,
    string Label,
    string Description)
{
    public static IReadOnlyList<ViiperGyroModeOption> All { get; } =
    [
        new(ViiperGyroMode.Source60Hz, "source_60hz（推荐）", "IMU 只在 BLE 新样本到达时输出，重复帧清空 gyro/accel。"),
        new(ViiperGyroMode.Hold250Hz, "hold_250hz", "所有推送都重复 latest gyro/accel，最接近旧行为。"),
        new(ViiperGyroMode.Scaled250Hz, "scaled_250hz", "重复高刷新时按 BLE/Push 比例缩放 gyro，accel 保持重力语义。"),
        new(ViiperGyroMode.Filtered250Hz, "filtered_250hz", "对 gyro/accel 做低通，降低重复推送的尖峰。")
    ];

    public static ViiperGyroModeOption Default => All[0];

    public static ViiperGyroModeOption FromLabel(string? label)
    {
        return All.FirstOrDefault(o => string.Equals(o.Label, label, StringComparison.Ordinal)) ?? Default;
    }
}

public sealed record VirtualBackendOption(
    VirtualBackendMode Mode,
    string Label,
    string Description)
{
    public static IReadOnlyList<VirtualBackendOption> All { get; } =
    [
        new(
            VirtualBackendMode.ViiperServer,
            "VIIPER server（稳定）",
            "当前稳定三模路径：内置 VIIPER 进程 + usbip-win2，保留 PS5 HD haptic。"),
        new(
            VirtualBackendMode.LibViiperExperimental,
            "libVIIPER（实验）",
            "V6.2 短链路目标：同进程 libVIIPER；需要 libVIIPER.dll 与 GPL 兼容发布策略。"),
        new(
            VirtualBackendMode.EmbeddedUsbipExperimental,
            "Embedded USBIP（研究）",
            "V6.2 研究目标：C# 内嵌 USBIP server；仍需要 usbip-win2 内核驱动。")
    ];

    public static VirtualBackendOption Default => All[0];

    public static VirtualBackendOption FromLabel(string? label)
    {
        return All.FirstOrDefault(o => string.Equals(o.Label, label, StringComparison.Ordinal)) ?? Default;
    }
}

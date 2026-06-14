using System;
using System.Collections.Generic;

namespace Y700Switch2V55Manager;

public enum OutputModeId
{
    Unknown,
    Recovery,
    Pro2,
    DualSenseLike,
    Xbox,
    XboxElite
}

public sealed record OutputModeProfile(
    string ProfileId,
    OutputModeId ModeId,
    string Label,
    string ExpectedUsbMarker,
    bool ManagerReady,
    string Notes);

public static class OutputModeCatalog
{
    public static readonly OutputModeProfile Recovery = new(
        "hid_only",
        OutputModeId.Recovery,
        "HID 纯恢复",
        "VID_054C&PID_0CE6",
        true,
        "用于安全重刷和 USB 枚举恢复的最小恢复固件。");

    public static readonly OutputModeProfile Pro2 = new(
        "pro2_bridge_v5_5",
        OutputModeId.Pro2,
        "Pro2 / Nintendo",
        "VID_057E&PID_2069",
        true,
        "稳定的 BLE Pro2 到 Nintendo 风格 USB 桥接。");

    public static readonly OutputModeProfile DualSenseLike = new(
        "hid_audio_uac1_4ch_ds5like",
        OutputModeId.DualSenseLike,
        "新和联胜 / PS5",
        "VID_054C&PID_0CE6",
        true,
        "严格 PS5 / DualSense 兼容身份，支持四声道 HD 震动音频与普通震动共同调度。");

    public static readonly OutputModeProfile Xbox = new(
        "xinput_bridge_v5_8",
        OutputModeId.Xbox,
        "Xbox / XInput",
        "VID_045E&PID_028E",
        true,
        "真实 Xbox 360 / XInput 风格 USB 后端，普通震动回传到 Pro2 BLE。");

    public static IReadOnlyList<OutputModeProfile> All { get; } = new[]
    {
        Recovery,
        DualSenseLike,
        Pro2,
        Xbox
    };

    public static OutputModeProfile? FindByProfileId(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return null;
        }

        foreach (OutputModeProfile profile in All)
        {
            if (string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        return null;
    }
}

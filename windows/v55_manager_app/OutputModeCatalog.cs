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
        "DualSense-like + Wireless Controller Audio",
        "VID_054C&PID_0CE6",
        true,
        "DualSense-like HID 加四声道控制器音频实验链路。");

    public static readonly OutputModeProfile Xbox = new(
        "xinput_bridge_v5_8",
        OutputModeId.Xbox,
        "Xbox / XInput",
        "VID_045E&PID_028E",
        true,
        "真实 Xbox 360 / XInput 风格 USB 后端，普通震动回传到 Pro2 BLE。");

    public static readonly OutputModeProfile XboxElite = new(
        "xinput_elite_bridge_v5_9",
        OutputModeId.XboxElite,
        "新和联胜 / Xbox Elite 2 GIP",
        "VID_045E&PID_0B00",
        true,
        "Xbox Elite 2 / GIP 身份实验入口，复用当前 Pro2 BLE 输入、普通震动和背键映射。");

    public static IReadOnlyList<OutputModeProfile> All { get; } = new[]
    {
        Recovery,
        Pro2,
        DualSenseLike,
        Xbox,
        XboxElite
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

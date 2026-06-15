using System;
using System.Buffers.Binary;

namespace Y700Switch2V60Viiper;

public enum ViiperVirtualMode
{
    DualSenseLike,
    Pro2,
    Xbox
}

public sealed record ViiperDeviceProfile(
    ViiperVirtualMode Mode,
    string Label,
    string DeviceType,
    int InputSize,
    int FeedbackSize,
    TimeSpan SendInterval)
{
    public static ViiperDeviceProfile DualSenseLike { get; } = new(
        ViiperVirtualMode.DualSenseLike,
        "新和联胜 / PS5",
        "dualsense",
        33,
        6,
        TimeSpan.FromMilliseconds(4));

    public static ViiperDeviceProfile Pro2 { get; } = new(
        ViiperVirtualMode.Pro2,
        "Pro2 / Nintendo",
        "ns2pro",
        27,
        34,
        TimeSpan.FromMilliseconds(4));

    public static ViiperDeviceProfile Xbox { get; } = new(
        ViiperVirtualMode.Xbox,
        "Xbox / XInput",
        "xbox360",
        20,
        2,
        TimeSpan.FromMilliseconds(4));
}

public static class VirtualPadPackets
{
    private const ushort AxisCenter = GamepadState.AxisCenter;
    private const ushort AxisMax = GamepadState.AxisMax;
    private const ushort TriggerMax = GamepadState.TriggerMax;

    public static byte[] NeutralInput(ViiperDeviceProfile profile)
    {
        return profile.Mode switch
        {
            ViiperVirtualMode.DualSenseLike => DualSenseNeutral(),
            ViiperVirtualMode.Pro2 => Ns2ProNeutral(),
            ViiperVirtualMode.Xbox => new byte[20],
            _ => throw new ArgumentOutOfRangeException(nameof(profile))
        };
    }

    public static byte[] FromGamepad(ViiperDeviceProfile profile, GamepadState? state)
    {
        state ??= GamepadState.Neutral();
        return profile.Mode switch
        {
            ViiperVirtualMode.DualSenseLike => DualSenseFromGamepad(state),
            ViiperVirtualMode.Pro2 => Ns2ProFromGamepad(state),
            ViiperVirtualMode.Xbox => XboxFromGamepad(state),
            _ => throw new ArgumentOutOfRangeException(nameof(profile))
        };
    }

    public static string FeedbackSummary(ViiperDeviceProfile profile, byte[] data)
    {
        return profile.Mode switch
        {
            ViiperVirtualMode.DualSenseLike when data.Length >= 6 =>
                $"DualSense output: small={data[0]}, large={data[1]}, led=({data[2]},{data[3]},{data[4]}), player={data[5]}",
            ViiperVirtualMode.Pro2 when data.Length >= 34 =>
                $"NS2Pro output: flags=0x{data[32]:X2}, player_led=0x{data[33]:X2}, L={Hex(data.AsSpan(0, 6))}, R={Hex(data.AsSpan(16, 6))}",
            ViiperVirtualMode.Xbox when data.Length >= 2 =>
                $"XInput output: left={data[0]}, right={data[1]}",
            _ => profile.Label + " output: " + Hex(data)
        };
    }

    private static byte[] DualSenseNeutral()
    {
        byte[] b = new byte[33];
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(31, 2), -8192);
        return b;
    }

    private static byte[] Ns2ProNeutral()
    {
        byte[] b = new byte[27];
        WriteU16(b, 4, 0x0800);
        WriteU16(b, 6, 0x0800);
        WriteU16(b, 8, 0x0800);
        WriteU16(b, 10, 0x0800);
        b[24] = 9;
        b[26] = 1;
        return b;
    }

    private static byte[] DualSenseFromGamepad(GamepadState state)
    {
        byte[] b = new byte[33];
        b[0] = unchecked((byte)Axis12ToI8(state.Lx, invert: false));
        b[1] = unchecked((byte)Axis12ToI8(state.Ly, invert: true));
        b[2] = unchecked((byte)Axis12ToI8(state.Rx, invert: false));
        b[3] = unchecked((byte)Axis12ToI8(state.Ry, invert: true));
        WriteU32(b, 4, DualSenseButtons(state));
        b[8] = DualSenseDpad(state);
        b[9] = Trigger12ToU8(state.L2, state.IsPressed(GamepadButtons.L2));
        b[10] = Trigger12ToU8(state.R2, state.IsPressed(GamepadButtons.R2));

        WriteI16(b, 21, state.GyroValid ? state.GyroX : (short)0);
        WriteI16(b, 23, state.GyroValid ? state.GyroY : (short)0);
        WriteI16(b, 25, state.GyroValid ? state.GyroZ : (short)0);
        WriteI16(b, 27, state.AccelValid ? state.AccelX : (short)0);
        WriteI16(b, 29, state.AccelValid ? state.AccelY : (short)0);
        WriteI16(b, 31, state.AccelValid ? state.AccelZ : (short)-8192);
        return b;
    }

    private static byte[] Ns2ProFromGamepad(GamepadState state)
    {
        byte[] b = new byte[27];
        WriteU32(b, 0, Ns2ProButtons(state));
        WriteU16(b, 4, SnapAxisCenter(state.Lx));
        WriteU16(b, 6, SnapAxisCenter(state.Ly));
        WriteU16(b, 8, SnapAxisCenter(state.Rx));
        WriteU16(b, 10, SnapAxisCenter(state.Ry));
        WriteI16(b, 12, state.AccelValid ? state.AccelX : (short)0);
        WriteI16(b, 14, state.AccelValid ? state.AccelY : (short)0);
        WriteI16(b, 16, state.AccelValid ? state.AccelZ : (short)0);
        WriteI16(b, 18, state.GyroValid ? state.GyroX : (short)0);
        WriteI16(b, 20, state.GyroValid ? state.GyroY : (short)0);
        WriteI16(b, 22, state.GyroValid ? state.GyroZ : (short)0);
        b[24] = Ns2ProBatteryLevel(state.BatteryPercent);
        b[25] = state.BatteryCharging ? (byte)1 : (byte)0;
        b[26] = 1;
        return b;
    }

    private static byte[] XboxFromGamepad(GamepadState state)
    {
        byte[] b = new byte[20];
        WriteU32(b, 0, XboxButtons(state));
        b[4] = Trigger12ToU8(state.L2, state.IsPressed(GamepadButtons.L2));
        b[5] = Trigger12ToU8(state.R2, state.IsPressed(GamepadButtons.R2));
        WriteI16(b, 6, Axis12ToI16(state.Lx, invert: false));
        WriteI16(b, 8, Axis12ToI16(state.Ly, invert: false));
        WriteI16(b, 10, Axis12ToI16(state.Rx, invert: false));
        WriteI16(b, 12, Axis12ToI16(state.Ry, invert: false));
        return b;
    }

    private static uint DualSenseButtons(GamepadState state)
    {
        uint buttons = 0;
        if (state.IsPressed(GamepadButtons.West)) buttons |= 0x00000010;
        if (state.IsPressed(GamepadButtons.South)) buttons |= 0x00000020;
        if (state.IsPressed(GamepadButtons.East)) buttons |= 0x00000040;
        if (state.IsPressed(GamepadButtons.North)) buttons |= 0x00000080;
        if (state.IsPressed(GamepadButtons.L1)) buttons |= 0x00000100;
        if (state.IsPressed(GamepadButtons.R1)) buttons |= 0x00000200;
        if (state.IsPressed(GamepadButtons.L2)) buttons |= 0x00000400;
        if (state.IsPressed(GamepadButtons.R2)) buttons |= 0x00000800;
        if (state.IsPressed(GamepadButtons.Back)) buttons |= 0x00001000;
        if (state.IsPressed(GamepadButtons.Start)) buttons |= 0x00002000;
        if (state.IsPressed(GamepadButtons.LeftStick)) buttons |= 0x00004000;
        if (state.IsPressed(GamepadButtons.RightStick)) buttons |= 0x00008000;
        if (state.IsPressed(GamepadButtons.Home)) buttons |= 0x00010000;
        if (state.IsPressed(GamepadButtons.Capture)) buttons |= 0x00020000;
        if (state.IsPressed(GamepadButtons.PaddleLeft)) buttons |= 0x00400000;
        if (state.IsPressed(GamepadButtons.PaddleRight)) buttons |= 0x00800000;
        return buttons;
    }

    private static byte DualSenseDpad(GamepadState state)
    {
        byte dpad = 0;
        if (state.IsPressed(GamepadButtons.DPadUp)) dpad |= 0x01;
        if (state.IsPressed(GamepadButtons.DPadDown)) dpad |= 0x02;
        if (state.IsPressed(GamepadButtons.DPadLeft)) dpad |= 0x04;
        if (state.IsPressed(GamepadButtons.DPadRight)) dpad |= 0x08;
        return dpad;
    }

    private static uint Ns2ProButtons(GamepadState state)
    {
        uint buttons = 0;
        if (state.IsPressed(GamepadButtons.South)) buttons |= 0x00000001;
        if (state.IsPressed(GamepadButtons.East)) buttons |= 0x00000002;
        if (state.IsPressed(GamepadButtons.West)) buttons |= 0x00000004;
        if (state.IsPressed(GamepadButtons.North)) buttons |= 0x00000008;
        if (state.IsPressed(GamepadButtons.R1)) buttons |= 0x00000010;
        if (state.IsPressed(GamepadButtons.R2)) buttons |= 0x00000020;
        if (state.IsPressed(GamepadButtons.Start)) buttons |= 0x00000040;
        if (state.IsPressed(GamepadButtons.RightStick)) buttons |= 0x00000080;
        if (state.IsPressed(GamepadButtons.DPadDown)) buttons |= 0x00000100;
        if (state.IsPressed(GamepadButtons.DPadRight)) buttons |= 0x00000200;
        if (state.IsPressed(GamepadButtons.DPadLeft)) buttons |= 0x00000400;
        if (state.IsPressed(GamepadButtons.DPadUp)) buttons |= 0x00000800;
        if (state.IsPressed(GamepadButtons.L1)) buttons |= 0x00001000;
        if (state.IsPressed(GamepadButtons.L2)) buttons |= 0x00002000;
        if (state.IsPressed(GamepadButtons.Back)) buttons |= 0x00004000;
        if (state.IsPressed(GamepadButtons.LeftStick)) buttons |= 0x00008000;
        if (state.IsPressed(GamepadButtons.Home)) buttons |= 0x00010000;
        if (state.IsPressed(GamepadButtons.Capture)) buttons |= 0x00020000;
        if (state.IsPressed(GamepadButtons.PaddleRight)) buttons |= 0x00040000;
        if (state.IsPressed(GamepadButtons.PaddleLeft)) buttons |= 0x00080000;
        if (state.IsPressed(GamepadButtons.Aux)) buttons |= 0x00100000;
        return buttons;
    }

    private static uint XboxButtons(GamepadState state)
    {
        uint buttons = 0;
        if (state.IsPressed(GamepadButtons.DPadUp)) buttons |= 0x0001;
        if (state.IsPressed(GamepadButtons.DPadDown)) buttons |= 0x0002;
        if (state.IsPressed(GamepadButtons.DPadLeft)) buttons |= 0x0004;
        if (state.IsPressed(GamepadButtons.DPadRight)) buttons |= 0x0008;
        if (state.IsPressed(GamepadButtons.Start)) buttons |= 0x0010;
        if (state.IsPressed(GamepadButtons.Back)) buttons |= 0x0020;
        if (state.IsPressed(GamepadButtons.LeftStick)) buttons |= 0x0040;
        if (state.IsPressed(GamepadButtons.RightStick)) buttons |= 0x0080;
        if (state.IsPressed(GamepadButtons.L1)) buttons |= 0x0100;
        if (state.IsPressed(GamepadButtons.R1)) buttons |= 0x0200;
        if (state.IsPressed(GamepadButtons.Home)) buttons |= 0x0400;
        if (state.IsPressed(GamepadButtons.South)) buttons |= 0x1000;
        if (state.IsPressed(GamepadButtons.East)) buttons |= 0x2000;
        if (state.IsPressed(GamepadButtons.West)) buttons |= 0x4000;
        if (state.IsPressed(GamepadButtons.North)) buttons |= 0x8000;
        return buttons;
    }

    private static ushort SnapAxisCenter(ushort value)
    {
        value = ClampAxis(value);
        int delta = value - AxisCenter;
        if (delta < 0) delta = -delta;
        return delta <= 64 ? AxisCenter : value;
    }

    private static sbyte Axis12ToI8(ushort value, bool invert)
    {
        short i16 = Axis12ToI16(value, invert);
        int scaled = i16 >= 0 ? (i16 * 127 + 16383) / 32767 : (i16 * 128 - 16384) / 32768;
        if (scaled < -128) scaled = -128;
        if (scaled > 127) scaled = 127;
        return (sbyte)scaled;
    }

    private static short Axis12ToI16(ushort value, bool invert)
    {
        value = SnapAxisCenter(value);
        int centered = value - AxisCenter;
        int scaled = centered >= 0
            ? (centered * 32767) / (AxisMax - AxisCenter)
            : (centered * 32768) / AxisCenter;
        if (invert) scaled = -scaled;
        if (scaled < short.MinValue) return short.MinValue;
        if (scaled > short.MaxValue) return short.MaxValue;
        return (short)scaled;
    }

    private static byte Trigger12ToU8(ushort value, bool pressed)
    {
        if (value == 0 && pressed)
        {
            return 255;
        }
        uint scaled = ((uint)value * 255u + TriggerMax / 2u) / TriggerMax;
        return (byte)Math.Min(255u, scaled);
    }

    private static byte Ns2ProBatteryLevel(byte batteryPercent)
    {
        if (batteryPercent == GamepadState.BatteryUnknown)
        {
            return 9;
        }

        int level = (batteryPercent * 9 + 50) / 100;
        return (byte)Math.Clamp(level, 0, 9);
    }

    private static ushort ClampAxis(ushort value)
    {
        return value > AxisMax ? AxisMax : value;
    }

    private static void WriteU16(byte[] b, int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(offset, 2), value);
    }

    private static void WriteI16(byte[] b, int offset, short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(offset, 2), value);
    }

    private static void WriteU32(byte[] b, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset, 4), value);
    }

    private static string Hex(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

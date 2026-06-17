using System;
using System.Buffers.Binary;

namespace Y700Switch2V60Viiper;

public sealed class Pro2HidReportParser
{
    private const int Fd2FullReportMinLength = 60;
    private const ushort Center12 = GamepadState.AxisCenter;
    private const ushort CenterLearnMaxDelta = 256;
    private const ushort AxisDeadzone = 64;
    private const ushort FullScaleRange = 1600;
    private readonly object gate = new();
    private readonly AxisCalibration standardAxis = new();
    private readonly AxisCalibration legacyAxis = new();

    public bool TryParse(ReadOnlySpan<byte> report, out GamepadState state, out string source)
    {
        lock (gate)
        {
            state = GamepadState.Neutral();
            source = "";
            if (report.IsEmpty)
            {
                return false;
            }

            if (TryParseHidInputReport(report, out state, out source))
            {
                return true;
            }

            // Raw GATT captures used by the ESP32 route are useful in diagnostics and
            // let this parser be tested without a Windows HID handle.
            if (TryParseFd2Payload(report, out state, out source))
            {
                return true;
            }

            if (TryParseLegacyPayload(report, out state, out source))
            {
                return true;
            }

            return false;
        }
    }

    public bool TryParseFd2Payload(ReadOnlySpan<byte> report, out GamepadState state, out string source)
    {
        lock (gate)
        {
            state = GamepadState.Neutral();
            source = "";
            if (report.Length < Fd2FullReportMinLength ||
                !LooksLikeFd2Payload(report))
            {
                return false;
            }

            ParseFd2Payload(report, state);
            source = "fd2_payload";
            return true;
        }
    }

    public bool TryParseLegacyPayload(ReadOnlySpan<byte> report, out GamepadState state, out string source)
    {
        lock (gate)
        {
            state = GamepadState.Neutral();
            source = "";
            if (report.Length < 11 || !LooksLikeLegacyPayload(report))
            {
                return false;
            }

            ParseLegacyPayload(report, state);
            source = "legacy_payload";
            return true;
        }
    }

    public bool TryParseHidInputReport(ReadOnlySpan<byte> report, out GamepadState state, out string source)
    {
        lock (gate)
        {
            state = GamepadState.Neutral();
            source = "";
            if (report.IsEmpty)
            {
                return false;
            }

            byte reportId = report[0];
            if ((reportId == 0x30 || reportId == 0x31 || reportId == 0x21 || reportId == 0x23) &&
                report.Length >= 13)
            {
                ParseStandardReport(report, state);
                source = "switch_pro_standard";
                return true;
            }

            return false;
        }
    }

    private void ParseStandardReport(ReadOnlySpan<byte> report, GamepadState state)
    {
        ApplyStandardButtons(state, report[3], report[4], report[5]);
        ApplyAxes(standardAxis,
            state,
            Unpack12X(report, 6),
            Unpack12Y(report, 6),
            Unpack12X(report, 9),
            Unpack12Y(report, 9));

        if (report.Length >= 49)
        {
            ApplyMotionSample(state, report.Slice(37, 12));
        }
        else if (report.Length >= 25)
        {
            ApplyMotionSample(state, report.Slice(13, 12));
        }
    }

    private void ParseFd2Payload(ReadOnlySpan<byte> payload, GamepadState state)
    {
        uint buttons = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
        ApplyFd2Buttons(state, buttons);
        ApplyAxes(standardAxis,
            state,
            Unpack12X(payload, 10),
            Unpack12Y(payload, 10),
            Unpack12X(payload, 13),
            Unpack12Y(payload, 13));

        if (payload.Length >= 60)
        {
            ApplyMotionSample(state, payload.Slice(48, 12));
        }
    }

    private void ParseLegacyPayload(ReadOnlySpan<byte> payload, GamepadState state)
    {
        ApplyStandardButtons(state, payload[2], payload[3], payload[4]);
        ApplyAxes(legacyAxis,
            state,
            Unpack12X(payload, 5),
            Unpack12Y(payload, 5),
            Unpack12X(payload, 8),
            Unpack12Y(payload, 8));
    }

    private static bool LooksLikeFd2Payload(ReadOnlySpan<byte> payload)
    {
        uint buttons = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
        return (buttons & 0xFC300020u) == 0;
    }

    private static bool LooksLikeLegacyPayload(ReadOnlySpan<byte> payload)
    {
        return payload[0] != 0x30 && payload[0] != 0x31 && payload[0] != 0x21 && payload[0] != 0x23;
    }

    private static void ApplyStandardButtons(GamepadState state, byte right, byte shared, byte left)
    {
        state.Buttons = GamepadButtons.None;
        Set(state, GamepadButtons.West, (right & 0x01) != 0);
        Set(state, GamepadButtons.North, (right & 0x02) != 0);
        Set(state, GamepadButtons.South, (right & 0x04) != 0);
        Set(state, GamepadButtons.East, (right & 0x08) != 0);
        Set(state, GamepadButtons.R1, (right & 0x40) != 0);
        Set(state, GamepadButtons.R2, (right & 0x80) != 0);
        Set(state, GamepadButtons.Back, (shared & 0x01) != 0);
        Set(state, GamepadButtons.Start, (shared & 0x02) != 0);
        Set(state, GamepadButtons.RightStick, (shared & 0x04) != 0);
        Set(state, GamepadButtons.LeftStick, (shared & 0x08) != 0);
        Set(state, GamepadButtons.Home, (shared & 0x10) != 0);
        Set(state, GamepadButtons.Capture, (shared & 0x20) != 0);
        Set(state, GamepadButtons.DPadDown, (left & 0x01) != 0);
        Set(state, GamepadButtons.DPadUp, (left & 0x02) != 0);
        Set(state, GamepadButtons.DPadRight, (left & 0x04) != 0);
        Set(state, GamepadButtons.DPadLeft, (left & 0x08) != 0);
        Set(state, GamepadButtons.L1, (left & 0x40) != 0);
        Set(state, GamepadButtons.L2, (left & 0x80) != 0);
        state.L2 = state.IsPressed(GamepadButtons.L2) ? GamepadState.TriggerMax : (ushort)0;
        state.R2 = state.IsPressed(GamepadButtons.R2) ? GamepadState.TriggerMax : (ushort)0;
    }

    private static void ApplyFd2Buttons(GamepadState state, uint buttons)
    {
        state.Buttons = GamepadButtons.None;
        Set(state, GamepadButtons.West, (buttons & 0x00000001) != 0);
        Set(state, GamepadButtons.North, (buttons & 0x00000002) != 0);
        Set(state, GamepadButtons.South, (buttons & 0x00000004) != 0);
        Set(state, GamepadButtons.East, (buttons & 0x00000008) != 0);
        Set(state, GamepadButtons.R1, (buttons & 0x00000040) != 0);
        Set(state, GamepadButtons.R2, (buttons & 0x00000080) != 0);
        Set(state, GamepadButtons.Back, (buttons & 0x00000100) != 0);
        Set(state, GamepadButtons.Start, (buttons & 0x00000200) != 0);
        Set(state, GamepadButtons.RightStick, (buttons & 0x00000400) != 0);
        Set(state, GamepadButtons.LeftStick, (buttons & 0x00000800) != 0);
        Set(state, GamepadButtons.Home, (buttons & 0x00001000) != 0);
        Set(state, GamepadButtons.Capture, (buttons & 0x00002000) != 0);
        Set(state, GamepadButtons.Aux, (buttons & 0x00004000) != 0);
        Set(state, GamepadButtons.DPadDown, (buttons & 0x00010000) != 0);
        Set(state, GamepadButtons.DPadUp, (buttons & 0x00020000) != 0);
        Set(state, GamepadButtons.DPadRight, (buttons & 0x00040000) != 0);
        Set(state, GamepadButtons.DPadLeft, (buttons & 0x00080000) != 0);
        Set(state, GamepadButtons.L1, (buttons & 0x00400000) != 0);
        Set(state, GamepadButtons.L2, (buttons & 0x00800000) != 0);
        Set(state, GamepadButtons.PaddleRight, (buttons & 0x01000000) != 0);
        Set(state, GamepadButtons.PaddleLeft, (buttons & 0x02000000) != 0);
        state.L2 = state.IsPressed(GamepadButtons.L2) ? GamepadState.TriggerMax : (ushort)0;
        state.R2 = state.IsPressed(GamepadButtons.R2) ? GamepadState.TriggerMax : (ushort)0;
    }

    private static void Set(GamepadState state, GamepadButtons button, bool pressed)
    {
        if (pressed)
        {
            state.Buttons |= button;
        }
    }

    private static ushort Unpack12X(ReadOnlySpan<byte> data, int offset)
    {
        return Clamp12(data[offset] | ((data[offset + 1] & 0x0F) << 8));
    }

    private static ushort Unpack12Y(ReadOnlySpan<byte> data, int offset)
    {
        return Clamp12(((data[offset + 1] >> 4) & 0x0F) | (data[offset + 2] << 4));
    }

    private static ushort Clamp12(int value)
    {
        if (value < 0) return 0;
        if (value > GamepadState.AxisMax) return GamepadState.AxisMax;
        return (ushort)value;
    }

    private static void ApplyAxes(
        AxisCalibration calibration,
        GamepadState state,
        ushort lx,
        ushort ly,
        ushort rx,
        ushort ry)
    {
        calibration.LearnIfCentered(state.Buttons, lx, ly, rx, ry);
        state.Lx = calibration.Normalize(lx, calibration.CenterLx);
        state.Ly = calibration.Normalize(ly, calibration.CenterLy);
        state.Rx = calibration.Normalize(rx, calibration.CenterRx);
        state.Ry = calibration.Normalize(ry, calibration.CenterRy);
    }

    private static void ApplyMotionSample(GamepadState state, ReadOnlySpan<byte> sample)
    {
        if (sample.Length < 12)
        {
            return;
        }

        state.AccelValid = true;
        state.GyroValid = true;
        state.AccelX = BinaryPrimitives.ReadInt16LittleEndian(sample.Slice(0, 2));
        state.AccelY = BinaryPrimitives.ReadInt16LittleEndian(sample.Slice(2, 2));
        state.AccelZ = BinaryPrimitives.ReadInt16LittleEndian(sample.Slice(4, 2));
        state.GyroX = BinaryPrimitives.ReadInt16LittleEndian(sample.Slice(6, 2));
        state.GyroY = BinaryPrimitives.ReadInt16LittleEndian(sample.Slice(8, 2));
        state.GyroZ = BinaryPrimitives.ReadInt16LittleEndian(sample.Slice(10, 2));
    }

    private sealed class AxisCalibration
    {
        private const int SamplesRequired = 20;
        private uint sampleCount;
        private uint sumLx;
        private uint sumLy;
        private uint sumRx;
        private uint sumRy;

        public ushort CenterLx { get; private set; } = Center12;
        public ushort CenterLy { get; private set; } = Center12;
        public ushort CenterRx { get; private set; } = Center12;
        public ushort CenterRy { get; private set; } = Center12;

        public void LearnIfCentered(GamepadButtons buttons, ushort lx, ushort ly, ushort rx, ushort ry)
        {
            if (buttons == GamepadButtons.None &&
                NearFactoryCenter(lx) &&
                NearFactoryCenter(ly) &&
                NearFactoryCenter(rx) &&
                NearFactoryCenter(ry))
            {
                sumLx += lx;
                sumLy += ly;
                sumRx += rx;
                sumRy += ry;
                sampleCount++;
                if (sampleCount >= SamplesRequired)
                {
                    CenterLx = (ushort)(sumLx / sampleCount);
                    CenterLy = (ushort)(sumLy / sampleCount);
                    CenterRx = (ushort)(sumRx / sampleCount);
                    CenterRy = (ushort)(sumRy / sampleCount);
                    ResetSums();
                }
            }
            else
            {
                ResetSums();
            }
        }

        public ushort Normalize(ushort value, ushort center)
        {
            int delta = value - center;
            bool negative = delta < 0;
            int magnitude = negative ? -delta : delta;
            if (magnitude <= AxisDeadzone)
            {
                return Center12;
            }

            int usable = FullScaleRange - AxisDeadzone;
            int target = negative ? Center12 : GamepadState.AxisMax - Center12;
            int scaled = ((magnitude - AxisDeadzone) * target + usable / 2) / usable;
            if (scaled > target)
            {
                scaled = target;
            }

            int output = negative ? Center12 - scaled : Center12 + scaled;
            return Clamp12(output);
        }

        private static bool NearFactoryCenter(ushort value)
        {
            int delta = value - Center12;
            if (delta < 0)
            {
                delta = -delta;
            }
            return delta <= CenterLearnMaxDelta;
        }

        private void ResetSums()
        {
            sampleCount = 0;
            sumLx = 0;
            sumLy = 0;
            sumRx = 0;
            sumRy = 0;
        }
    }
}

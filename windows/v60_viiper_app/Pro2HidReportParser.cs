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
    private readonly MotionCalibration motion = new();

    public string StartManualGyroCalibration()
    {
        lock (gate)
        {
            return motion.StartManualCalibration();
        }
    }

    public string GyroCalibrationSummary
    {
        get
        {
            lock (gate)
            {
                return motion.Summary;
            }
        }
    }

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

    public bool TryParsePrimaryProPayload(
        ReadOnlySpan<byte> report,
        out GamepadState state,
        out string source)
    {
        lock (gate)
        {
            state = GamepadState.Neutral();
            source = "";
            if (report.Length < 11 || report[1] != 0x20)
            {
                return false;
            }

            ApplyPrimaryProButtons(state, report[2], report[3], report[4]);
            ApplyAxes(standardAxis,
                state,
                Unpack12X(report, 5),
                Unpack12Y(report, 5),
                Unpack12X(report, 8),
                Unpack12Y(report, 8));
            source = "primary_pro_payload";
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
            AttachRawImuSamples(state, report, 13, 3);
            ApplyMotionSample(state, report.Slice(37, 12));
        }
        else if (report.Length >= 25)
        {
            AttachRawImuSamples(state, report, 13, 1);
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
            state.MotionTimestampUs =
                BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(42, 4));
            AttachRawImuSamples(state, payload, 48, 3);
            // Common FD2 carries one IMU sample after its temperature field.
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

    private static void ApplyPrimaryProButtons(
        GamepadState state,
        byte right,
        byte left,
        byte shared)
    {
        state.Buttons = GamepadButtons.None;
        Set(state, GamepadButtons.South, (right & 0x01) != 0);
        Set(state, GamepadButtons.East, (right & 0x02) != 0);
        Set(state, GamepadButtons.West, (right & 0x04) != 0);
        Set(state, GamepadButtons.North, (right & 0x08) != 0);
        Set(state, GamepadButtons.R1, (right & 0x10) != 0);
        Set(state, GamepadButtons.R2, (right & 0x20) != 0);
        Set(state, GamepadButtons.Start, (right & 0x40) != 0);
        Set(state, GamepadButtons.RightStick, (right & 0x80) != 0);

        Set(state, GamepadButtons.DPadDown, (left & 0x01) != 0);
        Set(state, GamepadButtons.DPadRight, (left & 0x02) != 0);
        Set(state, GamepadButtons.DPadLeft, (left & 0x04) != 0);
        Set(state, GamepadButtons.DPadUp, (left & 0x08) != 0);
        Set(state, GamepadButtons.L1, (left & 0x10) != 0);
        Set(state, GamepadButtons.L2, (left & 0x20) != 0);
        Set(state, GamepadButtons.Back, (left & 0x40) != 0);
        Set(state, GamepadButtons.LeftStick, (left & 0x80) != 0);

        Set(state, GamepadButtons.Home, (shared & 0x01) != 0);
        Set(state, GamepadButtons.Capture, (shared & 0x02) != 0);
        Set(state, GamepadButtons.PaddleRight, (shared & 0x04) != 0);
        Set(state, GamepadButtons.PaddleLeft, (shared & 0x08) != 0);
        Set(state, GamepadButtons.Aux, (shared & 0x10) != 0);
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

    private void ApplyMotionSample(GamepadState state, ReadOnlySpan<byte> sample)
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
        motion.Apply(state);
    }

    private static void AttachRawImuSamples(
        GamepadState state,
        ReadOnlySpan<byte> payload,
        int offset,
        int maxSamples)
    {
        SwitchImuRawBlock block =
            ProfessionalImuConverter.DecodeSwitchImuSamples(payload, offset, maxSamples);
        state.SwitchRawImuSamples = block.Samples;
        state.SwitchRawImuOffset = block.Offset;
        state.SwitchRawImuBytesHex = block.RawBytesHex;
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

    private sealed class MotionCalibration
    {
        private const int AutoSamplesRequired = 64;
        private const int ManualSamplesRequired = 200;
        private const int GyroStationaryMaxAbs = 256;
        private const int AccelMagnitudeMin = 3000;
        private const int AccelMagnitudeMax = 5400;
        private const double MaxGyroStdRaw = 3.5;
        private const double MaxAccelNormStdRaw = 12.0;

        private long gyroXSum;
        private long gyroYSum;
        private long gyroZSum;
        private long gyroXSquareSum;
        private long gyroYSquareSum;
        private long gyroZSquareSum;
        private double accelNormSum;
        private double accelNormSquareSum;
        private int sampleCount;
        private bool calibrated;
        private bool manualCalibrating;
        private double gyroXBias;
        private double gyroYBias;
        private double gyroZBias;
        private string lastResult = "auto_waiting_stationary";

        public string Summary =>
            "status=" + (manualCalibrating ? "manual_collecting" : calibrated ? "calibrated" : "not_calibrated") +
            " samples=" + sampleCount +
            " bias_raw=" + gyroXBias.ToString("0.###") + "," +
            gyroYBias.ToString("0.###") + "," +
            gyroZBias.ToString("0.###") +
            " result=" + lastResult;

        public string StartManualCalibration()
        {
            manualCalibrating = true;
            lastResult = "manual_collecting_keep_controller_still";
            ResetLearningWindow();
            return "陀螺仪三秒校准已开始；请将手柄静置在稳定平面。";
        }

        public void Apply(GamepadState state)
        {
            int accelX = state.AccelX;
            int accelY = state.AccelY;
            int accelZ = state.AccelZ;
            int gyroX = state.GyroX;
            int gyroY = state.GyroY;
            int gyroZ = state.GyroZ;

            bool canLearn =
                ControlsAreIdle(state) &&
                IsStationary(accelX, accelY, accelZ, gyroX, gyroY, gyroZ);

            if (manualCalibrating)
            {
                if (canLearn)
                {
                    Learn(accelX, accelY, accelZ, gyroX, gyroY, gyroZ, ManualSamplesRequired, true);
                }
                else
                {
                    lastResult = "manual_motion_detected_window_restarted";
                    ResetLearningWindow();
                }
            }
            else if (!calibrated)
            {
                if (canLearn)
                {
                    Learn(accelX, accelY, accelZ, gyroX, gyroY, gyroZ, AutoSamplesRequired, false);
                }
                else
                {
                    ResetLearningWindow();
                }
            }

            if (!calibrated)
            {
                return;
            }

            state.GyroX = ClampInt16((int)Math.Round(gyroX - gyroXBias));
            state.GyroY = ClampInt16((int)Math.Round(gyroY - gyroYBias));
            state.GyroZ = ClampInt16((int)Math.Round(gyroZ - gyroZBias));
        }

        private static bool ControlsAreIdle(GamepadState state)
        {
            const int axisTolerance = 128;
            return state.Buttons == GamepadButtons.None &&
                   Math.Abs(state.Lx - Center12) <= axisTolerance &&
                   Math.Abs(state.Ly - Center12) <= axisTolerance &&
                   Math.Abs(state.Rx - Center12) <= axisTolerance &&
                   Math.Abs(state.Ry - Center12) <= axisTolerance;
        }

        private static bool IsStationary(
            int accelX,
            int accelY,
            int accelZ,
            int gyroX,
            int gyroY,
            int gyroZ)
        {
            long accelMagnitudeSquared =
                (long)accelX * accelX +
                (long)accelY * accelY +
                (long)accelZ * accelZ;
            bool plausibleGravity =
                accelMagnitudeSquared >= (long)AccelMagnitudeMin * AccelMagnitudeMin &&
                accelMagnitudeSquared <= (long)AccelMagnitudeMax * AccelMagnitudeMax;
            bool gyroQuiet =
                Math.Abs(gyroX) <= GyroStationaryMaxAbs &&
                Math.Abs(gyroY) <= GyroStationaryMaxAbs &&
                Math.Abs(gyroZ) <= GyroStationaryMaxAbs;
            return plausibleGravity && gyroQuiet;
        }

        private void Learn(
            int accelX,
            int accelY,
            int accelZ,
            int gyroX,
            int gyroY,
            int gyroZ,
            int samplesRequired,
            bool manual)
        {
            gyroXSum += gyroX;
            gyroYSum += gyroY;
            gyroZSum += gyroZ;
            gyroXSquareSum += (long)gyroX * gyroX;
            gyroYSquareSum += (long)gyroY * gyroY;
            gyroZSquareSum += (long)gyroZ * gyroZ;
            double accelNorm = Math.Sqrt(
                (double)accelX * accelX +
                (double)accelY * accelY +
                (double)accelZ * accelZ);
            accelNormSum += accelNorm;
            accelNormSquareSum += accelNorm * accelNorm;
            sampleCount++;
            if (sampleCount < samplesRequired)
            {
                return;
            }

            double gyroXMean = (double)gyroXSum / sampleCount;
            double gyroYMean = (double)gyroYSum / sampleCount;
            double gyroZMean = (double)gyroZSum / sampleCount;
            double gyroXStd = StandardDeviation(gyroXSquareSum, gyroXMean, sampleCount);
            double gyroYStd = StandardDeviation(gyroYSquareSum, gyroYMean, sampleCount);
            double gyroZStd = StandardDeviation(gyroZSquareSum, gyroZMean, sampleCount);
            double accelNormMean = accelNormSum / sampleCount;
            double accelNormVariance = Math.Max(0, accelNormSquareSum / sampleCount - accelNormMean * accelNormMean);
            double accelNormStd = Math.Sqrt(accelNormVariance);
            if (gyroXStd > MaxGyroStdRaw ||
                gyroYStd > MaxGyroStdRaw ||
                gyroZStd > MaxGyroStdRaw ||
                accelNormStd > MaxAccelNormStdRaw)
            {
                lastResult = (manual ? "manual" : "auto") +
                             "_rejected_unstable std_gyro=" +
                             gyroXStd.ToString("0.###") + "," +
                             gyroYStd.ToString("0.###") + "," +
                             gyroZStd.ToString("0.###") +
                             " accel_norm_std=" + accelNormStd.ToString("0.###");
                ResetLearningWindow();
                return;
            }

            gyroXBias = gyroXMean;
            gyroYBias = gyroYMean;
            gyroZBias = gyroZMean;
            calibrated = true;
            manualCalibrating = false;
            lastResult = (manual ? "manual" : "auto") +
                         "_committed std_gyro=" +
                         gyroXStd.ToString("0.###") + "," +
                         gyroYStd.ToString("0.###") + "," +
                         gyroZStd.ToString("0.###") +
                         " accel_norm_std=" + accelNormStd.ToString("0.###");
            ResetLearningWindow();
        }

        private static double StandardDeviation(long squareSum, double mean, int count)
        {
            double variance = Math.Max(0, (double)squareSum / count - mean * mean);
            return Math.Sqrt(variance);
        }

        private static short ClampInt16(int value)
        {
            if (value < short.MinValue) return short.MinValue;
            if (value > short.MaxValue) return short.MaxValue;
            return (short)value;
        }

        private void ResetLearningWindow()
        {
            gyroXSum = 0;
            gyroYSum = 0;
            gyroZSum = 0;
            gyroXSquareSum = 0;
            gyroYSquareSum = 0;
            gyroZSquareSum = 0;
            accelNormSum = 0;
            accelNormSquareSum = 0;
            sampleCount = 0;
        }
    }
}

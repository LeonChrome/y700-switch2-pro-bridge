using System;
using System.Buffers.Binary;
using Y700Switch2V60Viiper;

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Pack12(byte[] data, int offset, ushort x, ushort y)
{
    data[offset] = (byte)(x & 0xff);
    data[offset + 1] = (byte)(((x >> 8) & 0x0f) | ((y & 0x0f) << 4));
    data[offset + 2] = (byte)((y >> 4) & 0xff);
}

static (int Low, int High) DecodeBleAmplitudes(
    ReadOnlySpan<byte> packet,
    int frameOffset)
{
    ulong value = 0;
    for (int i = 0; i < 5; i++)
    {
        value |= (ulong)packet[frameOffset + i] << (8 * i);
    }
    return (
        (int)((value >> 10) & 0x03ff),
        (int)((value >> 30) & 0x03ff));
}

static byte[] HapticFrame(byte kind, ReadOnlySpan<byte> payload)
{
    byte[] frame = new byte[DualSenseHapticFrame.WireSize];
    frame[0] = kind;
    BinaryPrimitives.WriteUInt16LittleEndian(
        frame.AsSpan(1, 2),
        (ushort)payload.Length);
    payload.CopyTo(frame.AsSpan(4));
    return frame;
}

static byte[] HapticPcmPacket(
    ref double leftPhase,
    ref double rightPhase,
    double leftHz,
    double rightHz)
{
    byte[] packet = new byte[384];
    const double sampleRate = 48000;
    for (int i = 0; i < 48; i++)
    {
        short left = (short)Math.Round(Math.Sin(leftPhase) * 12000);
        short right = (short)Math.Round(Math.Sin(rightPhase) * 12000);
        BinaryPrimitives.WriteInt16LittleEndian(
            packet.AsSpan(i * 8 + 4, 2),
            left);
        BinaryPrimitives.WriteInt16LittleEndian(
            packet.AsSpan(i * 8 + 6, 2),
            right);
        leftPhase += 2 * Math.PI * leftHz / sampleRate;
        rightPhase += 2 * Math.PI * rightHz / sampleRate;
    }
    return packet;
}

var state = new GamepadState
{
    Buttons = GamepadButtons.South |
              GamepadButtons.East |
              GamepadButtons.West |
              GamepadButtons.North |
              GamepadButtons.L1 |
              GamepadButtons.R1 |
              GamepadButtons.L2 |
              GamepadButtons.R2 |
              GamepadButtons.Back |
              GamepadButtons.Start |
              GamepadButtons.LeftStick |
              GamepadButtons.RightStick |
              GamepadButtons.DPadUp |
              GamepadButtons.DPadRight |
              GamepadButtons.Home |
              GamepadButtons.Capture,
    Lx = 0,
    Ly = 4095,
    Rx = 4095,
    Ry = 0,
    L2 = GamepadState.TriggerMax,
    R2 = GamepadState.TriggerMax,
    AccelValid = true,
    AccelZ = -8192,
    BatteryPercent = 50,
    BatteryCharging = true
};

byte[] ds = VirtualPadPackets.FromGamepad(ViiperDeviceProfile.DualSenseLike, state);
Expect(ds.Length == 33, "DualSense wire size");
Expect(unchecked((sbyte)ds[0]) == -128, "DualSense LX min");
Expect(unchecked((sbyte)ds[1]) == -128, "DualSense LY inverted max");
Expect(unchecked((sbyte)ds[2]) == 127, "DualSense RX max");
Expect(unchecked((sbyte)ds[3]) == 127, "DualSense RY inverted min");
uint dsButtons = BinaryPrimitives.ReadUInt32LittleEndian(ds.AsSpan(4, 4));
Expect((dsButtons & 0x000000F0) == 0x000000F0, "DualSense face buttons");
Expect((dsButtons & 0x0003FC00) == 0x0003FC00, "DualSense system/shoulder buttons");
Expect(ds[8] == 0x09, "DualSense dpad bitfield");
Expect(ds[9] == 255 && ds[10] == 255, "DualSense triggers");

byte[] ns = VirtualPadPackets.FromGamepad(ViiperDeviceProfile.Pro2, state);
Expect(ns.Length == 24, "NS2Pro wire size");
uint nsButtons = BinaryPrimitives.ReadUInt32LittleEndian(ns.AsSpan(0, 4));
Expect(nsButtons == 0x0003FAFF, "NS2Pro button bitfield");
Expect(BinaryPrimitives.ReadUInt16LittleEndian(ns.AsSpan(4, 2)) == 0, "NS2Pro LX min");
Expect(BinaryPrimitives.ReadUInt16LittleEndian(ns.AsSpan(6, 2)) == 4095, "NS2Pro LY max");
byte[] nsNeutral = VirtualPadPackets.NeutralInput(ViiperDeviceProfile.Pro2);
Expect(nsNeutral.Length == 24, "NS2Pro neutral wire size");

byte[] xb = VirtualPadPackets.FromGamepad(ViiperDeviceProfile.Xbox, state);
Expect(xb.Length == 20, "Xbox wire size");
uint xbButtons = BinaryPrimitives.ReadUInt32LittleEndian(xb.AsSpan(0, 4));
Expect((xbButtons & 0xFFFF) == 0xF7F9, "Xbox button bitfield");
Expect(xb[4] == 255 && xb[5] == 255, "Xbox triggers");
Expect(BinaryPrimitives.ReadInt16LittleEndian(xb.AsSpan(6, 2)) == short.MinValue, "Xbox LX min");
Expect(BinaryPrimitives.ReadInt16LittleEndian(xb.AsSpan(8, 2)) == short.MaxValue, "Xbox LY max");

var parser = new Pro2HidReportParser();
byte[] report = new byte[49];
report[0] = 0x30;
report[3] = 0xCF;
report[4] = 0x33;
report[5] = 0xC6;
Pack12(report, 6, 2048, 2048);
Pack12(report, 9, 2048, 2048);
BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(37, 2), 1);
BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(39, 2), 2);
BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(41, 2), 3);
BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(43, 2), 4);
BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(45, 2), 5);
BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(47, 2), 6);

Expect(parser.TryParse(report, out GamepadState parsed, out string source), "parse standard Pro2 HID");
Expect(source == "switch_pro_standard", "parse source");
Expect(parser.TryParseHidInputReport(report, out GamepadState parsedHid, out string hidSource), "strict parse standard Pro2 HID");
Expect(hidSource == "switch_pro_standard", "strict parse source");
Expect(parsedHid.Lx == GamepadState.AxisCenter, "strict parsed centered axes");
Expect(parsed.IsPressed(GamepadButtons.South), "parsed south");
Expect(parsed.IsPressed(GamepadButtons.East), "parsed east");
Expect(parsed.IsPressed(GamepadButtons.West), "parsed west");
Expect(parsed.IsPressed(GamepadButtons.North), "parsed north");
Expect(parsed.IsPressed(GamepadButtons.L1), "parsed L1");
Expect(parsed.IsPressed(GamepadButtons.R1), "parsed R1");
Expect(parsed.IsPressed(GamepadButtons.L2), "parsed L2");
Expect(parsed.IsPressed(GamepadButtons.R2), "parsed R2");
Expect(parsed.Lx == GamepadState.AxisCenter && parsed.Ry == GamepadState.AxisCenter, "parsed centered axes");
Expect(parsed.AccelValid && parsed.GyroValid, "parsed motion");
Expect(parsed.AccelX == 1 && parsed.GyroZ == 6, "parsed motion values");

byte[] fd2 = new byte[60];
BinaryPrimitives.WriteUInt32LittleEndian(
    fd2.AsSpan(4, 4),
    0x00020000u | 0x00000080u | 0x00000004u);
Pack12(fd2, 10, 2048, 2048);
Pack12(fd2, 13, 2048, 2048);
BinaryPrimitives.WriteInt16LittleEndian(fd2.AsSpan(48, 2), -7);
BinaryPrimitives.WriteInt16LittleEndian(fd2.AsSpan(58, 2), 77);
Expect(parser.TryParse(fd2, out GamepadState fd2Parsed, out string fd2Source), "parse FD2 BLE payload");
Expect(fd2Source == "fd2_payload", "FD2 parse source");
Expect(parser.TryParseFd2Payload(fd2, out GamepadState fd2DirectParsed, out string fd2DirectSource), "parse FD2 payload directly");
Expect(fd2DirectSource == "fd2_payload" && fd2DirectParsed.Lx == GamepadState.AxisCenter, "direct FD2 parse source");
Expect(fd2Parsed.IsPressed(GamepadButtons.South), "FD2 parsed south");
Expect(fd2Parsed.IsPressed(GamepadButtons.R2), "FD2 parsed R2");
Expect(fd2Parsed.IsPressed(GamepadButtons.DPadUp), "FD2 parsed dpad up");
Expect(fd2Parsed.R2 == GamepadState.TriggerMax, "FD2 parsed analog R2");
Expect(fd2Parsed.AccelValid && fd2Parsed.GyroValid, "FD2 parsed motion");
Expect(fd2Parsed.AccelX == -7 && fd2Parsed.GyroZ == 77, "FD2 parsed motion values");
byte[] shortFd2 = new byte[16];
BinaryPrimitives.WriteUInt32LittleEndian(shortFd2.AsSpan(4, 4), 0x00000004u);
Pack12(shortFd2, 10, 2048, 2048);
Expect(!parser.TryParseFd2Payload(shortFd2, out _, out _), "direct FD2 parser rejects short non-full payload");

byte[] viiperLikeReport = new byte[64];
viiperLikeReport[0] = 0x05;
viiperLikeReport[3] = 0xCF;
viiperLikeReport[4] = 0x33;
viiperLikeReport[5] = 0xC6;
Expect(!parser.TryParseHidInputReport(viiperLikeReport, out _, out _), "strict parser rejects VIIPER ns2pro input report");

byte[] nsFeedback = new byte[34];
nsFeedback[0] = 0x50;
nsFeedback[1] = 0x22;
nsFeedback[2] = 0x11;
nsFeedback[3] = 0x44;
nsFeedback[4] = 0x33;
nsFeedback[5] = 0x55;
nsFeedback[16] = 0x50;
nsFeedback[17] = 0xaa;
nsFeedback[18] = 0xbb;
nsFeedback[19] = 0xcc;
nsFeedback[20] = 0xdd;
nsFeedback[21] = 0xee;
nsFeedback[32] = 0x03;
nsFeedback[33] = 0x0f;
Expect(Pro2OutputPacketMapper.TryMapFeedback(ViiperDeviceProfile.Pro2, nsFeedback, out Pro2OutputPacket hdPacket, out string hdReason), "map ns2pro feedback");
Expect(hdPacket.Report.Length == 64, "ns2pro output report length");
Expect(hdPacket.Report[0] == 0x02, "ns2pro report id");
Expect(hdPacket.Report[1] == 0x50 && hdPacket.Report[17] == 0x50, "ns2pro motor block headers");
Expect(hdPacket.Report[2] == 0x22 && hdPacket.Report[18] == 0xaa, "ns2pro motor frames copied");
Expect(hdPacket.PlayerLedMask == 0x0f, "ns2pro player LED carried");
Expect(hdPacket.Active, "ns2pro active rumble detected");
byte[] blePacket = new byte[Pro2BleRumblePacketEncoder.BlePacketSize];
Expect(
    Pro2BleRumblePacketEncoder.TryEncodeRaw02(hdPacket.Report, 0x0a, blePacket, out bool bleActive, out string bleError),
    "encode raw02 to BLE packet: " + bleError);
Expect(blePacket[0] == 0x00, "BLE rumble report prefix");
Expect(blePacket[1] == 0x5a && blePacket[17] == 0x5a, "BLE rumble packet id in motor blocks");
Expect(bleActive, "BLE rumble active");
Expect(!hdPacket.Report.AsSpan(1, 32).SequenceEqual(blePacket.AsSpan(1, 32)), "BLE packet is converted, not raw HID copied");

byte[] ledOnly = (byte[])nsFeedback.Clone();
ledOnly[32] = 0x02;
Expect(!Pro2OutputPacketMapper.TryMapFeedback(ViiperDeviceProfile.Pro2, ledOnly, out _, out string ledReason), "skip led-only ns2pro feedback");
Expect(ledReason.Contains("led-only", StringComparison.OrdinalIgnoreCase), "led-only skip reason");

byte[] xinputFeedback = [200, 120];
Expect(Pro2OutputPacketMapper.TryMapFeedback(ViiperDeviceProfile.Xbox, xinputFeedback, out Pro2OutputPacket ordinaryPacket, out _), "map xinput feedback");
Expect(ordinaryPacket.Report.Length == 64, "ordinary output report length");
Expect(ordinaryPacket.Report[0] == 0x02, "ordinary report id");
Expect(ordinaryPacket.Report[1] == 0x50 && ordinaryPacket.Report[17] == 0x50, "ordinary motor block headers");
Expect(ordinaryPacket.Active, "ordinary active rumble detected");
Expect(ordinaryPacket.Report[2] != 0 || ordinaryPacket.Report[18] != 0, "ordinary rumble frame populated");

byte[] stopFeedback = [0, 0];
Expect(Pro2OutputPacketMapper.TryMapFeedback(ViiperDeviceProfile.Xbox, stopFeedback, out Pro2OutputPacket stopPacket, out _), "map xinput stop feedback");
Expect(!stopPacket.Active, "ordinary stop is neutral");
byte[] stopBle = new byte[Pro2BleRumblePacketEncoder.BlePacketSize];
Expect(
    Pro2BleRumblePacketEncoder.TryEncodeRaw02(stopPacket.Report, 0x01, stopBle, out bool stopBleActive, out string stopBleError),
    "encode stop raw02 to BLE packet: " + stopBleError);
Expect(!stopBleActive, "BLE stop packet is neutral");

byte[] gainZeroBle = new byte[Pro2BleRumblePacketEncoder.BlePacketSize];
Expect(
    Pro2BleRumblePacketEncoder.TryEncodeRaw02(
        ordinaryPacket.Report,
        0x02,
        gainZeroBle,
        out bool gainZeroActive,
        out string gainZeroError,
        0),
    "encode zero-gain rumble: " + gainZeroError);
(int gainZeroLow, int gainZeroHigh) =
    DecodeBleAmplitudes(gainZeroBle, 2);
Expect(!gainZeroActive, "zero gain disables physical rumble");
Expect(gainZeroLow == 0 && gainZeroHigh == 0, "zero gain encodes zero amplitudes");

byte[] gainOneBle = new byte[Pro2BleRumblePacketEncoder.BlePacketSize];
Expect(
    Pro2BleRumblePacketEncoder.TryEncodeRaw02(
        ordinaryPacket.Report,
        0x03,
        gainOneBle,
        out bool gainOneActive,
        out string gainOneError,
        1),
    "encode 1x rumble: " + gainOneError);
(int gainOneLow, int gainOneHigh) =
    DecodeBleAmplitudes(gainOneBle, 2);
Expect(gainOneActive, "1x gain keeps rumble active");
Expect(gainOneLow is > 0 and < 1023 && gainOneHigh is > 0 and < 1023, "1x amplitudes remain proportional");

byte[] gainThreeBle = new byte[Pro2BleRumblePacketEncoder.BlePacketSize];
Expect(
    Pro2BleRumblePacketEncoder.TryEncodeRaw02(
        ordinaryPacket.Report,
        0x04,
        gainThreeBle,
        out bool gainThreeActive,
        out string gainThreeError,
        3),
    "encode 3x rumble: " + gainThreeError);
(int gainThreeLow, int gainThreeHigh) =
    DecodeBleAmplitudes(gainThreeBle, 2);
Expect(gainThreeActive, "3x gain keeps rumble active");
Expect(gainThreeLow == 1023 && gainThreeHigh == 1023, "3x amplitudes saturate at Pro2 maximum");
Expect(
    V60UserSettings.NormalizeRumbleMultiplier(-1) == 0 &&
    V60UserSettings.NormalizeRumbleMultiplier(3.8) == 3 &&
    V60UserSettings.NormalizeRumbleMultiplier(double.NaN) == 1,
    "rumble multiplier is normalized to 0..3");

var hapticScheduler = new DualSenseHapticRumbleScheduler();
byte[] compatibilityReport = new byte[64];
compatibilityReport[0] = 0x02;
compatibilityReport[1] = 0x01;
compatibilityReport[3] = 72;
compatibilityReport[4] = 180;
Expect(
    hapticScheduler.TryProcess(
        HapticFrame(1, compatibilityReport),
        out Pro2OutputPacket compatibilityPacket,
        out string compatibilitySummary,
        out string compatibilityReason),
    "DualSense compatibility output maps to ordinary rumble: " +
    compatibilityReason);
Expect(
    compatibilityPacket.Source == "dualsense-ordinary" &&
    compatibilityPacket.Active &&
    compatibilitySummary.Contains("compatibility", StringComparison.Ordinal),
    "DualSense compatibility mode selects ordinary rumble");

double blockedLeftPhase = 0;
double blockedRightPhase = 0;
byte[] blockedAudio = HapticPcmPacket(
    ref blockedLeftPhase,
    ref blockedRightPhase,
    140,
    330);
Expect(
    !hapticScheduler.TryProcess(
        HapticFrame(2, blockedAudio),
        out _,
        out _,
        out string blockedReason) &&
    blockedReason.Contains("blocked", StringComparison.OrdinalIgnoreCase),
    "DualSense compatibility mode blocks HD audio");

byte[] audioModeReport = new byte[64];
audioModeReport[0] = 0x02;
Expect(
    hapticScheduler.TryProcess(
        HapticFrame(1, audioModeReport),
        out Pro2OutputPacket transitionStop,
        out string audioModeSummary,
        out string audioModeReason),
    "DualSense audio mode transition emits a stop packet: " +
    audioModeReason);
Expect(
    !transitionStop.Active &&
    audioModeSummary.Contains("audio_haptics", StringComparison.Ordinal),
    "DualSense audio mode is selected by host flags");

double leftPhase = 0;
double rightPhase = 0;
Pro2OutputPacket? dualSenseHdPacket = null;
for (int i = 0; i < 40; i++)
{
    byte[] pcm = HapticPcmPacket(
        ref leftPhase,
        ref rightPhase,
        140,
        330);
    if (hapticScheduler.TryProcess(
            HapticFrame(2, pcm),
            out Pro2OutputPacket candidate,
            out _,
            out _) &&
        candidate.Active)
    {
        dualSenseHdPacket = candidate;
        break;
    }
}
Expect(
    dualSenseHdPacket != null,
    "four-channel DualSense PCM produces HD raw02 output");
Expect(
    dualSenseHdPacket!.Source == "dualsense-hd-audio" &&
    dualSenseHdPacket.Report.Length == 64 &&
    dualSenseHdPacket.Report[0] == 0x02,
    "DualSense HD output is a Pro2 raw02 report");

string originalPath = @"C:\Windows\System32";
string withUsbip = UsbipRuntimeLocator.BuildPathWithUsbipDirectory(
    originalPath,
    new UsbipRuntime(@"C:\USBip\usbip.exe", @"C:\USBip"));
Expect(withUsbip.StartsWith(@"C:\USBip;", StringComparison.OrdinalIgnoreCase), "usbip dir prepended to PATH");
Expect(
    UsbipRuntimeLocator.BuildPathWithUsbipDirectory(withUsbip, new UsbipRuntime(@"C:\USBip\usbip.exe", @"C:\USBip")) == withUsbip,
    "usbip dir is not duplicated in PATH");
Expect(UsbipRuntimeLocator.FindBundledInstaller() != null, "bundled usbip-win2 installer is discoverable");
string[] embeddedResources = typeof(UsbipRuntimeLocator).Assembly.GetManifestResourceNames();
Expect(
    embeddedResources.Contains(
        "Embedded.viiper.haptic.exe",
        StringComparer.Ordinal),
    "single-file app embeds the haptic VIIPER server");
Expect(
    embeddedResources.Contains("Embedded.usbip.installer", StringComparer.Ordinal),
    "single-file app embeds the usbip-win2 installer");
Expect(
    embeddedResources.Contains("Embedded.usbip.license", StringComparer.Ordinal),
    "single-file app embeds the usbip-win2 license");
Expect(
    Pro2BleInputSource.ShouldKeepLiveInput(58.5, 58.5, 173, 173),
    "real 15 ms / 58.5 Hz Pro2 session remains live instead of being rejected");
Expect(
    !Pro2BleInputSource.ShouldKeepLiveInput(20.0, 20.0, 80, 80),
    "unusable low-rate BLE input is rejected");
Expect(
    !Pro2BleInputSource.ShouldKeepLiveInput(65.0, 5.0, 200, 15),
    "mostly unparsed BLE notifications are rejected");

var heldState = new GamepadState
{
    Buttons = GamepadButtons.South,
    Lx = 3000,
    R2 = GamepadState.TriggerMax
};
GamepadState held = InputContinuityPolicy.Resolve(
    heldState,
    TimeSpan.FromMilliseconds(540),
    out string heldSource);
Expect(heldSource == "pro2_ble_hold", "540 ms BLE gap uses last-state hold");
Expect(held.IsPressed(GamepadButtons.South), "short BLE gap preserves buttons");
Expect(held.Lx == 3000 && held.R2 == GamepadState.TriggerMax, "short BLE gap preserves analog state");

GamepadState decayed = InputContinuityPolicy.Resolve(
    heldState,
    TimeSpan.FromMilliseconds(1125),
    out string decayedSource);
Expect(decayedSource == "pro2_ble_safe_hold", "longer BLE gap enters safe analog hold");
Expect(decayed.Buttons == GamepadButtons.None, "safe hold releases buttons");
Expect(decayed.Lx == heldState.Lx, "safe hold does not create visible stick drift");
Expect(decayed.R2 == 0, "safe hold releases triggers");

GamepadState stale = InputContinuityPolicy.Resolve(
    heldState,
    TimeSpan.FromMilliseconds(2100),
    out string staleSource);
Expect(staleSource == "neutral", "disconnected BLE input becomes neutral");
Expect(stale.Buttons == GamepadButtons.None && stale.Lx == GamepadState.AxisCenter, "neutral fallback is safe");

var stability = new Pro2InputStabilityFilter();
GamepadState centerState = GamepadState.Neutral();
Expect(
    stability.TryAccept(centerState, out GamepadState acceptedCenter, out _) &&
    acceptedCenter.Lx == GamepadState.AxisCenter,
    "stability filter accepts initial center");
var singleFrameSpike = new GamepadState
{
    Buttons = GamepadButtons.South,
    Lx = GamepadState.AxisMax,
    Ly = GamepadState.AxisCenter,
    Rx = GamepadState.AxisCenter,
    Ry = GamepadState.AxisCenter
};
Expect(
    !stability.TryAccept(singleFrameSpike, out GamepadState rejectedSpike, out string spikeReason) &&
    spikeReason == "axis_spike_hold" &&
    rejectedSpike.Lx == GamepadState.AxisCenter &&
    rejectedSpike.IsPressed(GamepadButtons.South),
    "stability filter holds one-frame axis spike while preserving buttons");
Expect(
    stability.TryAccept(centerState, out GamepadState acceptedAfterSpike, out _) &&
    acceptedAfterSpike.Lx == GamepadState.AxisCenter,
    "stability filter returns to center after discarded spike");
Expect(
    !stability.TryAccept(singleFrameSpike, out _, out _),
    "stability filter delays first confirmed large movement frame");
Expect(
    !stability.TryAccept(singleFrameSpike, out _, out _),
    "stability filter delays second confirmed large movement frame");
Expect(
    !stability.TryAccept(singleFrameSpike, out _, out _),
    "stability filter delays third confirmed large movement frame");
Expect(
    stability.TryAccept(singleFrameSpike, out GamepadState confirmedSpike, out _) &&
    confirmedSpike.Lx == GamepadState.AxisMax,
    "stability filter accepts sustained large movement as intentional");

using (WindowsTimerResolutionScope timerResolution = WindowsTimerResolutionScope.Begin())
{
    Expect(timerResolution.IsActive, "Windows 1 ms timer resolution request");
    using var timer = new HighResolutionPeriodicTimer(TimeSpan.FromMilliseconds(4));
    var timerWatch = System.Diagnostics.Stopwatch.StartNew();
    const int timerSamples = 125;
    for (int i = 0; i < timerSamples; i++)
    {
        Expect(timer.WaitForNextTick(CancellationToken.None), "high-resolution timer tick");
    }

    double timerHz = timerSamples / timerWatch.Elapsed.TotalSeconds;
    Expect(timerHz >= 220, "high-resolution timer cadence, actual=" + timerHz.ToString("F1"));
    Expect(
        timer.Backend == "high_resolution_waitable_timer_absolute",
        "high-resolution waitable timer backend: " + timer.Backend);
}

Console.WriteLine("v60_packet_mapper_test: passed");

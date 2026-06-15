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
Expect(ns.Length == 27, "NS2Pro wire size");
uint nsButtons = BinaryPrimitives.ReadUInt32LittleEndian(ns.AsSpan(0, 4));
Expect(nsButtons == 0x0003FAFF, "NS2Pro button bitfield");
Expect(BinaryPrimitives.ReadUInt16LittleEndian(ns.AsSpan(4, 2)) == 0, "NS2Pro LX min");
Expect(BinaryPrimitives.ReadUInt16LittleEndian(ns.AsSpan(6, 2)) == 4095, "NS2Pro LY max");
Expect(ns[24] == 5 && ns[25] == 1 && ns[26] == 1, "NS2Pro battery and power state");
byte[] nsNeutral = VirtualPadPackets.NeutralInput(ViiperDeviceProfile.Pro2);
Expect(nsNeutral.Length == 27 && nsNeutral[24] == 9 && nsNeutral[26] == 1, "NS2Pro neutral power state");

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
Expect(fd2Parsed.IsPressed(GamepadButtons.South), "FD2 parsed south");
Expect(fd2Parsed.IsPressed(GamepadButtons.R2), "FD2 parsed R2");
Expect(fd2Parsed.IsPressed(GamepadButtons.DPadUp), "FD2 parsed dpad up");
Expect(fd2Parsed.R2 == GamepadState.TriggerMax, "FD2 parsed analog R2");
Expect(fd2Parsed.AccelValid && fd2Parsed.GyroValid, "FD2 parsed motion");
Expect(fd2Parsed.AccelX == -7 && fd2Parsed.GyroZ == 77, "FD2 parsed motion values");

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

string originalPath = @"C:\Windows\System32";
string withUsbip = UsbipRuntimeLocator.BuildPathWithUsbipDirectory(
    originalPath,
    new UsbipRuntime(@"C:\USBip\usbip.exe", @"C:\USBip"));
Expect(withUsbip.StartsWith(@"C:\USBip;", StringComparison.OrdinalIgnoreCase), "usbip dir prepended to PATH");
Expect(
    UsbipRuntimeLocator.BuildPathWithUsbipDirectory(withUsbip, new UsbipRuntime(@"C:\USBip\usbip.exe", @"C:\USBip")) == withUsbip,
    "usbip dir is not duplicated in PATH");
Expect(UsbipRuntimeLocator.FindBundledInstaller() != null, "bundled usbip-win2 installer is discoverable");

Console.WriteLine("v60_packet_mapper_test: passed");

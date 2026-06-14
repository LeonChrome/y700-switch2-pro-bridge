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
    AccelZ = -8192
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

Console.WriteLine("v60_packet_mapper_test: passed");

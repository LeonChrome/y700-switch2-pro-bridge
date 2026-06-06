param(
    [ValidateRange(0, 255)]
    [int]$RightLight = 48,

    [ValidateRange(0, 255)]
    [int]$LeftHeavy = 80,

    [ValidateRange(50, 1000)]
    [int]$PulseMs = 250,

    [switch]$Send
)

$ErrorActionPreference = "Stop"

Write-Output "[V5_5_DS5_RUMBLE_TEST] vid=054c"
Write-Output "[V5_5_DS5_RUMBLE_TEST] pid=0ce6"
Write-Output "[V5_5_DS5_RUMBLE_TEST] right_light=$RightLight"
Write-Output "[V5_5_DS5_RUMBLE_TEST] left_heavy=$LeftHeavy"
Write-Output "[V5_5_DS5_RUMBLE_TEST] pulse_ms=$PulseMs"

if (!$Send) {
    Write-Output "[V5_5_DS5_RUMBLE_TEST] send=false"
    Write-Output "[V5_5_DS5_RUMBLE_TEST] result=dry_run"
    exit 0
}

$source = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class V55DualSenseRumbleTest {
    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData {
        public int Size;
        public Guid ClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    private struct InterfaceDetail {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=512)]
        public string Path;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("setupapi.dll", SetLastError=true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid guid, IntPtr enumerator, IntPtr parent, int flags);

    [DllImport("setupapi.dll", SetLastError=true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr set, IntPtr device, ref Guid guid, int index,
        ref InterfaceData data);

    [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr set, ref InterfaceData data, ref InterfaceDetail detail,
        int size, out int required, IntPtr device);

    [DllImport("setupapi.dll", SetLastError=true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern bool WriteFile(
        SafeFileHandle file, byte[] data, int length,
        out int written, IntPtr overlapped);

    private const int Present = 0x02;
    private const int DeviceInterface = 0x10;
    private const uint ReadWrite = 0xc0000000;
    private const uint Share = 0x03;
    private const uint OpenExisting = 3;

    public static int Send(byte rightLight, byte leftHeavy, int pulseMs) {
        foreach (string path in Enumerate()) {
            if (path.IndexOf("vid_054c&pid_0ce6",
                             StringComparison.OrdinalIgnoreCase) < 0) {
                continue;
            }

            using (SafeFileHandle file = CreateFile(
                path, ReadWrite, Share, IntPtr.Zero, OpenExisting, 0,
                IntPtr.Zero)) {
                if (file.IsInvalid) {
                    continue;
                }

                byte[] start = Build(rightLight, leftHeavy);
                byte[] stop = Build(0, 0);
                int written;
                bool startOk = WriteFile(
                    file, start, start.Length, out written, IntPtr.Zero);
                Console.WriteLine(
                    "[V5_5_DS5_RUMBLE_TEST] path=" + path);
                Console.WriteLine(
                    "[V5_5_DS5_RUMBLE_TEST] start_write=" +
                    startOk.ToString().ToLowerInvariant() +
                    " bytes=" + written);
                if (!startOk) {
                    return 2;
                }

                System.Threading.Thread.Sleep(pulseMs);
                bool stopOk = WriteFile(
                    file, stop, stop.Length, out written, IntPtr.Zero);
                Console.WriteLine(
                    "[V5_5_DS5_RUMBLE_TEST] stop_write=" +
                    stopOk.ToString().ToLowerInvariant() +
                    " bytes=" + written);
                return stopOk ? 0 : 3;
            }
        }
        Console.WriteLine("[V5_5_DS5_RUMBLE_TEST] device_found=false");
        return 1;
    }

    private static byte[] Build(byte rightLight, byte leftHeavy) {
        byte[] report = new byte[48];
        report[0] = 0x02;
        report[1] = 0x03;
        report[3] = rightLight;
        report[4] = leftHeavy;
        return report;
    }

    private static IEnumerable<string> Enumerate() {
        Guid guid;
        HidD_GetHidGuid(out guid);
        IntPtr set = SetupDiGetClassDevs(
            ref guid, IntPtr.Zero, IntPtr.Zero, Present | DeviceInterface);
        if (set == IntPtr.Zero || set.ToInt64() == -1) {
            yield break;
        }

        try {
            for (int index = 0; ; index++) {
                InterfaceData data = new InterfaceData {
                    Size = Marshal.SizeOf(typeof(InterfaceData))
                };
                if (!SetupDiEnumDeviceInterfaces(
                    set, IntPtr.Zero, ref guid, index, ref data)) {
                    yield break;
                }

                InterfaceDetail detail = new InterfaceDetail {
                    Size = IntPtr.Size == 8 ? 8 : 6
                };
                int required;
                if (SetupDiGetDeviceInterfaceDetail(
                    set, ref data, ref detail,
                    Marshal.SizeOf(typeof(InterfaceDetail)),
                    out required, IntPtr.Zero)) {
                    yield return detail.Path;
                }
            }
        } finally {
            SetupDiDestroyDeviceInfoList(set);
        }
    }
}
'@

if (!("V55DualSenseRumbleTest" -as [type])) {
    Add-Type $source
}
$result = [V55DualSenseRumbleTest]::Send(
    [byte]$RightLight,
    [byte]$LeftHeavy,
    $PulseMs)
Write-Output "[V5_5_DS5_RUMBLE_TEST] send=true"
Write-Output "[V5_5_DS5_RUMBLE_TEST] result=$(if ($result -eq 0) { 'passed' } else { 'failed' })"
exit $result

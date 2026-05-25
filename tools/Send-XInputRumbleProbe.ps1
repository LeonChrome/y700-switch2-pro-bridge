param(
    [int]$PulseMs = 600,
    [int]$Low = 60000,
    [int]$High = 60000
)

$ErrorActionPreference = "Stop"

$source = @'
using System;
using System.Runtime.InteropServices;

public static class XInputRumbleProbe {
  [StructLayout(LayoutKind.Sequential)]
  struct XINPUT_VIBRATION {
    public ushort wLeftMotorSpeed;
    public ushort wRightMotorSpeed;
  }

  [DllImport("xinput1_4.dll", EntryPoint="XInputSetState")]
  static extern int XInputSetState14(int dwUserIndex, ref XINPUT_VIBRATION pVibration);

  public static void Run(int pulseMs, int low, int high) {
    ushort l = (ushort)Math.Max(0, Math.Min(65535, low));
    ushort h = (ushort)Math.Max(0, Math.Min(65535, high));
    for (int i = 0; i < 4; i++) {
      XINPUT_VIBRATION vib = new XINPUT_VIBRATION();
      vib.wLeftMotorSpeed = l;
      vib.wRightMotorSpeed = h;
      int rc = XInputSetState14(i, ref vib);
      Console.WriteLine("start index=" + i + " rc=" + rc + " low=" + l + " high=" + h);
    }

    System.Threading.Thread.Sleep(Math.Max(1, pulseMs));

    for (int i = 0; i < 4; i++) {
      XINPUT_VIBRATION vib = new XINPUT_VIBRATION();
      vib.wLeftMotorSpeed = 0;
      vib.wRightMotorSpeed = 0;
      int rc = XInputSetState14(i, ref vib);
      Console.WriteLine("stop index=" + i + " rc=" + rc);
    }
  }
}
'@

Add-Type $source
[XInputRumbleProbe]::Run($PulseMs, $Low, $High)

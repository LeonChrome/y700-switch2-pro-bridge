param(
    [string]$SteamApiDll = "C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\steam_api64.dll",
    [string]$SteamRoot = "C:\Program Files (x86)\Steam",
    [string]$AppId = "413150",
    [int]$PulseMs = 650,
    [int]$GapMs = 160,
    [int]$Repeats = 2,
    [switch]$Extended
)

$ErrorActionPreference = "Stop"

if (!(Test-Path -LiteralPath $SteamApiDll)) {
    throw "steam_api64.dll not found: $SteamApiDll"
}

$dllDir = Split-Path -Parent $SteamApiDll
$runtimeDir = Join-Path $PSScriptRoot "steam_input_probe_runtime"
New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null
Copy-Item -LiteralPath $SteamApiDll -Destination (Join-Path $runtimeDir "steam_api64.dll") -Force
[IO.File]::WriteAllText((Join-Path $runtimeDir "steam_appid.txt"), $AppId)

$env:SteamAppId = $AppId
$env:SteamGameId = $AppId
$env:PATH = "$runtimeDir;$dllDir;$SteamRoot;$env:PATH"
Set-Location -LiteralPath $runtimeDir

$source = @'
using System;
using System.Runtime.InteropServices;
using System.Threading;

public static class SteamInputRumbleProbe {
  [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
  static extern bool SetDllDirectory(string lpPathName);

  [DllImport("steam_api64.dll", CallingConvention=CallingConvention.Cdecl)]
  static extern bool SteamAPI_Init();

  [DllImport("steam_api64.dll", CallingConvention=CallingConvention.Cdecl)]
  static extern void SteamAPI_Shutdown();

  [DllImport("steam_api64.dll", CallingConvention=CallingConvention.Cdecl)]
  static extern void SteamAPI_RunCallbacks();

  [DllImport("steam_api64.dll", CallingConvention=CallingConvention.Cdecl)]
  static extern IntPtr SteamAPI_SteamInput_v005();

  [DllImport("steam_api64.dll", CallingConvention=CallingConvention.Cdecl)]
  static extern bool SteamAPI_ISteamInput_Init(IntPtr self, bool explicitlyCallRunFrame);

  [DllImport("steam_api64.dll", CallingConvention=CallingConvention.Cdecl)]
  static extern void SteamAPI_ISteamInput_Shutdown(IntPtr self);

  [DllImport("steam_api64.dll", CallingConvention=CallingConvention.Cdecl)]
  static extern void SteamAPI_ISteamInput_RunFrame(IntPtr self, bool reserved);

  [DllImport("steam_api64.dll", CallingConvention=CallingConvention.Cdecl)]
  static extern int SteamAPI_ISteamInput_GetConnectedControllers(IntPtr self, [Out] ulong[] handlesOut);

  [DllImport("steam_api64.dll", CallingConvention=CallingConvention.Cdecl)]
  static extern int SteamAPI_ISteamInput_GetInputTypeForHandle(IntPtr self, ulong inputHandle);

  [DllImport("steam_api64.dll", CallingConvention=CallingConvention.Cdecl)]
  static extern void SteamAPI_ISteamInput_TriggerVibration(IntPtr self, ulong inputHandle, ushort leftSpeed, ushort rightSpeed);

  [DllImport("steam_api64.dll", CallingConvention=CallingConvention.Cdecl)]
  static extern void SteamAPI_ISteamInput_TriggerVibrationExtended(IntPtr self, ulong inputHandle, ushort leftSpeed, ushort rightSpeed, ushort leftTriggerSpeed, ushort rightTriggerSpeed);

  public static int Run(string dllDir, int pulseMs, int gapMs, int repeats, bool extended) {
    SetDllDirectory(dllDir);

    bool apiOk = SteamAPI_Init();
    Console.WriteLine("SteamAPI_Init=" + apiOk);
    if (!apiOk) return 2;

    IntPtr input = SteamAPI_SteamInput_v005();
    Console.WriteLine("SteamInput_v005=0x" + input.ToInt64().ToString("x"));
    if (input == IntPtr.Zero) return 3;

    bool inputOk = SteamAPI_ISteamInput_Init(input, true);
    Console.WriteLine("ISteamInput_Init=" + inputOk);

    for (int i = 0; i < 12; i++) {
      SteamAPI_ISteamInput_RunFrame(input, false);
      SteamAPI_RunCallbacks();
      Thread.Sleep(50);
    }

    ulong[] handles = new ulong[16];
    int count = SteamAPI_ISteamInput_GetConnectedControllers(input, handles);
    Console.WriteLine("GetConnectedControllers=" + count);
    for (int i = 0; i < count && i < handles.Length; i++) {
      int type = SteamAPI_ISteamInput_GetInputTypeForHandle(input, handles[i]);
      Console.WriteLine("handle[" + i + "]=0x" + handles[i].ToString("x16") + " type=" + type);
    }

    for (int r = 0; r < repeats; r++) {
      for (int i = 0; i < count && i < handles.Length; i++) {
        if (handles[i] == 0) continue;
        Console.WriteLine("rumble start repeat=" + r + " handle=0x" + handles[i].ToString("x16") + " extended=" + extended);
        if (extended) {
          SteamAPI_ISteamInput_TriggerVibrationExtended(input, handles[i], 65535, 65535, 65535, 65535);
        } else {
          SteamAPI_ISteamInput_TriggerVibration(input, handles[i], 65535, 65535);
        }
      }

      int steps = Math.Max(1, pulseMs / 50);
      for (int s = 0; s < steps; s++) {
        SteamAPI_ISteamInput_RunFrame(input, false);
        SteamAPI_RunCallbacks();
        Thread.Sleep(50);
      }

      for (int i = 0; i < count && i < handles.Length; i++) {
        if (handles[i] == 0) continue;
        Console.WriteLine("rumble stop repeat=" + r + " handle=0x" + handles[i].ToString("x16"));
        if (extended) {
          SteamAPI_ISteamInput_TriggerVibrationExtended(input, handles[i], 0, 0, 0, 0);
        } else {
          SteamAPI_ISteamInput_TriggerVibration(input, handles[i], 0, 0);
        }
      }

      for (int s = 0; s < 4; s++) {
        SteamAPI_ISteamInput_RunFrame(input, false);
        SteamAPI_RunCallbacks();
        Thread.Sleep(50);
      }
      Thread.Sleep(Math.Max(0, gapMs));
    }

    SteamAPI_ISteamInput_Shutdown(input);
    SteamAPI_Shutdown();
    return 0;
  }
}
'@

Add-Type $source
[SteamInputRumbleProbe]::Run($dllDir, $PulseMs, $GapMs, $Repeats, [bool]$Extended)

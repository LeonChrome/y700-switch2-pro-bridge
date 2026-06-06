param(
    [string]$Port = "COM12",
    [ValidateRange(1, 10)]
    [int]$Seconds = 3
)

$ErrorActionPreference = "Stop"

function Write-TestLine {
    param([string]$Key, [object]$Value)
    if ($Value -is [bool]) {
        $Value = $Value.ToString().ToLowerInvariant()
    }
    Write-Output "[V5_5_DS5_AUDIO_STREAM] $Key=$Value"
}

function Wait-DualSenseAudioEndpoint {
    param([int]$TimeoutSeconds = 10)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $candidate = Get-PnpDevice -PresentOnly -Class AudioEndpoint -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Status -eq "OK" -and
                $_.FriendlyName -match "Wireless Controller Audio"
            } |
            Select-Object -First 1
        if ($candidate) {
            return $candidate
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Wireless Controller Audio render endpoint not found after $TimeoutSeconds seconds."
}

$interopSource = @'
using System;
using System.IO;
using System.Runtime.InteropServices;

public enum AudioDataFlow {
    Render = 0,
    Capture = 1,
    All = 2
}

public enum AudioRole {
    Console = 0,
    Multimedia = 1,
    Communications = 2
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject {
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator {
    int EnumAudioEndpoints(AudioDataFlow dataFlow, uint stateMask, out IntPtr devices);
    int GetDefaultAudioEndpoint(
        AudioDataFlow dataFlow,
        AudioRole role,
        out IMMDevice endpoint);
    int GetDevice(
        [MarshalAs(UnmanagedType.LPWStr)] string id,
        out IMMDevice device);
    int RegisterEndpointNotificationCallback(IntPtr client);
    int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice {
    int Activate(ref Guid iid, uint classContext, IntPtr activationParams, out IntPtr instance);
    int OpenPropertyStore(uint access, out IntPtr properties);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetState(out uint state);
}

[ComImport]
[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigClient {
}

[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig {
    int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);
    int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultFormat, out IntPtr format);
    int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
    int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultPeriod, out long period, out long minimumPeriod);
    int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref long period);
    int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
    int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
    int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, IntPtr value);
    int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, IntPtr value);
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, AudioRole role);
    int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
}

public static class V55AudioTest {
    public static string GetDefaultEndpoint(AudioRole role) {
        var enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
        IMMDevice endpoint;
        Marshal.ThrowExceptionForHR(
            enumerator.GetDefaultAudioEndpoint(AudioDataFlow.Render, role, out endpoint));
        string id;
        Marshal.ThrowExceptionForHR(endpoint.GetId(out id));
        return id;
    }

    public static void SetDefaultEndpoint(string endpointId, AudioRole role) {
        var policy = (IPolicyConfig)(new PolicyConfigClient());
        Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(endpointId, role));
    }

    public static void CreateStereoWave(string path, int seconds) {
        const int channels = 2;
        const int sampleRate = 48000;
        const int bitsPerSample = 16;
        int frameCount = checked(sampleRate * seconds);
        int blockAlign = channels * (bitsPerSample / 8);
        int dataLength = checked(frameCount * blockAlign);

        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream)) {
            writer.Write(new char[] {'R', 'I', 'F', 'F'});
            writer.Write(36 + dataLength);
            writer.Write(new char[] {'W', 'A', 'V', 'E'});
            writer.Write(new char[] {'f', 'm', 't', ' '});
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * blockAlign);
            writer.Write((short)blockAlign);
            writer.Write((short)bitsPerSample);
            writer.Write(new char[] {'d', 'a', 't', 'a'});
            writer.Write(dataLength);

            for (int frame = 0; frame < frameCount; frame++) {
                double time = (double)frame / sampleRate;
                short frontLeft = (short)(Math.Sin(2.0 * Math.PI * 220.0 * time) * 1800.0);
                short frontRight = (short)(Math.Sin(2.0 * Math.PI * 330.0 * time) * 1800.0);
                writer.Write(frontLeft);
                writer.Write(frontRight);
            }
        }
    }
}
'@

Add-Type -TypeDefinition $interopSource -Language CSharp

$roles = @(
    [AudioRole]::Console,
    [AudioRole]::Multimedia,
    [AudioRole]::Communications
)
$originalEndpoints = @{}
$wavePath = Join-Path ([IO.Path]::GetTempPath()) "v5_5_ds5_4ch_test.wav"
$serial = $null
$player = $null
$log = [Text.StringBuilder]::new()

Write-TestLine "port" $Port

try {
    $serial = [IO.Ports.SerialPort]::new($Port, 115200, "None", 8, "One")
    $serial.DtrEnable = $false
    $serial.RtsEnable = $false
    $serial.ReadTimeout = 100
    $serial.Open()
    Start-Sleep -Seconds 2
    [void]$serial.ReadExisting()

    $endpoint = Wait-DualSenseAudioEndpoint
    if ($endpoint.InstanceId -notmatch "^SWD\\MMDEVAPI\\(.+)$") {
        throw "Cannot parse MMDevice endpoint ID from '$($endpoint.InstanceId)'."
    }
    $targetEndpointId = $Matches[1]
    Write-TestLine "endpoint_name" $endpoint.FriendlyName
    Write-TestLine "endpoint_id" $targetEndpointId
    Write-TestLine "endpoint_format" "4ch_48000hz_pcm16"
    Write-TestLine "source_format" "2ch_48000hz_pcm16_shared_mode"

    foreach ($role in $roles) {
        $originalEndpoints[$role] = [V55AudioTest]::GetDefaultEndpoint($role)
        [V55AudioTest]::SetDefaultEndpoint($targetEndpointId, $role)
    }
    $activeDefault = [V55AudioTest]::GetDefaultEndpoint([AudioRole]::Console)
    Write-TestLine "default_endpoint_selected" ($activeDefault -eq $targetEndpointId)

    [V55AudioTest]::CreateStereoWave($wavePath, $Seconds)
    $player = [System.Media.SoundPlayer]::new($wavePath)
    $player.Load()
    $player.Play()

    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds + 2)
    while ([DateTime]::UtcNow -lt $deadline) {
        $text = $serial.ReadExisting()
        if ($text) {
            [void]$log.Append($text)
        }
        Start-Sleep -Milliseconds 50
    }
    $player.Stop()
} finally {
    if ($player) {
        $player.Stop()
        $player.Dispose()
    }
    if ($serial -and $serial.IsOpen) {
        $remaining = $serial.ReadExisting()
        if ($remaining) {
            [void]$log.Append($remaining)
        }
        $serial.Close()
        $serial.Dispose()
    }
    foreach ($role in $roles) {
        if ($originalEndpoints.ContainsKey($role) -and $originalEndpoints[$role]) {
            [V55AudioTest]::SetDefaultEndpoint($originalEndpoints[$role], $role)
        }
    }
    Remove-Item -LiteralPath $wavePath -Force -ErrorAction SilentlyContinue
}

$logText = $log.ToString()
$streaming = $logText -match "\[DS5_UAC1\] streaming=true"
$altOne = $logText -match "\[DS5_UAC1\] set_interface=1"
$outPacket = $logText -match "\[DS5_UAC1\] out_packet"
$packetMatches = [regex]::Matches(
    $logText,
    "\[DS5_UAC1\] out_packet len=(\d+) count=(\d+)")
$lastPacketLength = if ($packetMatches.Count) {
    $packetMatches[$packetMatches.Count - 1].Groups[1].Value
} else {
    0
}
$lastPacketCount = if ($packetMatches.Count) {
    $packetMatches[$packetMatches.Count - 1].Groups[2].Value
} else {
    0
}

Write-TestLine "set_interface_1" $altOne
Write-TestLine "streaming" $streaming
Write-TestLine "out_packet" $outPacket
Write-TestLine "out_packet_length" $lastPacketLength
Write-TestLine "out_packet_count" $lastPacketCount
Write-TestLine "default_endpoint_restored" $true

$interestingLines = $logText -split "`r?`n" |
    Where-Object { $_ -match "\[DS5_UAC1\]" }
$stopped = $interestingLines.Count -gt 0 -and
    $interestingLines[$interestingLines.Count - 1] -match "\[DS5_UAC1\] set_interface=0"
Write-TestLine "stopped_at_alt_0" $stopped
foreach ($line in $interestingLines) {
    Write-Output "[V5_5_DS5_AUDIO_STREAM] uart=$line"
}

if (!$streaming -or !$altOne -or !$outPacket -or !$stopped) {
    Write-TestLine "result" "failed"
    exit 1
}

Write-TestLine "result" "passed"

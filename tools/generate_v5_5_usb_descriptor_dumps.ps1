param(
    [string]$ToolchainBin
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$BuildRoot = Join-Path $RepoRoot "work\build\v5_5_dualsense_identity"
$GeneratedRoot = Join-Path $RepoRoot "docs\generated"
$ElfName = "esp32s3_dualsense_identity_experiment.elf"

function Find-ToolchainBin {
    if ($ToolchainBin) {
        $resolved = (Resolve-Path -LiteralPath $ToolchainBin).Path
        return $resolved
    }

    $roots = @()
    if ($env:IDF_TOOLS_PATH) {
        $roots += $env:IDF_TOOLS_PATH
    }
    $roots += (Join-Path $env:SystemDrive "Espressif\tools")

    foreach ($root in $roots | Select-Object -Unique) {
        $toolRoot = Join-Path $root "xtensa-esp-elf"
        if (!(Test-Path -LiteralPath $toolRoot)) {
            continue
        }
        $candidates = @(Get-ChildItem -LiteralPath $toolRoot -Directory |
            Sort-Object Name -Descending |
            ForEach-Object {
                Join-Path $_.FullName "xtensa-esp-elf\bin"
            } |
            Where-Object {
                Test-Path -LiteralPath (Join-Path $_ "xtensa-esp32s3-elf-nm.exe")
            })
        if ($candidates.Count -gt 0) {
            return $candidates[0]
        }
    }

    throw "ESP32-S3 toolchain not found. Pass -ToolchainBin <xtensa-esp-elf-bin>."
}

function Get-ElfSymbolBytes {
    param(
        [string]$ElfPath,
        [string]$SymbolName,
        [string]$NmPath,
        [string]$ObjdumpPath
    )

    $symbolLine = & $NmPath -S $ElfPath |
        Where-Object { $_ -match ("\s" + [regex]::Escape($SymbolName) + "$") } |
        Select-Object -First 1
    if (!$symbolLine) {
        throw "Symbol '$SymbolName' not found in $ElfPath"
    }
    if ($symbolLine -notmatch "^\s*([0-9a-fA-F]+)\s+([0-9a-fA-F]+)\s+\w\s+") {
        throw "Cannot parse symbol line: $symbolLine"
    }

    $address = [Convert]::ToUInt64($Matches[1], 16)
    $size = [Convert]::ToInt32($Matches[2], 16)
    $stop = $address + $size
    $dump = & $ObjdumpPath -s `
        ("--start-address=0x{0:x}" -f $address) `
        ("--stop-address=0x{0:x}" -f $stop) `
        $ElfPath
    if ($LASTEXITCODE -ne 0) {
        throw "objdump failed for '$SymbolName' in $ElfPath"
    }

    $bytes = [System.Collections.Generic.List[byte]]::new()
    foreach ($line in $dump) {
        if ($line -notmatch "^\s*[0-9a-fA-F]+\s+(.+)$") {
            continue
        }
        $fields = $Matches[1] -split "\s+"
        foreach ($field in $fields) {
            if ($field -notmatch "^[0-9a-fA-F]{2,8}$" -or ($field.Length % 2) -ne 0) {
                break
            }
            for ($index = 0; $index -lt $field.Length; $index += 2) {
                if ($bytes.Count -lt $size) {
                    $bytes.Add([Convert]::ToByte($field.Substring($index, 2), 16))
                }
            }
        }
    }
    if ($bytes.Count -ne $size) {
        throw "Extracted $($bytes.Count) bytes for '$SymbolName'; expected $size."
    }
    return $bytes.ToArray()
}

function Get-CArrayHexBytes {
    param(
        [string]$SourcePath,
        [string]$ArrayDeclaration,
        [hashtable]$Replacements = @{}
    )

    $text = Get-Content -LiteralPath $SourcePath -Raw
    $start = $text.IndexOf($ArrayDeclaration, [StringComparison]::Ordinal)
    if ($start -lt 0) {
        throw "Array declaration not found: $ArrayDeclaration"
    }
    $open = $text.IndexOf("{", $start)
    $close = $text.IndexOf("};", $open, [StringComparison]::Ordinal)
    if ($open -lt 0 -or $close -lt 0) {
        throw "Array bounds not found: $ArrayDeclaration"
    }

    $body = $text.Substring($open + 1, $close - $open - 1)
    foreach ($key in $Replacements.Keys) {
        $body = $body.Replace($key, $Replacements[$key])
    }

    $active = $true
    $stack = [System.Collections.Generic.Stack[object]]::new()
    $bytes = [System.Collections.Generic.List[byte]]::new()
    foreach ($rawLine in ($body -split "`r?`n")) {
        $line = $rawLine.Trim()
        if ($line -match "^#if\s+ENABLE_SERIAL") {
            $stack.Push([pscustomobject]@{ Parent = $active; Condition = $false })
            $active = $false
            continue
        }
        if ($line -match "^#else") {
            $frame = $stack.Peek()
            $active = $frame.Parent -and !$frame.Condition
            continue
        }
        if ($line -match "^#endif") {
            $frame = $stack.Pop()
            $active = $frame.Parent
            continue
        }
        if (!$active) {
            continue
        }

        $data = ($rawLine -replace "//.*$", "")
        foreach ($match in [regex]::Matches($data, "0x([0-9a-fA-F]{1,2})")) {
            $bytes.Add([Convert]::ToByte($match.Groups[1].Value, 16))
        }
    }
    return $bytes.ToArray()
}

function Format-HexDump {
    param([byte[]]$Bytes)
    $lines = [System.Collections.Generic.List[string]]::new()
    for ($offset = 0; $offset -lt $Bytes.Length; $offset += 16) {
        $count = [Math]::Min(16, $Bytes.Length - $offset)
        $hex = ($Bytes[$offset..($offset + $count - 1)] |
            ForEach-Object { $_.ToString("X2") }) -join " "
        $lines.Add(("{0:X4}: {1}" -f $offset, $hex))
    }
    return $lines -join "`n"
}

function Get-DescriptorTypeName {
    param([int]$Type)
    switch ($Type) {
        0x01 { "DEVICE" }
        0x02 { "CONFIGURATION" }
        0x04 { "INTERFACE" }
        0x05 { "ENDPOINT" }
        0x0B { "IAD" }
        0x21 { "HID" }
        0x24 { "CS_INTERFACE" }
        0x25 { "CS_ENDPOINT" }
        default { "TYPE_0x{0:X2}" -f $Type }
    }
}

function Parse-ConfigurationDescriptor {
    param(
        [byte[]]$Bytes,
        [int]$HidReportLength,
        [int]$StringCount,
        [bool]$IadExpected,
        [int]$KnownSampleRate
    )

    if ($Bytes.Length -lt 9 -or $Bytes[1] -ne 0x02) {
        throw "Invalid configuration descriptor."
    }

    $rows = [System.Collections.Generic.List[object]]::new()
    $interfaces = [System.Collections.Generic.List[object]]::new()
    $endpoints = [System.Collections.Generic.List[object]]::new()
    $iads = [System.Collections.Generic.List[object]]::new()
    $stringIndices = [System.Collections.Generic.List[int]]::new()
    $hidDeclaredLength = $null
    $audioVersion = "none"
    $channels = 0
    $sampleRate = 0
    $currentInterface = $null
    $offset = 0

    while ($offset -lt $Bytes.Length) {
        $length = [int]$Bytes[$offset]
        if ($length -lt 2 -or ($offset + $length) -gt $Bytes.Length) {
            throw "Invalid descriptor length $length at offset $offset."
        }
        $type = [int]$Bytes[$offset + 1]
        $detail = ""

        switch ($type) {
            0x02 {
                $total = [int]$Bytes[$offset + 2] -bor ([int]$Bytes[$offset + 3] -shl 8)
                $detail = "wTotalLength=$total bNumInterfaces=$($Bytes[$offset + 4]) attributes=0x$($Bytes[$offset + 7].ToString('X2')) max_power=$([int]$Bytes[$offset + 8] * 2)mA"
                $stringIndices.Add([int]$Bytes[$offset + 6])
            }
            0x04 {
                $currentInterface = [pscustomobject]@{
                    Number = [int]$Bytes[$offset + 2]
                    Alt = [int]$Bytes[$offset + 3]
                    DeclaredEndpoints = [int]$Bytes[$offset + 4]
                    Class = [int]$Bytes[$offset + 5]
                    SubClass = [int]$Bytes[$offset + 6]
                    Protocol = [int]$Bytes[$offset + 7]
                    StringIndex = [int]$Bytes[$offset + 8]
                    ActualEndpoints = 0
                }
                $interfaces.Add($currentInterface)
                $stringIndices.Add($currentInterface.StringIndex)
                $detail = "interface=$($currentInterface.Number) alt=$($currentInterface.Alt) endpoints=$($currentInterface.DeclaredEndpoints) class=0x$($currentInterface.Class.ToString('X2')) subclass=0x$($currentInterface.SubClass.ToString('X2')) protocol=0x$($currentInterface.Protocol.ToString('X2')) iInterface=$($currentInterface.StringIndex)"
            }
            0x05 {
                $maxPacket = [int]$Bytes[$offset + 4] -bor ([int]$Bytes[$offset + 5] -shl 8)
                $endpoint = [pscustomobject]@{
                    Interface = if ($currentInterface) { $currentInterface.Number } else { -1 }
                    Alt = if ($currentInterface) { $currentInterface.Alt } else { -1 }
                    Address = [int]$Bytes[$offset + 2]
                    Attributes = [int]$Bytes[$offset + 3]
                    MaxPacket = $maxPacket
                    Interval = [int]$Bytes[$offset + 6]
                }
                if ($currentInterface) {
                    $currentInterface.ActualEndpoints++
                }
                $endpoints.Add($endpoint)
                $transfer = switch ($endpoint.Attributes -band 0x03) {
                    1 { "isochronous" }
                    2 { "bulk" }
                    3 { "interrupt" }
                    default { "control" }
                }
                $direction = if (($endpoint.Address -band 0x80) -ne 0) { "IN" } else { "OUT" }
                $detail = "ep=0x$($endpoint.Address.ToString('X2')) $direction $transfer attributes=0x$($endpoint.Attributes.ToString('X2')) max_packet=$maxPacket interval=$($endpoint.Interval)"
            }
            0x0B {
                $iad = [pscustomobject]@{
                    First = [int]$Bytes[$offset + 2]
                    Count = [int]$Bytes[$offset + 3]
                    Class = [int]$Bytes[$offset + 4]
                    SubClass = [int]$Bytes[$offset + 5]
                    Protocol = [int]$Bytes[$offset + 6]
                    StringIndex = [int]$Bytes[$offset + 7]
                }
                $iads.Add($iad)
                $stringIndices.Add($iad.StringIndex)
                $detail = "first_interface=$($iad.First) count=$($iad.Count) class=0x$($iad.Class.ToString('X2')) subclass=0x$($iad.SubClass.ToString('X2')) protocol=0x$($iad.Protocol.ToString('X2'))"
            }
            0x21 {
                $hidDeclaredLength = [int]$Bytes[$offset + 7] -bor ([int]$Bytes[$offset + 8] -shl 8)
                $detail = "bcdHID=0x$($Bytes[$offset + 3].ToString('X2'))$($Bytes[$offset + 2].ToString('X2')) report_length=$hidDeclaredLength"
            }
            0x24 {
                $subtype = [int]$Bytes[$offset + 2]
                $detail = "subtype=0x$($subtype.ToString('X2'))"
                if ($currentInterface -and $currentInterface.Class -eq 0x01 -and
                    $currentInterface.SubClass -eq 0x01 -and $subtype -eq 0x01) {
                    $bcdAdc = [int]$Bytes[$offset + 3] -bor ([int]$Bytes[$offset + 4] -shl 8)
                    $audioVersion = if ($bcdAdc -ge 0x0200) { "UAC2" } else { "UAC1" }
                    $detail += " bcdADC=0x$($bcdAdc.ToString('X4'))"
                }
                if ($currentInterface -and $currentInterface.Class -eq 0x01 -and
                    $currentInterface.SubClass -eq 0x02 -and $subtype -eq 0x02) {
                    if ($length -ge 11) {
                        $channels = [int]$Bytes[$offset + 4]
                        $sampleRate = [int]$Bytes[$offset + 8] -bor
                            ([int]$Bytes[$offset + 9] -shl 8) -bor
                            ([int]$Bytes[$offset + 10] -shl 16)
                        $detail += " channels=$channels bits=$($Bytes[$offset + 6]) sample_rate=$sampleRate"
                    } elseif ($length -ge 6) {
                        $detail += " subslot_bytes=$($Bytes[$offset + 4]) bits=$($Bytes[$offset + 5])"
                    }
                }
                if ($audioVersion -eq "UAC2" -and $currentInterface -and
                    $currentInterface.SubClass -eq 0x02 -and $subtype -eq 0x01 -and
                    $length -ge 16) {
                    $channels = [int]$Bytes[$offset + 10]
                    $detail += " channels=$channels"
                }
            }
            0x25 {
                $detail = "subtype=0x$($Bytes[$offset + 2].ToString('X2'))"
            }
        }

        $rows.Add([pscustomobject]@{
            Offset = $offset
            Length = $length
            Type = Get-DescriptorTypeName -Type $type
            Detail = $detail
        })
        $offset += $length
    }

    if ($sampleRate -eq 0 -and $audioVersion -ne "none") {
        $sampleRate = $KnownSampleRate
    }
    $uniqueInterfaces = @($interfaces | Select-Object -ExpandProperty Number -Unique | Sort-Object)
    $expectedInterfaces = if ($uniqueInterfaces.Count -eq 0) { @() } else { @(0..($uniqueInterfaces.Count - 1)) }
    $interfaceContinuity = (($uniqueInterfaces -join ",") -eq ($expectedInterfaces -join ","))
    $endpointCountsValid = @($interfaces | Where-Object {
        $_.DeclaredEndpoints -ne $_.ActualEndpoints
    }).Count -eq 0
    $endpointKeys = @($endpoints | ForEach-Object {
        "{0}:{1}:0x{2:X2}" -f $_.Interface, $_.Alt, $_.Address
    })
    $endpointConflict = @($endpointKeys | Group-Object | Where-Object Count -gt 1).Count -gt 0
    $maxStringIndex = if ($stringIndices.Count -gt 0) {
        ($stringIndices | Measure-Object -Maximum).Maximum
    } else {
        0
    }
    $declaredTotal = [int]$Bytes[2] -bor ([int]$Bytes[3] -shl 8)
    $declaredInterfaceCount = [int]$Bytes[4]
    $iadCoverageValid = $true
    foreach ($iad in $iads) {
        $covered = @($iad.First..($iad.First + $iad.Count - 1))
        if (@($covered | Where-Object { $_ -notin $uniqueInterfaces }).Count -gt 0) {
            $iadCoverageValid = $false
        }
    }

    return [pscustomobject]@{
        Rows = $rows
        Interfaces = $interfaces
        Endpoints = $endpoints
        Iads = $iads
        ActualLength = $Bytes.Length
        DeclaredTotal = $declaredTotal
        DeclaredInterfaceCount = $declaredInterfaceCount
        UniqueInterfaceCount = $uniqueInterfaces.Count
        InterfaceContinuity = $interfaceContinuity
        EndpointCountsValid = $endpointCountsValid
        EndpointConflict = $endpointConflict
        IadPresent = $iads.Count -gt 0
        IadExpected = $IadExpected
        IadMatchesExpected = (($iads.Count -gt 0) -eq $IadExpected)
        IadCoverageValid = $iadCoverageValid
        HidDeclaredLength = $hidDeclaredLength
        HidActualLength = $HidReportLength
        HidLengthValid = ($hidDeclaredLength -eq $HidReportLength)
        StringCount = $StringCount
        MaxStringIndex = $maxStringIndex
        StringIndicesValid = ($maxStringIndex -lt $StringCount)
        AudioVersion = $audioVersion
        Channels = $channels
        SampleRate = $sampleRate
        TotalLengthValid = ($declaredTotal -eq $Bytes.Length)
        InterfaceCountValid = ($declaredInterfaceCount -eq $uniqueInterfaces.Count)
    }
}

function Format-DeviceDescriptor {
    param([byte[]]$Bytes)
    if ($Bytes.Length -ne 18) {
        throw "Device descriptor must be 18 bytes; got $($Bytes.Length)."
    }
    $vid = [int]$Bytes[8] -bor ([int]$Bytes[9] -shl 8)
    $productId = [int]$Bytes[10] -bor ([int]$Bytes[11] -shl 8)
    $bcdUsb = [int]$Bytes[2] -bor ([int]$Bytes[3] -shl 8)
    $bcdDevice = [int]$Bytes[12] -bor ([int]$Bytes[13] -shl 8)
    return @"
| Field | Value |
| --- | --- |
| bcdUSB | ``0x$($bcdUsb.ToString("X4"))`` |
| bDeviceClass/SubClass/Protocol | ``0x$($Bytes[4].ToString("X2")) / 0x$($Bytes[5].ToString("X2")) / 0x$($Bytes[6].ToString("X2"))`` |
| bMaxPacketSize0 | $($Bytes[7]) |
| VID/PID | ``0x$($vid.ToString("X4")) / 0x$($productId.ToString("X4"))`` |
| bcdDevice | ``0x$($bcdDevice.ToString("X4"))`` |
| iManufacturer/iProduct/iSerial | $($Bytes[14]) / $($Bytes[15]) / $($Bytes[16]) |
| bNumConfigurations | $($Bytes[17]) |
"@
}

function Format-ParsedDescriptorTable {
    param([object]$Parsed)
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("| Offset | Length | Type | Detail |")
    $lines.Add("| ---: | ---: | --- | --- |")
    foreach ($row in $Parsed.Rows) {
        $lines.Add("| ``0x$($row.Offset.ToString('X4'))`` | $($row.Length) | $($row.Type) | $($row.Detail) |")
    }
    return $lines -join "`n"
}

function Format-ValidationTable {
    param([object]$Parsed)
    return @"
| Check | Result |
| --- | --- |
| wTotalLength | declared=$($Parsed.DeclaredTotal), actual=$($Parsed.ActualLength), valid=$($Parsed.TotalLengthValid.ToString().ToLowerInvariant()) |
| bNumInterfaces | declared=$($Parsed.DeclaredInterfaceCount), unique=$($Parsed.UniqueInterfaceCount), valid=$($Parsed.InterfaceCountValid.ToString().ToLowerInvariant()) |
| interface continuity | $($Parsed.InterfaceContinuity.ToString().ToLowerInvariant()) |
| interface endpoint counts | $($Parsed.EndpointCountsValid.ToString().ToLowerInvariant()) |
| duplicate endpoint in same interface/alt | $($Parsed.EndpointConflict.ToString().ToLowerInvariant()) |
| IAD | present=$($Parsed.IadPresent.ToString().ToLowerInvariant()), expected=$($Parsed.IadExpected.ToString().ToLowerInvariant()), valid=$($Parsed.IadMatchesExpected.ToString().ToLowerInvariant()) |
| IAD interface coverage | $($Parsed.IadCoverageValid.ToString().ToLowerInvariant()) |
| HID report length | declared=$($Parsed.HidDeclaredLength), actual=$($Parsed.HidActualLength), valid=$($Parsed.HidLengthValid.ToString().ToLowerInvariant()) |
| string indices | max=$($Parsed.MaxStringIndex), descriptor_count=$($Parsed.StringCount), valid=$($Parsed.StringIndicesValid.ToString().ToLowerInvariant()) |
| audio | version=$($Parsed.AudioVersion), channels=$($Parsed.Channels), sample_rate=$($Parsed.SampleRate) |
"@
}

function New-ProfileSection {
    param(
        [object]$Profile,
        [string]$NmPath,
        [string]$ObjdumpPath
    )
    $elfPath = Join-Path (Join-Path $BuildRoot $Profile.Name) $ElfName
    if (!(Test-Path -LiteralPath $elfPath)) {
        throw "Build missing for profile '$($Profile.Name)': $elfPath"
    }
    $device = Get-ElfSymbolBytes -ElfPath $elfPath `
        -SymbolName "s_ds5_device_descriptor" -NmPath $NmPath -ObjdumpPath $ObjdumpPath
    $config = Get-ElfSymbolBytes -ElfPath $elfPath `
        -SymbolName "s_ds5_configuration_descriptor" -NmPath $NmPath -ObjdumpPath $ObjdumpPath
    $hid = Get-ElfSymbolBytes -ElfPath $elfPath `
        -SymbolName "s_ds5_hid_report_descriptor" -NmPath $NmPath -ObjdumpPath $ObjdumpPath
    $parsed = Parse-ConfigurationDescriptor -Bytes $config `
        -HidReportLength $hid.Length `
        -StringCount $Profile.StringCount `
        -IadExpected $Profile.IadExpected `
        -KnownSampleRate $Profile.SampleRate

    return @"
## ``$($Profile.Name)``

~~~text
serial=$($Profile.Serial)
build=work/build/v5_5_dualsense_identity/$($Profile.Name)/$ElfName
device_class_hint=$($Profile.DeviceClassHint)
iad_expected=$($Profile.IadExpected.ToString().ToLowerInvariant())
~~~

### Device Descriptor

$(Format-DeviceDescriptor -Bytes $device)

~~~text
$(Format-HexDump -Bytes $device)
~~~

### Configuration Descriptor

~~~text
$(Format-HexDump -Bytes $config)
~~~

$(Format-ParsedDescriptorTable -Parsed $parsed)

### HID Report Descriptor

~~~text
$(Format-HexDump -Bytes $hid)
~~~

### Validation

$(Format-ValidationTable -Parsed $parsed)
"@
}

function Write-GeneratedFile {
    param(
        [string]$RelativePath,
        [string]$Content
    )
    $path = Join-Path $RepoRoot $RelativePath
    $parent = Split-Path -Parent $path
    if (!(Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }
    Set-Content -LiteralPath $path -Value $Content -Encoding utf8
    Write-Output "[V5_5_DESCRIPTOR_DUMP] wrote=$RelativePath"
}

$toolBin = Find-ToolchainBin
$nm = Join-Path $toolBin "xtensa-esp32s3-elf-nm.exe"
$objdump = Join-Path $toolBin "xtensa-esp32s3-elf-objdump.exe"
Write-Output "[V5_5_DESCRIPTOR_DUMP] toolchain_bin=$toolBin"

$profiles = @(
    [pscustomobject]@{ Name = "hid_only"; Serial = "V55HIDONLY"; DeviceClassHint = "00/00/00"; IadExpected = $false; StringCount = 5; SampleRate = 0 },
    [pscustomobject]@{ Name = "hid_composite_dummy_interface_class_00"; Serial = "V55DUMMY00"; DeviceClassHint = "00/00/00"; IadExpected = $false; StringCount = 5; SampleRate = 0 },
    [pscustomobject]@{ Name = "hid_composite_dummy_interface_class_ef"; Serial = "V55DUMMYEF"; DeviceClassHint = "EF/02/01"; IadExpected = $false; StringCount = 5; SampleRate = 0 },
    [pscustomobject]@{ Name = "hid_audio_control_only"; Serial = "V55ACONLY"; DeviceClassHint = "00/00/00"; IadExpected = $false; StringCount = 6; SampleRate = 48000 },
    [pscustomobject]@{ Name = "hid_audio_streaming_alt0_only"; Serial = "V55ASALT0"; DeviceClassHint = "00/00/00"; IadExpected = $false; StringCount = 6; SampleRate = 48000 },
    [pscustomobject]@{ Name = "hid_audio_uac1_2ch"; Serial = "V55UAC1_2CH"; DeviceClassHint = "00/00/00"; IadExpected = $false; StringCount = 6; SampleRate = 48000 },
    [pscustomobject]@{ Name = "hid_audio_uac1_4ch_ds5like"; Serial = "V55UAC1_4CH"; DeviceClassHint = "00/00/00"; IadExpected = $false; StringCount = 6; SampleRate = 48000 },
    [pscustomobject]@{ Name = "hid_audio_uac2_2ch"; Serial = "V55UAC2_2CH"; DeviceClassHint = "EF/02/01"; IadExpected = $true; StringCount = 6; SampleRate = 48000 },
    [pscustomobject]@{ Name = "hid_audio_uac2_4ch"; Serial = "V55UAC2_4CH"; DeviceClassHint = "EF/02/01"; IadExpected = $true; StringCount = 6; SampleRate = 48000 }
)

$docGroups = @(
    [pscustomobject]@{ File = "docs/generated/v5_5_usb_descriptor_dump_hid_only.md"; Names = @("hid_only") },
    [pscustomobject]@{ File = "docs/generated/v5_5_usb_descriptor_dump_hid_composite_dummy_interface.md"; Names = @("hid_composite_dummy_interface_class_00", "hid_composite_dummy_interface_class_ef") },
    [pscustomobject]@{ File = "docs/generated/v5_5_usb_descriptor_dump_hid_audio_control_only.md"; Names = @("hid_audio_control_only") },
    [pscustomobject]@{ File = "docs/generated/v5_5_usb_descriptor_dump_hid_audio_streaming_alt0_only.md"; Names = @("hid_audio_streaming_alt0_only") },
    [pscustomobject]@{ File = "docs/generated/v5_5_usb_descriptor_dump_hid_audio_uac1_2ch.md"; Names = @("hid_audio_uac1_2ch") },
    [pscustomobject]@{ File = "docs/generated/v5_5_usb_descriptor_dump_hid_audio_uac1_4ch_ds5like.md"; Names = @("hid_audio_uac1_4ch_ds5like") },
    [pscustomobject]@{ File = "docs/generated/v5_5_usb_descriptor_dump_hid_audio_uac2_2ch.md"; Names = @("hid_audio_uac2_2ch") },
    [pscustomobject]@{ File = "docs/generated/v5_5_usb_descriptor_dump_hid_audio_uac2_4ch.md"; Names = @("hid_audio_uac2_4ch") }
)

foreach ($group in $docGroups) {
    $sections = foreach ($name in $group.Names) {
        $profile = $profiles | Where-Object Name -eq $name | Select-Object -First 1
        New-ProfileSection -Profile $profile -NmPath $nm -ObjdumpPath $objdump
    }
    $title = [IO.Path]::GetFileNameWithoutExtension($group.File) -replace "_", " "
    $content = @"
# $title

Date: 2026-06-06

Generated from compiled ELF symbols by:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\generate_v5_5_usb_descriptor_dumps.ps1
~~~

$($sections -join "`n`n")
"@
    Write-GeneratedFile -RelativePath $group.File -Content $content
}

$upstreamSource = Join-Path $RepoRoot "research\upstream\DS5Dongle\src\usb_descriptors.cpp"
$upstreamCommit = (& git -C (Join-Path $RepoRoot "research\upstream\DS5Dongle") rev-parse HEAD).Trim()
$upstreamConfig = Get-CArrayHexBytes -SourcePath $upstreamSource `
    -ArrayDeclaration "uint8_t descriptor_configuration[]" `
    -Replacements @{
        "U16_TO_U8S_LE(CONFIG_DESC_LEN_TOTAL)" = "0xE3, 0x00"
        "ITF_NUM_TOTAL" = "0x04"
    }
$upstreamHid = Get-CArrayHexBytes -SourcePath $upstreamSource `
    -ArrayDeclaration "uint8_t const desc_hid_report_ds[]"
$upstreamDevice = [byte[]]@(
    0x12, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x40,
    0x4C, 0x05, 0xE6, 0x0C, 0x00, 0x01, 0x01, 0x02,
    0x00, 0x01
)
$langString = [byte[]]@(0x04, 0x03, 0x09, 0x04)
$manufacturerBody = [Text.Encoding]::Unicode.GetBytes("Sony Interactive Entertainment")
$manufacturerStringBytes = [System.Collections.Generic.List[byte]]::new()
$manufacturerStringBytes.Add([byte]($manufacturerBody.Length + 2))
$manufacturerStringBytes.Add(0x03)
$manufacturerStringBytes.AddRange($manufacturerBody)
$manufacturerString = $manufacturerStringBytes.ToArray()
$productBody = [Text.Encoding]::Unicode.GetBytes("DualSense Wireless Controller")
$productStringBytes = [System.Collections.Generic.List[byte]]::new()
$productStringBytes.Add([byte]($productBody.Length + 2))
$productStringBytes.Add(0x03)
$productStringBytes.AddRange($productBody)
$productString = $productStringBytes.ToArray()
if ($upstreamConfig.Length -ne 227) {
    throw "DS5Dongle default configuration length is $($upstreamConfig.Length); expected 227."
}
if ($upstreamHid.Length -ne 321) {
    throw "DS5Dongle DS report descriptor length is $($upstreamHid.Length); expected 321."
}
$upstreamParsed = Parse-ConfigurationDescriptor -Bytes $upstreamConfig `
    -HidReportLength $upstreamHid.Length `
    -StringCount 4 `
    -IadExpected $false `
    -KnownSampleRate 48000

$reference = @"
# V5.5 DS5Dongle USB Descriptor Reference

Date: 2026-06-06

Source:

~~~text
repository=research/upstream/DS5Dongle
commit=$upstreamCommit
file=src/usb_descriptors.cpp
configuration=default ENABLE_SERIAL=OFF, DualSense mode
~~~

The default final descriptor is UAC1 with Audio Control, four-channel Audio
Streaming OUT, two-channel Audio Streaming IN, and HID. It does not emit an
IAD and uses device class ``00/00/00``. DS5Dongle only switches to device class
``EF/02/01`` and adds the Audio IAD when ``ENABLE_SERIAL=ON`` also adds CDC.

## Device Descriptor

$(Format-DeviceDescriptor -Bytes $upstreamDevice)

~~~text
$(Format-HexDump -Bytes $upstreamDevice)
~~~

## Configuration Descriptor

~~~text
$(Format-HexDump -Bytes $upstreamConfig)
~~~

$(Format-ParsedDescriptorTable -Parsed $upstreamParsed)

## HID Report Descriptor

~~~text
$(Format-HexDump -Bytes $upstreamHid)
~~~

## String Descriptors

| Index | Value | Source |
| ---: | --- | --- |
| 0 | language ``0x0409`` | fixed |
| 1 | ``Sony Interactive Entertainment`` | fixed |
| 2 | ``DualSense Wireless Controller`` | selected dynamically in DualSense mode |
| 3 | board USB serial | generated dynamically by ``board_usb_get_serial`` |

~~~text
index_0:
$(Format-HexDump -Bytes $langString)

index_1:
$(Format-HexDump -Bytes $manufacturerString)

index_2:
$(Format-HexDump -Bytes $productString)

index_3:
runtime-generated; no fixed raw byte sequence in the upstream source
~~~

## Validation

$(Format-ValidationTable -Parsed $upstreamParsed)

## Final Topology

| Interface | Function | Endpoints |
| ---: | --- | --- |
| 0 | UAC1 Audio Control | none |
| 1 alt 0/1 | UAC1 Audio Streaming OUT, 4ch, 16-bit, 48 kHz | ``0x01`` adaptive isoch, max 392 |
| 2 alt 0/1 | UAC1 Audio Streaming IN, 2ch, 16-bit, 48 kHz | ``0x82`` asynchronous isoch, max 196 |
| 3 | DualSense HID | ``0x84`` interrupt IN and ``0x03`` interrupt OUT, 64 bytes |

~~~text
wTotalLength=227
bNumInterfaces=4
audio_control_total_length=73
hid_report_descriptor_length=321
iad_present=false
device_class=00/00/00
~~~
"@
Write-GeneratedFile -RelativePath "docs/generated/v5_5_ds5dongle_usb_descriptor_reference.md" -Content $reference

Write-Output "[V5_5_DESCRIPTOR_DUMP] result=passed"

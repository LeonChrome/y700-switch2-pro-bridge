param(
    [Parameter(Mandatory = $true)][string]$Path,
    [uint64]$StartSequence = 0,
    [uint64]$EndSequence = [uint64]::MaxValue,
    [ValidateSet('X', 'Y', 'Z')][string]$MainAxis,
    [double]$ReferenceAngleDeg = 0,
    [ValidateRange(1.0, 180.0)][double]$MinimumReferenceAngleDeg = 45.0,
    [ValidateRange(0, 10000000)][int]$TailLines = 0
)

$ErrorActionPreference = 'Stop'
$rows = [System.Collections.Generic.List[object]]::new()
if ([IO.Path]::GetExtension($Path).Equals('.csv', [StringComparison]::OrdinalIgnoreCase)) {
    $sequence = 0
    Import-Csv -LiteralPath $Path | ForEach-Object {
        if ($sequence -ge $StartSequence -and $sequence -le $EndSequence) {
            $rows.Add([pscustomobject]@{
                Sequence = [uint64]$sequence
                MotionTimestamp = [uint32]$_.motion_timestamp
                AccelX = [int16]$_.accel_x
                AccelY = [int16]$_.accel_y
                AccelZ = [int16]$_.accel_z
                GyroX = [int16]$_.gyro_x
                GyroY = [int16]$_.gyro_y
                GyroZ = [int16]$_.gyro_z
            })
        }
        $sequence++
    }
} else {
    $inputLines = if ($TailLines -gt 0) {
        Get-Content -LiteralPath $Path -Tail $TailLines
    } else {
        Get-Content -LiteralPath $Path
    }
    $inputLines | ForEach-Object {
        try {
            $json = $_ | ConvertFrom-Json
            if ($json.type -ne 'frame') { return }
            $sequence = [uint64]$json.frame.FrameIndex
            if ($sequence -lt $StartSequence -or $sequence -gt $EndSequence) { return }
            $raw = [Convert]::FromHexString([string]$json.frame.RawFd2Hex)
            $rows.Add([pscustomobject]@{
                Sequence = $sequence
                MotionTimestamp = [uint32][BitConverter]::ToUInt32($raw, 42)
                AccelX = [int16][BitConverter]::ToInt16($raw, 48)
                AccelY = [int16][BitConverter]::ToInt16($raw, 50)
                AccelZ = [int16][BitConverter]::ToInt16($raw, 52)
                GyroX = [int16][BitConverter]::ToInt16($raw, 54)
                GyroY = [int16][BitConverter]::ToInt16($raw, 56)
                GyroZ = [int16][BitConverter]::ToInt16($raw, 58)
            })
        } catch {
            # The live recorder may leave one incomplete JSON line in a copied snapshot.
        }
    }
}

if ($rows.Count -lt 100) { throw "FD2 rotation window has too few rows: $($rows.Count)" }

$timestampSteps = [System.Collections.Generic.List[double]]::new()
for ($index = 1; $index -lt $rows.Count; $index++) {
    $before = [uint64]$rows[$index - 1].MotionTimestamp
    $after = [uint64]$rows[$index].MotionTimestamp
    $delta = if ($after -ge $before) { $after - $before } else { 4294967296 + $after - $before }
    if ($delta -gt 0 -and $delta -lt 100000) { $timestampSteps.Add([double]$delta) }
}
$sortedSteps = @($timestampSteps | Sort-Object)
$medianStepUs = $sortedSteps[[int]($sortedSteps.Count / 2)]
$sourceRateHz = 1000000.0 / $medianStepUs
$maximumInactiveGapSamples = [Math]::Max(2, [int][Math]::Ceiling($sourceRateHz * 0.06))
$minimumActiveSamples = [Math]::Max(12, [int][Math]::Floor($sourceRateHz * 0.55))
$stationaryFarSamples = [Math]::Max(12, [int][Math]::Round($sourceRateHz * 0.55))
$stationaryNearSamples = [Math]::Max(4, [int][Math]::Round($sourceRateHz * 0.18))

function Median([string]$Name) {
    $values = @($rows | ForEach-Object { [double]($_.$Name) } | Sort-Object)
    return $values[[int]($values.Count / 2)]
}

$axisNames = @('GyroX', 'GyroY', 'GyroZ')
$mainIndex = @{ X = 0; Y = 1; Z = 2 }[$MainAxis]
$globalBias = @((Median 'GyroX'), (Median 'GyroY'), (Median 'GyroZ'))
$active = [System.Collections.Generic.List[int]]::new()
for ($index = 0; $index -lt $rows.Count; $index++) {
    $dx = [double]$rows[$index].GyroX - $globalBias[0]
    $dy = [double]$rows[$index].GyroY - $globalBias[1]
    $dz = [double]$rows[$index].GyroZ - $globalBias[2]
    if ([Math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz) -gt 30) { $active.Add($index) }
}

$segments = [System.Collections.Generic.List[object]]::new()
if ($active.Count -gt 0) {
    $start = $active[0]
    $previous = $start
    foreach ($index in ($active | Select-Object -Skip 1)) {
        if ($index - $previous -gt $maximumInactiveGapSamples) {
            if ($previous - $start -ge $minimumActiveSamples) { $segments.Add(@($start, $previous)) }
            $start = $index
        }
        $previous = $index
    }
    if ($previous - $start -ge $minimumActiveSamples) { $segments.Add(@($start, $previous)) }
}

$accelMatrix = @(
    @(0.0002442977796, -0.0000001354957791, 0.00000002645982992),
    @(-0.0000001354957791, 0.0002443118997, -0.00000072629197),
    @(0.00000002645982992, -0.00000072629197, 0.0002445054679)
)
$accelBias = @(3.3312, -1.7768, 96.4045)

function GravityVector([int]$First, [int]$Last) {
    $window = $rows[$First..$Last]
    $raw = @(
        (($window | Measure-Object AccelX -Average).Average),
        (($window | Measure-Object AccelY -Average).Average),
        (($window | Measure-Object AccelZ -Average).Average)
    )
    $output = @(0.0, 0.0, 0.0)
    for ($row = 0; $row -lt 3; $row++) {
        for ($column = 0; $column -lt 3; $column++) {
            $output[$row] += $accelMatrix[$row][$column] * ($raw[$column] - $accelBias[$column])
        }
    }
    return ,$output
}

function Integrate([int]$Start, [int]$End, [double[]]$BiasStart, [double[]]$BiasEnd) {
    $integrals = @(0.0, 0.0, 0.0)
    for ($index = $Start + 1; $index -le $End; $index++) {
        $beforeTimestamp = [uint64]$rows[$index - 1].MotionTimestamp
        $afterTimestamp = [uint64]$rows[$index].MotionTimestamp
        $deltaUs = if ($afterTimestamp -ge $beforeTimestamp) {
            $afterTimestamp - $beforeTimestamp
        } else {
            4294967296 + $afterTimestamp - $beforeTimestamp
        }
        if ($deltaUs -eq 0 -or $deltaUs -gt 150000) { continue }
        $dt = $deltaUs / 1000000.0
        for ($axis = 0; $axis -lt 3; $axis++) {
            $fractionA = ($index - 1 - $Start) / ($End - $Start)
            $fractionB = ($index - $Start) / ($End - $Start)
            $biasA = $BiasStart[$axis] + ($BiasEnd[$axis] - $BiasStart[$axis]) * $fractionA
            $biasB = $BiasStart[$axis] + ($BiasEnd[$axis] - $BiasStart[$axis]) * $fractionB
            $valueA = [double]$rows[$index - 1].($axisNames[$axis]) - $biasA
            $valueB = [double]$rows[$index].($axisNames[$axis]) - $biasB
            $integrals[$axis] += (($valueA + $valueB) / 2.0) * $dt
        }
    }
    return ,$integrals
}

$results = [System.Collections.Generic.List[object]]::new()
$pulseNumber = 0
foreach ($segment in $segments) {
    $pulseNumber++
    $preFirst = $segment[0] - $stationaryFarSamples
    $preLast = $segment[0] - $stationaryNearSamples
    $postFirst = $segment[1] + $stationaryNearSamples
    $postLast = $segment[1] + $stationaryFarSamples
    if ($preFirst -lt 0 -or $postLast -ge $rows.Count) { continue }

    $gravityBefore = GravityVector $preFirst $preLast
    $gravityAfter = GravityVector $postFirst $postLast
    $dot = $gravityBefore[0] * $gravityAfter[0] + $gravityBefore[1] * $gravityAfter[1] + $gravityBefore[2] * $gravityAfter[2]
    $normBefore = [Math]::Sqrt(($gravityBefore | ForEach-Object { $_ * $_ } | Measure-Object -Sum).Sum)
    $normAfter = [Math]::Sqrt(($gravityAfter | ForEach-Object { $_ * $_ } | Measure-Object -Sum).Sum)
    $cosine = [Math]::Max(-1.0, [Math]::Min(1.0, $dot / ($normBefore * $normAfter)))
    $gravityAngle = [Math]::Acos($cosine) * 180.0 / [Math]::PI
    $angle = if ($ReferenceAngleDeg -gt 0) { $ReferenceAngleDeg } else { $gravityAngle }

    $preWindow = $rows[$preFirst..$preLast]
    $postWindow = $rows[$postFirst..$postLast]
    $localBefore = @()
    $localAfter = @()
    foreach ($name in $axisNames) {
        $localBefore += (($preWindow | Measure-Object $name -Average).Average)
        $localAfter += (($postWindow | Measure-Object $name -Average).Average)
    }
    $integrationStart = [int](($preFirst + $preLast) / 2)
    $integrationEnd = [int](($postFirst + $postLast) / 2)
    $localIntegral = Integrate $integrationStart $integrationEnd $localBefore $localAfter
    $globalIntegral = Integrate $integrationStart $integrationEnd $globalBias $globalBias
    $validReferencePulse =
        $angle -gt $MinimumReferenceAngleDeg -and
        ($ReferenceAngleDeg -le 0 -or [Math]::Abs($globalIntegral[$mainIndex]) -gt $angle * 8.0)
    $scale = if ($validReferencePulse) { [Math]::Abs($globalIntegral[$mainIndex]) / $angle } else { $null }
    $vectorScale = if ($validReferencePulse) {
        [Math]::Sqrt(
            $globalIntegral[0] * $globalIntegral[0] +
            $globalIntegral[1] * $globalIntegral[1] +
            $globalIntegral[2] * $globalIntegral[2]) / $angle
    } else { $null }
    $results.Add([pscustomobject]@{
        Pulse = $pulseNumber
        GravityBeforeX = $gravityBefore[0]
        GravityBeforeY = $gravityBefore[1]
        GravityBeforeZ = $gravityBefore[2]
        GravityAfterX = $gravityAfter[0]
        GravityAfterY = $gravityAfter[1]
        GravityAfterZ = $gravityAfter[2]
        GravityAngleDeg = $gravityAngle
        ReferenceAngleDeg = $angle
        GlobalRawIntegralX = $globalIntegral[0]
        GlobalRawIntegralY = $globalIntegral[1]
        GlobalRawIntegralZ = $globalIntegral[2]
        LocalRawIntegralX = $localIntegral[0]
        LocalRawIntegralY = $localIntegral[1]
        LocalRawIntegralZ = $localIntegral[2]
        MainRawPerDps = $scale
        VectorRawPerDps = $vectorScale
    })
}

$validScales = @($results | Where-Object { $null -ne $_.MainRawPerDps } | ForEach-Object MainRawPerDps | Sort-Object)
$validVectorScales = @($results | Where-Object { $null -ne $_.VectorRawPerDps } | ForEach-Object VectorRawPerDps | Sort-Object)
$mean = if ($validScales.Count) { ($validScales | Measure-Object -Average).Average } else { 0 }
$median = if (!$validScales.Count) { 0 } elseif ($validScales.Count % 2) {
    $validScales[[int]($validScales.Count / 2)]
} else {
    ($validScales[$validScales.Count / 2 - 1] + $validScales[$validScales.Count / 2]) / 2
}
$vectorMean = if ($validVectorScales.Count) { ($validVectorScales | Measure-Object -Average).Average } else { 0 }
$vectorMedian = if (!$validVectorScales.Count) { 0 } elseif ($validVectorScales.Count % 2) {
    $validVectorScales[[int]($validVectorScales.Count / 2)]
} else {
    ($validVectorScales[$validVectorScales.Count / 2 - 1] + $validVectorScales[$validVectorScales.Count / 2]) / 2
}

[pscustomobject]@{
    Path = (Resolve-Path $Path).Path
    StartSequence = $StartSequence
    EndSequence = $EndSequence
    Frames = $rows.Count
    SourceRateHz = $sourceRateHz
    GlobalBiasRaw = $globalBias
    Segments = $segments.Count
    MainAxis = $MainAxis
    ValidScaleSamples = $validScales.Count
    MainRawPerDpsMean = $mean
    MainRawPerDpsMedian = $median
    VectorRawPerDpsMean = $vectorMean
    VectorRawPerDpsMedian = $vectorMedian
    Pulses = $results
} | ConvertTo-Json -Depth 6

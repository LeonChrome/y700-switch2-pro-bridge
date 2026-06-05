param(
    [string]$JavaHome = "",
    [string]$AndroidSdk = ""
)

$ErrorActionPreference = "Stop"

function Resolve-JavaHome {
    param([string]$Path)
    if ($Path -and (Test-Path -LiteralPath $Path)) { return (Resolve-Path $Path).Path }
    if ($env:JAVA_HOME -and (Test-Path -LiteralPath $env:JAVA_HOME)) { return (Resolve-Path $env:JAVA_HOME).Path }
    $androidStudioJbr = Join-Path $env:ProgramFiles "Android\Android Studio\jbr"
    if (Test-Path -LiteralPath $androidStudioJbr) { return $androidStudioJbr }
    $javac = Get-Command javac -ErrorAction SilentlyContinue
    if ($javac) { return (Split-Path -Parent (Split-Path -Parent $javac.Source)) }
    throw "Missing Java. Set JAVA_HOME or pass -JavaHome."
}

function Resolve-AndroidSdk {
    param([string]$Path)
    $candidates = @(
        $Path,
        $env:ANDROID_HOME,
        $env:ANDROID_SDK_ROOT,
        (Join-Path $env:LOCALAPPDATA "Android\Sdk")
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path $candidate).Path
        }
    }
    throw "Missing Android SDK. Set ANDROID_HOME/ANDROID_SDK_ROOT or pass -AndroidSdk."
}

function Resolve-LatestSdkFile {
    param([string]$Root, [string]$Filter, [string]$Description)
    $file = Get-ChildItem -LiteralPath $Root -Recurse -Filter $Filter -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (!$file) { throw "Missing $Description under $Root" }
    return $file.FullName
}

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$JavaHome = Resolve-JavaHome $JavaHome
$AndroidSdk = Resolve-AndroidSdk $AndroidSdk
$Javac = Join-Path $JavaHome "bin\javac.exe"
$JarTool = Join-Path $JavaHome "bin\jar.exe"
$D8 = Resolve-LatestSdkFile (Join-Path $AndroidSdk "build-tools") "d8.bat" "d8.bat"
$AndroidJar = Resolve-LatestSdkFile (Join-Path $AndroidSdk "platforms") "android.jar" "android.jar"
$Src = Join-Path $Root "src\Switch2BleBridge.java"
$Build = Join-Path $Root "build\switch2_ble_bridge"
$Classes = Join-Path $Build "classes"
$Dex = Join-Path $Build "dex"
$Jar = Join-Path $Root "switch2_ble_bridge.jar"

if (!(Test-Path -LiteralPath $Javac)) { throw "Missing javac: $Javac" }
if (!(Test-Path -LiteralPath $JarTool)) { throw "Missing jar: $JarTool" }
if (!(Test-Path -LiteralPath $D8)) { throw "Missing d8: $D8" }
if (!(Test-Path -LiteralPath $AndroidJar)) { throw "Missing Android SDK jar: $AndroidJar" }
if (!(Test-Path -LiteralPath $Src)) { throw "Missing source: $Src" }

Remove-Item -LiteralPath $Build -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $Jar -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Classes, $Dex | Out-Null

& $Javac -source 8 -target 8 -Xlint:-options -cp $AndroidJar -d $Classes $Src
if ($LASTEXITCODE -ne 0) { throw "javac failed: $LASTEXITCODE" }

$env:JAVA_HOME = $JavaHome
$env:Path = (Join-Path $JavaHome "bin") + ";" + $env:Path
$ClassFiles = Get-ChildItem -LiteralPath $Classes -Recurse -Filter "Switch2BleBridge*.class" |
    ForEach-Object { $_.FullName }
& $D8 --release --min-api 26 --lib $AndroidJar --output $Dex @ClassFiles
if ($LASTEXITCODE -ne 0) { throw "d8 failed: $LASTEXITCODE" }

Push-Location $Dex
try {
    & $JarTool cf $Jar classes.dex
    if ($LASTEXITCODE -ne 0) { throw "jar failed: $LASTEXITCODE" }
} finally {
    Pop-Location
}
Write-Host "Built $Jar"

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$JavaHome = "C:\Program Files\Android\Android Studio\jbr"
$Javac = Join-Path $JavaHome "bin\javac.exe"
$JarTool = Join-Path $JavaHome "bin\jar.exe"
$D8 = "C:\Users\leon\AppData\Local\Android\Sdk\build-tools\36.1.0\d8.bat"
$AndroidJar = "C:\Users\leon\AppData\Local\Android\Sdk\platforms\android-36.1\android.jar"
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

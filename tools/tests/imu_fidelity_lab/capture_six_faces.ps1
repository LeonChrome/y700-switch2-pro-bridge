param(
    [Parameter(Mandatory = $true)]
    [int]$PathIndex,

    [string]$OutputDirectory = "work\imu_fidelity_lab\six-face",

    [ValidateRange(5, 60)]
    [int]$DurationSeconds = 12
)

$ErrorActionPreference = "Stop"
$project = "tools\tests\hid_rate_probe\HidRateProbe.csproj"
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null

$poses = @(
    @{ File = "accel_pos_x.csv"; Prompt = "+X：右侧边缘朝上，手柄保持静止" },
    @{ File = "accel_neg_x.csv"; Prompt = "-X：左侧边缘朝上，手柄保持静止" },
    @{ File = "accel_pos_y.csv"; Prompt = "+Y：USB-C 前端朝上，手柄保持静止" },
    @{ File = "accel_neg_y.csv"; Prompt = "-Y：握把尾端朝上，手柄保持静止" },
    @{ File = "accel_pos_z.csv"; Prompt = "+Z：按键正面朝上，手柄保持静止" },
    @{ File = "accel_neg_z.csv"; Prompt = "-Z：按键正面朝下，手柄保持静止" }
)

Write-Host "即将采集六面原始加速度。每个姿态按 Enter 后等待 $DurationSeconds 秒。"
Write-Host "输出目录：$output"
foreach ($pose in $poses) {
    Read-Host $pose.Prompt
    $csv = Join-Path $output $pose.File
    dotnet run --project $project -c Debug -- `
        --path-index $PathIndex `
        --duration $DurationSeconds `
        --csv $csv
    if ($LASTEXITCODE -ne 0) {
        throw "采集失败：$($pose.File)"
    }
}

Write-Host "六面采集完成。运行以下命令求解："
Write-Host "dotnet run --project tools\tests\imu_fidelity_lab\ImuFidelityLab.csproj -- --six-face-dir `"$output`" --output work\imu_fidelity_lab\six-face-report.md"

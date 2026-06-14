# 测试电脑端断联取证

此工具只负责记录，不会刷写或修改 ESP32 固件。

## 游戏断联测试

1. ESP32 的原生 USB 和 CH343 控制口都连接到测试电脑。
2. 关闭 PRO2 Manager、串口监视器和其他会占用 COM 口的软件。
3. 在此目录打开 PowerShell，运行：

   `powershell -ExecutionPolicy Bypass -File .\Start-DualSenseHostTrace.ps1 -DurationSeconds 1800`

4. 先打开网页 Gamepad Tester，确认输入正常，再进入游戏复现断联。
5. 断联后继续等待至少 30 秒，然后回到监控窗口按 Enter。
6. 桌面会生成 `DualSenseHostTrace_时间.zip`，把 ZIP 交给 Codex 分析。

新版工具只会周期性短暂打开 CH343 串口，不再在整个监控期间长期独占
COM 口。切换固件前仍建议停止诊断，以避免采样窗口与 esptool 碰撞。

## 独立震动测试

先关闭 Steam 和游戏，再运行：

`powershell -ExecutionPolicy Bypass -File .\Start-DualSenseHostTrace.ps1 -DurationSeconds 30 -RumbleTest`

工具会发送三次非零左右电机命令。此测试可以区分：

- Windows 没有把 HID OUT 包发给固件；
- 固件收到包但没有进入震动转换；
- 固件已向 BLE 写震动，而 Pro2 没有实际响应。

测试时不要同时打开 Manager 的日志监听，避免抢占 CH343 串口。

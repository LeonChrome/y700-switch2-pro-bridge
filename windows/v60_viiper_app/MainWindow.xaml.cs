using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Y700Switch2V60Viiper;

public partial class MainWindow : Window
{
    private Forms.NotifyIcon? trayIcon;
    private Forms.ContextMenuStrip? trayMenu;
    private bool shutdownInProgress;
    private bool shutdownComplete;
    private bool trayTipShown;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        SourceInitialized += MainWindow_SourceInitialized;
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        CreateTrayIcon();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int enabled = 1;
            if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            {
                _ = DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
            }
        }
        catch
        {
            // Dark title bars are cosmetic; unsupported Windows builds keep the system default.
        }
    }

    private void CreateTrayIcon()
    {
        trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("切换 新和联胜 / PS5", null, (_, _) => ExecuteTrayCommand(vm => vm.StartDualSenseCommand));
        trayMenu.Items.Add("切换 Pro2 / Nintendo", null, (_, _) => ExecuteTrayCommand(vm => vm.StartPro2Command));
        trayMenu.Items.Add("切换 Xbox / XInput", null, (_, _) => ExecuteTrayCommand(vm => vm.StartXboxCommand));
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("连接 PRO2 · 进入游戏", null, (_, _) => ExecuteTrayCommand(vm => vm.ConnectPro2InputCommand));
        trayMenu.Items.Add("停止虚拟设备", null, (_, _) => ExecuteTrayCommand(vm => vm.StopCommand));
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("退出", null, (_, _) => Dispatcher.BeginInvoke(new Action(RequestExitFromTray)));

        trayIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "PRO2 控制板 V6.2.7 新和联胜",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray));
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
        {
            return;
        }

        if (IsUiSmokeRun())
        {
            return;
        }

        Hide();
        if (!trayTipShown && trayIcon != null)
        {
            trayTipShown = true;
            trayIcon.BalloonTipTitle = "PRO2 控制板仍在运行";
            trayIcon.BalloonTipText = "右键托盘图标可后台切换三模或进入游戏；双击才会打开主界面。";
            trayIcon.ShowBalloonTip(2500);
        }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    private void ExecuteTrayCommand(Func<MainViewModel, System.Windows.Input.ICommand> commandSelector)
    {
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (DataContext is not MainViewModel viewModel)
            {
                return;
            }

            System.Windows.Input.ICommand command = commandSelector(viewModel);
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }
        }));
    }

    private void RequestExitFromTray()
    {
        Close();
    }

    private static bool IsUiSmokeRun()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("V60_UI_SMOKE"),
            "1",
            StringComparison.Ordinal);
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (shutdownComplete)
        {
            return;
        }

        e.Cancel = true;
        if (shutdownInProgress)
        {
            return;
        }

        shutdownInProgress = true;
        try
        {
            if (DataContext is MainViewModel viewModel)
            {
                await viewModel.ShutdownAsync();
            }
        }
        finally
        {
            shutdownComplete = true;
            shutdownInProgress = false;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Close));
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        trayIcon?.Dispose();
        trayMenu?.Dispose();
        trayIcon = null;
        trayMenu = null;
        System.Windows.Application.Current.Shutdown(0);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int valueSize);
}

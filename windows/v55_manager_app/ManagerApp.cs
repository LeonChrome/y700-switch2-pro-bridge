using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Y700Switch2V55Manager
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application app = new Application();
            app.Run(new MainWindow());
        }
    }

    public sealed class MainWindow : Window
    {
        private readonly string repoRoot;
        private readonly DispatcherTimer statusTimer = new DispatcherTimer();
        private readonly StringBuilder log = new StringBuilder();
        private SerialPort serial;

        private ComboBox portBox;
        private ComboBox firmwareModeBox;
        private ComboBox hapticModeBox;
        private ComboBox patternBox;
        private TextBox idfPathBox;
        private TextBox bleTargetBox;
        private TextBox maxBox;
        private TextBox gainBox;
        private TextBox transientGainBox;
        private TextBox intervalBox;
        private TextBox thresholdBox;
        private TextBox durationBox;
        private TextBox intensityBox;
        private TextBox customBox;
        private TextBox logBox;
        private TextBlock modeText;
        private TextBlock firmwareText;
        private TextBlock usbText;
        private TextBlock bleText;
        private TextBlock hapticText;
        private TextBlock raw02Text;
        private TextBlock commandText;

        public MainWindow()
        {
            repoRoot = FindRepoRoot();
            Title = "Y700 Switch2 V5.5 Manager";
            Width = 1280;
            Height = 820;
            MinWidth = 1100;
            MinHeight = 720;
            Background = new SolidColorBrush(Color.FromRgb(245, 247, 250));
            Content = BuildUi();
            RefreshPorts();
            statusTimer.Interval = TimeSpan.FromSeconds(2);
            statusTimer.Tick += delegate { SendSerial("status", false); };
            Closing += delegate { CloseSerial(); };
        }

        private UIElement BuildUi()
        {
            DockPanel root = new DockPanel();
            root.Children.Add(BuildStatusBar());

            Grid grid = new Grid { Margin = new Thickness(12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(grid);

            StackPanel left = new StackPanel();
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);
            left.Children.Add(Section("Flash", BuildFlashPanel()));
            left.Children.Add(Section("Mode", BuildModePanel()));
            left.Children.Add(Section("BLE", BuildBlePanel()));

            Grid right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            AddToGrid(right, Section("USB Checks", BuildUsbPanel()), 0);
            AddToGrid(right, Section("Haptic Audio / raw02", BuildHapticPanel()), 1);
            AddToGrid(right, Section("Log", BuildLogPanel()), 2);
            return root;
        }

        private UIElement BuildStatusBar()
        {
            Border bar = new Border { Background = new SolidColorBrush(Color.FromRgb(23, 32, 42)), Padding = new Thickness(14) };
            DockPanel.SetDock(bar, Dock.Top);
            Grid grid = new Grid();
            for (int i = 0; i < 6; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            modeText = Status("Mode: Unknown", Brushes.White);
            firmwareText = Status("FW: unknown", Brushes.White);
            usbText = Status("USB: unchecked", Brushes.Gold);
            bleText = Status("BLE: disconnected", Brushes.Orange);
            hapticText = Status("Haptic: off", Brushes.LightGray);
            raw02Text = Status("Raw02: dry", Brushes.LightGray);
            AddToGrid(grid, modeText, 0, 0);
            AddToGrid(grid, firmwareText, 0, 1);
            AddToGrid(grid, usbText, 0, 2);
            AddToGrid(grid, bleText, 0, 3);
            AddToGrid(grid, hapticText, 0, 4);
            AddToGrid(grid, raw02Text, 0, 5);
            bar.Child = grid;
            return bar;
        }

        private UIElement BuildFlashPanel()
        {
            StackPanel p = new StackPanel();
            portBox = new ComboBox { Width = 130, Margin = Pad() };
            firmwareModeBox = new ComboBox { Width = 260, Margin = Pad() };
            firmwareModeBox.Items.Add("V5.5 DualSense-Pro2 Haptic");
            firmwareModeBox.Items.Add("V5.5 HID-only Recovery");
            firmwareModeBox.Items.Add("V5.2 raw02 experimental");
            firmwareModeBox.Items.Add("V5.0 stable Pro2");
            firmwareModeBox.SelectedIndex = 0;
            idfPathBox = new TextBox { Text = @"C:\Espressif\v5.3.3\esp-idf", Margin = Pad() };
            p.Children.Add(Row(Label("COM"), portBox, Button("Refresh", delegate { RefreshPorts(); })));
            p.Children.Add(Row(Label("Firmware"), firmwareModeBox));
            p.Children.Add(Row(Label("IDF"), idfPathBox));
            p.Children.Add(Wrap(
                Button("One-click flash", delegate { Flash(false); }),
                Button("Flash + monitor", delegate { Flash(true); }),
                Button("Restore V5.0 stable", delegate { firmwareModeBox.SelectedIndex = 3; Flash(false); }),
                Button("Open release folder", delegate { OpenPath(Path.Combine(repoRoot, "release", "v5.5")); })
            ));
            return p;
        }

        private UIElement BuildModePanel()
        {
            return Wrap(
                Button("PRO2 mode", delegate { Log("PRO2 mode requires flashing V5.0/V5.2 and replugging native USB."); SendSerial("mode pro2", true); }),
                Button("DualSense mode", delegate { SendSerial("mode dualsense", true); }),
                Button("Status", delegate { SendSerial("status", true); })
            );
        }

        private UIElement BuildUsbPanel()
        {
            return Wrap(
                Button("Composite", delegate { RunScript(@"tools\check_v5_5_usb_composite.ps1", ""); }),
                Button("Identity", delegate { RunScript(@"tools\check_v5_5_dualsense_identity.ps1", ""); }),
                Button("Audio endpoint", delegate { RunScript(@"tools\check_v5_5_dualsense_audio.ps1", ""); }),
                Button("HID frequency", delegate { RunScript(@"tools\check_v5_5_dualsense_reports.ps1", ""); }),
                Button("joy.cpl", delegate { StartShell("joy.cpl", ""); }),
                Button("mmsys.cpl", delegate { StartShell("mmsys.cpl", ""); })
            );
        }

        private UIElement BuildBlePanel()
        {
            StackPanel p = new StackPanel();
            bleTargetBox = new TextBox { Margin = Pad() };
            p.Children.Add(Row(Label("Target"), bleTargetBox));
            p.Children.Add(Wrap(
                Button("Scan", delegate { SendSerial("ble scan", true); }),
                Button("List", delegate { SendSerial("ble list", true); }),
                Button("Connect", delegate { SendSerial("ble connect " + bleTargetBox.Text.Trim(), true); }),
                Button("Connect last", delegate { SendSerial("ble reconnect", true); }),
                Button("Autoreconnect ON", delegate { SendSerial("ble auto on", true); }),
                Button("Autoreconnect OFF", delegate { SendSerial("ble auto off", true); }),
                Button("Disconnect", delegate { SendSerial("ble disconnect", true); })
            ));
            return p;
        }

        private UIElement BuildHapticPanel()
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            StackPanel left = new StackPanel();
            StackPanel right = new StackPanel();
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);

            hapticModeBox = new ComboBox { Margin = Pad() };
            foreach (string s in new[] { "auto", "tick", "punch", "continuous", "texture" }) hapticModeBox.Items.Add(s);
            hapticModeBox.SelectedIndex = 0;
            maxBox = Box("96");
            gainBox = Box("1.0");
            transientGainBox = Box("0.65");
            intervalBox = Box("50");
            thresholdBox = Box("512");
            left.Children.Add(Row(Label("Mode"), hapticModeBox));
            left.Children.Add(Row(Label("Max"), maxBox, Label("Gain"), gainBox));
            left.Children.Add(Row(Label("Transient"), transientGainBox, Label("Interval"), intervalBox));
            left.Children.Add(Row(Label("Threshold"), thresholdBox));
            left.Children.Add(Wrap(
                Button("Read", delegate { SendSerial("haptic status", true); }),
                Button("Apply", ApplyHaptic),
                Button("Defaults", delegate { SendSerial("haptic defaults", true); }),
                Button("Dry-run ON", delegate { SendSerial("haptic dryrun on", true); }),
                Button("Dry-run OFF", delegate { SendSerial("haptic dryrun off", true); }),
                Button("Live ON", LiveOn),
                Button("Live OFF", delegate { SendSerial("haptic raw02 off", true); }),
                Button("Stop", delegate { SendSerial("haptic test stop", true); })
            ));

            patternBox = new ComboBox { Margin = Pad() };
            foreach (string s in new[] { "silence", "ch2_tick", "ch3_tick", "both_tick", "ch2_punch", "ch3_punch", "both_punch", "continuous", "texture", "sweep" }) patternBox.Items.Add(s);
            patternBox.SelectedIndex = 3;
            durationBox = Box("600");
            intensityBox = Box("50");
            right.Children.Add(Row(Label("Pattern"), patternBox));
            right.Children.Add(Row(Label("Duration"), durationBox, Label("Intensity"), intensityBox));
            right.Children.Add(Wrap(
                Button("Send audio pattern", SendAudioPattern),
                Button("Haptic status", delegate { SendSerial("haptic status", true); }),
                Button("Test tick raw02", delegate { SendSerial("haptic test tick", true); }),
                Button("Test punch raw02", delegate { SendSerial("haptic test punch", true); }),
                Button("List audio devices", delegate { RunScript(@"tools\send_v5_5_haptic_audio_test.ps1", "-ListDevices"); })
            ));
            return grid;
        }

        private UIElement BuildLogPanel()
        {
            DockPanel p = new DockPanel();
            StackPanel top = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(top, Dock.Top);
            customBox = new TextBox { Width = 360, Margin = Pad() };
            commandText = new TextBlock { Text = "Command: idle", VerticalAlignment = VerticalAlignment.Center, Margin = Pad() };
            top.Children.Add(Button("Clear", delegate { log.Length = 0; UpdateLog(); }));
            top.Children.Add(Button("Save", delegate { SaveLog(); }));
            top.Children.Add(customBox);
            top.Children.Add(Button("Send", delegate { SendSerial(customBox.Text, true); }));
            top.Children.Add(commandText);
            logBox = new TextBox
            {
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            p.Children.Add(top);
            p.Children.Add(logBox);
            return p;
        }

        private void RefreshPorts()
        {
            string selected = portBox != null ? portBox.SelectedItem as string : null;
            if (portBox == null) return;
            portBox.Items.Clear();
            foreach (string port in SerialPort.GetPortNames()) portBox.Items.Add(port);
            if (!string.IsNullOrEmpty(selected) && portBox.Items.Contains(selected)) portBox.SelectedItem = selected;
            else if (portBox.Items.Count > 0) portBox.SelectedIndex = 0;
            Log("Ports refreshed.");
        }

        private void ConnectSerial()
        {
            if (serial != null && serial.IsOpen) return;
            string port = portBox.SelectedItem as string;
            if (string.IsNullOrEmpty(port)) throw new InvalidOperationException("Select a COM port first.");
            serial = new SerialPort(port, 115200) { NewLine = "\n", ReadTimeout = 500, WriteTimeout = 1000 };
            serial.Open();
            Task.Run(delegate { ReadLoop(); });
            statusTimer.Start();
            Log("Serial connected: " + port);
        }

        private void CloseSerial()
        {
            statusTimer.Stop();
            try { if (serial != null) serial.Close(); } catch { }
            serial = null;
        }

        private void ReadLoop()
        {
            while (serial != null && serial.IsOpen)
            {
                try
                {
                    string line = serial.ReadLine().Trim();
                    Dispatcher.Invoke(delegate { HandleLine(line); });
                }
                catch (TimeoutException) { }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(delegate { Log("ERROR serial " + ex.Message); });
                    break;
                }
            }
        }

        private void SendSerial(string command, bool logTx)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            try
            {
                ConnectSerial();
                if (logTx) Log("> " + command);
                serial.Write(command.Trim() + "\n");
            }
            catch (Exception ex)
            {
                Log("ERROR send: " + ex.Message);
            }
        }

        private void HandleLine(string line)
        {
            Log(line);
            if (line.IndexOf("\"mode\":\"dualsense\"") >= 0) modeText.Text = "Mode: DualSense";
            if (line.IndexOf("\"ble\":\"connected\"") >= 0) { bleText.Text = "BLE: connected"; bleText.Foreground = Brushes.LightGreen; }
            else if (line.IndexOf("\"ble\":\"") >= 0) { bleText.Text = "BLE: see log"; bleText.Foreground = Brushes.Orange; }
            if (line.IndexOf("\"haptic\":\"live\"") >= 0) { hapticText.Text = "Haptic: live"; hapticText.Foreground = Brushes.LightGreen; raw02Text.Text = "Raw02: live"; }
            else if (line.IndexOf("\"haptic\":\"dry\"") >= 0) { hapticText.Text = "Haptic: dry"; hapticText.Foreground = Brushes.Gold; raw02Text.Text = "Raw02: dry"; }
            else if (line.IndexOf("\"haptic\":\"off\"") >= 0) { hapticText.Text = "Haptic: off"; hapticText.Foreground = Brushes.LightGray; }
            if (line.IndexOf("\"version\":") >= 0) firmwareText.Text = "FW: v5.5 experimental";
        }

        private void ApplyHaptic()
        {
            SendSerial("haptic mode " + hapticModeBox.SelectedItem, true);
            SendSerial("haptic max " + maxBox.Text.Trim(), true);
            SendSerial("haptic gain " + gainBox.Text.Trim(), true);
            SendSerial("haptic transient_gain " + transientGainBox.Text.Trim(), true);
            SendSerial("haptic interval " + intervalBox.Text.Trim(), true);
            SendSerial("haptic threshold " + thresholdBox.Text.Trim(), true);
            SendSerial("haptic status", true);
        }

        private void LiveOn()
        {
            if (MessageBox.Show("Live raw02 forwarding will send haptic audio to the real Pro2 over BLE. Keep intensity conservative?", "Enable live raw02", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }
            SendSerial("haptic raw02 on", true);
            SendSerial("haptic dryrun off", true);
            SendSerial("haptic status", true);
        }

        private void SendAudioPattern()
        {
            string args = "-Pattern " + patternBox.SelectedItem + " -DurationMs " + durationBox.Text.Trim() + " -Intensity " + intensityBox.Text.Trim();
            RunScript(@"tools\send_v5_5_haptic_audio_test.ps1", args);
        }

        private void Flash(bool monitor)
        {
            string selected = (firmwareModeBox.SelectedItem as string) ?? "";
            string profile = selected.IndexOf("HID-only") >= 0 ? "hid_only" : "hid_audio_uac1_4ch_ds5like";
            if (selected.IndexOf("V5.0") >= 0 || selected.IndexOf("V5.2") >= 0)
            {
                Log("Selected Pro2 firmware route. Use the V5.0/V5.2 release flasher for that mode; V5.5 Manager keeps DualSense identity independent.");
                return;
            }
            string port = portBox.SelectedItem as string;
            string args = "-Profile " + profile;
            if (!string.IsNullOrEmpty(port)) args += " -Port " + port;
            if (!string.IsNullOrWhiteSpace(idfPathBox.Text)) args += " -IdfPath \"" + idfPathBox.Text.Trim() + "\"";
            RunScript(@"tools\esp32s3\flash_v5_5_dualsense_identity.ps1", args);
            if (monitor && !string.IsNullOrEmpty(port))
            {
                RunScript(@"tools\esp32s3\monitor.ps1", "-Port " + port);
            }
        }

        private void RunScript(string relativeScript, string arguments)
        {
            string script = Path.Combine(repoRoot, relativeScript);
            if (!File.Exists(script))
            {
                Log("ERROR missing script: " + relativeScript);
                return;
            }
            string args = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" " + (arguments ?? "");
            commandText.Text = "Command: powershell " + relativeScript + " " + arguments;
            Task.Run(delegate { RunProcess("powershell", args); });
        }

        private void RunProcess(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.WorkingDirectory = repoRoot;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                Process p = Process.Start(psi);
                p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) Dispatcher.Invoke(delegate { Log(e.Data); }); };
                p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) Dispatcher.Invoke(delegate { Log("ERR " + e.Data); }); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                Dispatcher.Invoke(delegate { Log("process exit=" + p.ExitCode); });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(delegate { Log("ERROR process " + ex.Message); });
            }
        }

        private void StartShell(string file, string args)
        {
            try { Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true }); }
            catch (Exception ex) { Log("ERROR open " + ex.Message); }
        }

        private void OpenPath(string path)
        {
            Directory.CreateDirectory(path);
            StartShell(path, "");
        }

        private void SaveLog()
        {
            string path = Path.Combine(repoRoot, "logs", "v5_5_manager_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, log.ToString(), Encoding.UTF8);
            Log("Saved log: " + path);
        }

        private void Log(string text)
        {
            log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text);
            UpdateLog();
        }

        private void UpdateLog()
        {
            if (logBox == null) return;
            logBox.Text = log.ToString();
            logBox.ScrollToEnd();
        }

        private static string FindRepoRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                if (File.Exists(Path.Combine(dir, "README.md")) && Directory.Exists(Path.Combine(dir, "tools"))) return dir;
                dir = Directory.GetParent(dir) != null ? Directory.GetParent(dir).FullName : null;
            }
            string cwd = Directory.GetCurrentDirectory();
            if (File.Exists(Path.Combine(cwd, "README.md"))) return cwd;
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private static Border Section(string title, UIElement child)
        {
            StackPanel p = new StackPanel();
            p.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            p.Children.Add(child);
            return new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(216, 222, 231)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10), Child = p };
        }

        private static TextBlock Status(string text, Brush brush) { return new TextBlock { Text = text, Foreground = brush, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6) }; }
        private static TextBlock Label(string text) { return new TextBlock { Text = text, Width = 74, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray, Margin = Pad() }; }
        private static TextBox Box(string text) { return new TextBox { Text = text, Width = 76, Margin = Pad() }; }
        private static Thickness Pad() { return new Thickness(4); }
        private static Button Button(string text, Action action) { Button b = new Button { Content = text, MinWidth = 86, Padding = new Thickness(8, 4, 8, 4), Margin = Pad() }; b.Click += delegate { action(); }; return b; }
        private static WrapPanel Wrap(params UIElement[] children) { WrapPanel p = new WrapPanel(); foreach (UIElement child in children) p.Children.Add(child); return p; }
        private static StackPanel Row(params UIElement[] children) { StackPanel p = new StackPanel { Orientation = Orientation.Horizontal }; foreach (UIElement child in children) p.Children.Add(child); return p; }
        private static void AddToGrid(Grid grid, UIElement child, int row) { Grid.SetRow(child, row); grid.Children.Add(child); }
        private static void AddToGrid(Grid grid, UIElement child, int row, int column) { Grid.SetRow(child, row); Grid.SetColumn(child, column); grid.Children.Add(child); }
    }
}

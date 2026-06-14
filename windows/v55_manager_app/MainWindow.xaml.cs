using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Threading;

namespace Y700Switch2V55Manager;

public partial class MainWindow : Window
{
    private bool logScrollPending;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(this);
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Shutdown();
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        Application.Current.Shutdown(0);
    }

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (logScrollPending || textBox.Text.Length == 0)
        {
            return;
        }

        logScrollPending = true;
        textBox.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                logScrollPending = false;
                if (!textBox.IsVisible || textBox.IsKeyboardFocusWithin)
                {
                    return;
                }

                textBox.CaretIndex = textBox.Text.Length;
                textBox.ScrollToEnd();
            }));
    }
}

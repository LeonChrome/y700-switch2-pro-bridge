using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;

namespace Y700Switch2V55Manager;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(this);
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Shutdown();
        }
    }

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        bool nearBottom = textBox.VerticalOffset + textBox.ViewportHeight >= textBox.ExtentHeight - 24;
        if (!nearBottom)
        {
            return;
        }

        textBox.CaretIndex = textBox.Text.Length;
        textBox.ScrollToEnd();
    }
}

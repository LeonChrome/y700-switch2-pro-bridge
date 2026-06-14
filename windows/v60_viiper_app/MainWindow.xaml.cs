using System;
using System.ComponentModel;
using System.Windows;

namespace Y700Switch2V60Viiper;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.ShutdownAsync();
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        Application.Current.Shutdown(0);
    }
}

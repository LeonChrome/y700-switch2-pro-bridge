using System;
using System.ComponentModel;
using System.Windows;

namespace Y700Switch2V60Viiper;

public partial class MainWindow : Window
{
    private bool shutdownInProgress;
    private bool shutdownComplete;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
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
            Close();
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        Application.Current.Shutdown(0);
    }
}

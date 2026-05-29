using System.Windows;

namespace Y700Switch2Manager;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void LogTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.AutoScroll)
        {
            LogTextBox.ScrollToEnd();
        }
    }
}

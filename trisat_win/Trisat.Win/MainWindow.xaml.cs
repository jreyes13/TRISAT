using System.ComponentModel;
using System.Windows;
using Trisat.Win.ViewModels;

namespace Trisat.Win;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(Dispatcher);
        DataContext = _viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosing(e);
    }
}

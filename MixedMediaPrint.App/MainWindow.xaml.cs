using System.Windows;
using MixedMediaPrint.App.ViewModels;

namespace MixedMediaPrint.App;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

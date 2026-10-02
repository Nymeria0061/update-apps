using UpdateHub.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace UpdateHub.App.Views.Pages;

public partial class LogPage : INavigableView<LogViewModel>
{
    public LogPage(LogViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    public LogViewModel ViewModel { get; }
}

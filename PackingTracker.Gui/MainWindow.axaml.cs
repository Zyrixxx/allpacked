using Avalonia.Controls;

namespace PackingTracker.Gui;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        // In MVVM, the window stays thin: XAML binds to this DataContext,
        // while MainWindowViewModel owns the screen state and commands.
        _viewModel = new MainWindowViewModel(new DialogService(this));
        DataContext = _viewModel;

        Closed += (_, _) => _viewModel.SaveCurrentProfile();
    }
}

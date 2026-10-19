using Avalonia;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Ursa.Controls;
using Avalonia.Controls;
using Avalonia.Input;
using LabelPlus_Next.Services;
using LabelPlus_Next.Lang;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace LabelPlus_Next.Views;

public partial class MainProjects : UrsaWindow
{
    private DataGrid? _grid;
    private Button? _removeButton;

    public MainProjects()
    {
        InitializeComponent();
        _grid = this.FindControl<DataGrid>("ProjectsGrid");
        _removeButton = this.FindControl<Button>("RemoveButton");
        var cont = this.FindControl<Button>("ContinueNoProjectButton");
        if (cont != null) cont.Click += ContinueNoProject_Click;

        var topProvider = App.Services.GetRequiredService<ITopLevelProvider>();
        topProvider.TopLevel = this;

        if (DataContext is null)
            DataContext = App.Services.GetRequiredService<MainProjectsViewModel>();

        if (DataContext is MainProjectsViewModel vm)
        {
            vm.OpenProjectRequested += async (_, path) =>
            {
                if (vm.IsBusy) return;
                var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                var topProvider = App.Services.GetRequiredService<ITopLevelProvider>();
                var existing = MainWindow.Instance;
                MainWindow main;
                if (existing is null)
                {
                    main = new MainWindow { DataContext = App.Services.GetRequiredService<MainWindowViewModel>() };
                    main.Show();
                }
                else
                {
                    main = existing;
                    if (!main.IsVisible) main.Show();
                }
                if (lifetime != null) lifetime.MainWindow = main;
                topProvider.TopLevel = main;

                // Await on UI context (no ConfigureAwait(false)) so continuation runs on UI thread
                await main.OpenTranslateWithFileAsync(path);
                await Dispatcher.UIThread.InvokeAsync(Close); // ensure Close on UI thread
            };
        }

        if (_removeButton != null)
        {
            _removeButton.Click += OnRemoveClicked;
        }
    }

    private async void OnRemoveClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainProjectsViewModel vm) return;
        if (vm.SelectedProject is null) return;
        var res = await MessageBox.ShowAsync(
            UiText.F(LangKeys.ui0199, "确认移除项目: {0}?", vm.SelectedProject.Name),
            UiText.T(LangKeys.ui0200, "确认"),
            MessageBoxIcon.Warning,
            MessageBoxButton.YesNo);
        if (res == MessageBoxResult.Yes)
        {
            if (vm.RemoveSelectedCommand.CanExecute(null)) vm.RemoveSelectedCommand.Execute(null);
        }
    }

    private void ProjectsGrid_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainProjectsViewModel vm) return;
        if (vm.SelectedProject is null) return;
        if (vm.OpenSelectedProjectCommand.CanExecute(null))
        {
            vm.OpenSelectedProjectCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ContinueNoProject_Click(object? sender, RoutedEventArgs e)
    {
        // Open main window without selecting a project
        var existing = MainWindow.Instance;
        MainWindow main;
        if (existing is null)
        {
            main = new MainWindow { DataContext = App.Services.GetRequiredService<MainWindowViewModel>() };
            main.Show();
        }
        else
        {
            main = existing;
            if (!main.IsVisible) main.Show();
        }
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (lifetime != null) lifetime.MainWindow = main;
        App.Services.GetRequiredService<ITopLevelProvider>().TopLevel = main;
        // close this projects window
        if (this.VisualRoot is Window w) w.Close();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using GameBoost.ViewModels;

namespace GameBoost.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => UpdateMaximizedState();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm) oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is MainViewModel newVm) newVm.PropertyChanged += OnViewModelPropertyChanged;
        UpdateStatusDots();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateStatusDots();

    private void UpdateStatusDots()
    {
        if (DataContext is not MainViewModel vm) return;
        var on = (Brush)FindResource("B.Accent");
        var off = (Brush)FindResource("B.TextFaint");
        GmDot.Fill = vm.IsGameModeActive ? on : off;
        ChDot.Fill = vm.IsCrosshairVisible ? on : off;
    }

    private void UpdateMaximizedState()
    {
        // Ohne Rahmen ragt ein maximiertes Fenster über den Bildschirmrand – das gleichen wir aus.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

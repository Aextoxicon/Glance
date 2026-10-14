using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Glance.ViewModels;

namespace Glance.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog() : this(null) { }

    public SettingsDialog(MainViewModel? vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) SyncTheme(vm.ThemeMode);
    }

    private void SyncTheme(ThemeMode mode)
    {
        RbLight.IsChecked = mode == ThemeMode.Light;
        RbDark.IsChecked = mode == ThemeMode.Dark;
        RbSystem.IsChecked = mode == ThemeMode.System;
    }

    private void OnThemeChecked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (RbLight.IsChecked == true) vm.ThemeMode = ThemeMode.Light;
        else if (RbDark.IsChecked == true) vm.ThemeMode = ThemeMode.Dark;
        else if (RbSystem.IsChecked == true) vm.ThemeMode = ThemeMode.System;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}

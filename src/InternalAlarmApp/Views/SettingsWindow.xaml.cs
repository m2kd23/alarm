using System.Windows;
using System.Windows.Controls;
using InternalAlarmApp.Models;

namespace InternalAlarmApp.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        IntervalBox.Text = settings.DoublePressMilliseconds.ToString();
        StartupBox.IsChecked = settings.RunAtStartup;
        KeepPopupBox.IsChecked = settings.KeepPopupVisible;
        foreach (ComboBoxItem item in HotKeyBox.Items)
        {
            var values = item.Tag?.ToString()?.Split('|');
            if (values is { Length: 3 }
                && values[0] == settings.HotKeyVirtualKey.ToString()
                && values[1] == settings.HotKeyModifiers.ToString()
                && values[2] == settings.HotKeyPressCount.ToString()) HotKeyBox.SelectedItem = item;
        }
        HotKeyBox.SelectedIndex = Math.Max(0, HotKeyBox.SelectedIndex);
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IntervalBox.Text, out var interval) || interval is < 200 or > 2000)
        {
            MessageBox.Show(this, "인정 시간은 200~2000ms 사이로 입력해 주세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Information); return;
        }
        var item = (ComboBoxItem)HotKeyBox.SelectedItem;
        var values = item.Tag.ToString()!.Split('|');
        _settings.HotKeyVirtualKey = int.Parse(values[0]); _settings.HotKeyModifiers = int.Parse(values[1]); _settings.HotKeyPressCount = int.Parse(values[2]);
        _settings.HotKeyName = item.Content.ToString()!.Split(" (")[0];
        _settings.DoublePressMilliseconds = interval; _settings.RunAtStartup = StartupBox.IsChecked == true; _settings.KeepPopupVisible = KeepPopupBox.IsChecked == true;
        DialogResult = true;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Globalization;
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
        MorningTimeBox.Text = settings.MorningBriefingTime;
        WrapUpBox.IsChecked = settings.DailyWrapUpEnabled;
        WrapUpTimeBox.Text = settings.DailyWrapUpTime;
        foreach (ComboBoxItem item in HotKeyBox.Items)
        {
            var values = item.Tag?.ToString()?.Split('|');
            if (values is { Length: 3 }
                && values[0] == settings.HotKeyVirtualKey.ToString()
                && values[1] == settings.HotKeyModifiers.ToString()
                && values[2] == settings.HotKeyPressCount.ToString()) HotKeyBox.SelectedItem = item;
        }
        HotKeyBox.SelectedIndex = Math.Max(0, HotKeyBox.SelectedIndex);
        foreach (ComboBoxItem item in MorningModeBox.Items)
            if (item.Tag?.ToString() == ((int)settings.MorningBriefingMode).ToString()) MorningModeBox.SelectedItem = item;
        MorningModeBox.SelectedIndex = Math.Max(0, MorningModeBox.SelectedIndex);
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IntervalBox.Text, out var interval) || interval is < 200 or > 2000)
        {
            MessageBox.Show(this, "인정 시간은 200~2000ms 사이로 입력해 주세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Information); return;
        }
        var morningMode = (DailyTriggerMode)int.Parse(((ComboBoxItem)MorningModeBox.SelectedItem).Tag.ToString()!);
        var morningTime = new TimeSpan(8, 50, 0);
        if (morningMode == DailyTriggerMode.AtSpecifiedTime && !TryGetTime(MorningTimeBox.Text, out morningTime))
        {
            MessageBox.Show(this, "모닝 브리핑 시각은 HH:mm 형식으로 입력해 주세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Information); return;
        }
        var wrapUpTime = new TimeSpan(17, 50, 0);
        if (WrapUpBox.IsChecked == true && !TryGetTime(WrapUpTimeBox.Text, out wrapUpTime))
        {
            MessageBox.Show(this, "퇴근 브리핑 시각은 HH:mm 형식으로 입력해 주세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Information); return;
        }
        var item = (ComboBoxItem)HotKeyBox.SelectedItem;
        var values = item.Tag.ToString()!.Split('|');
        _settings.HotKeyVirtualKey = int.Parse(values[0]); _settings.HotKeyModifiers = int.Parse(values[1]); _settings.HotKeyPressCount = int.Parse(values[2]);
        _settings.HotKeyName = item.Content.ToString()!.Split(" (")[0];
        _settings.DoublePressMilliseconds = interval; _settings.RunAtStartup = StartupBox.IsChecked == true; _settings.KeepPopupVisible = KeepPopupBox.IsChecked == true;
        _settings.MorningBriefingMode = morningMode;
        _settings.MorningBriefingTime = (morningMode == DailyTriggerMode.AtSpecifiedTime ? morningTime : TimeSpan.TryParse(MorningTimeBox.Text, out var savedMorning) ? savedMorning : new TimeSpan(8, 50, 0)).ToString(@"hh\:mm");
        _settings.DailyWrapUpEnabled = WrapUpBox.IsChecked == true;
        _settings.DailyWrapUpTime = (_settings.DailyWrapUpEnabled ? wrapUpTime : TimeSpan.TryParse(WrapUpTimeBox.Text, out var savedWrapUp) ? savedWrapUp : new TimeSpan(17, 50, 0)).ToString(@"hh\:mm");
        DialogResult = true;
    }

    private static bool TryGetTime(string value, out TimeSpan time) =>
        TimeSpan.TryParseExact(value.Trim(), [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out time);
}

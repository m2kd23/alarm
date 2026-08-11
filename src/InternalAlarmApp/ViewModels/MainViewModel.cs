using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using InternalAlarmApp.Models;
using InternalAlarmApp.Services.DataService;

namespace InternalAlarmApp.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AlarmRepository _repository;
    private string _searchText = "";
    private string _filter = "전체 일정";
    private Alarm? _selectedAlarm;

    public ObservableCollection<Alarm> Alarms { get; } = [];
    public ICollectionView VisibleAlarms { get; }
    public IReadOnlyList<string> Filters { get; } = ["전체 일정", "오늘 일정", "이번 주 일정", "예정", "완료"];

    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value)) VisibleAlarms.Refresh(); }
    }
    public string Filter
    {
        get => _filter;
        set { if (Set(ref _filter, value)) VisibleAlarms.Refresh(); }
    }
    public Alarm? SelectedAlarm { get => _selectedAlarm; set => Set(ref _selectedAlarm, value); }

    public MainViewModel(AlarmRepository repository)
    {
        _repository = repository;
        VisibleAlarms = CollectionViewSource.GetDefaultView(Alarms);
        VisibleAlarms.Filter = IsVisible;
        VisibleAlarms.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(Alarm.AlarmDateTime), System.ComponentModel.ListSortDirection.Ascending));
        Reload();
    }

    public void Reload()
    {
        Alarms.Clear();
        foreach (var alarm in _repository.GetAll()) Alarms.Add(alarm);
        VisibleAlarms.Refresh();
    }

    public void SelectFilter(string filter) => Filter = Filters.Contains(filter) ? filter : "전체 일정";

    private bool IsVisible(object item)
    {
        if (item is not Alarm alarm) return false;
        var searchOk = string.IsNullOrWhiteSpace(SearchText)
            || alarm.Title.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
            || alarm.Description.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase);
        if (!searchOk) return false;

        var today = DateTime.Today;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return Filter switch
        {
            "오늘 일정" => alarm.AlarmDateTime.Date == today,
            "이번 주 일정" => alarm.AlarmDateTime.Date >= monday && alarm.AlarmDateTime.Date < monday.AddDays(7),
            "예정" => !alarm.Completed && alarm.Status != AlarmStatus.Cancelled,
            "완료" => alarm.Completed,
            _ => true
        };
    }
}

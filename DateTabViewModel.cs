using Prism.Mvvm;

namespace ConvenientNote
{
    public sealed class DateTabViewModel : BindableBase
    {
        public DateTabViewModel(DateTime date, bool isToday, bool isSelected)
        {
            Date = date.Date;
            IsToday = isToday;
            IsSelected = isSelected;
        }

        public DateTime Date { get; }

        public string DayLabel => Date.Day.ToString("00");

        public string WeekLabel => IsToday ? "今" : Date.ToString("ddd");

        public bool IsToday { get; }

        public bool IsSelected { get; }
    }
}

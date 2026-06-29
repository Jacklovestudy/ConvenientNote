using Prism.Mvvm;

namespace ConvenientNote.ViewModels
{
    public sealed class ScheduleViewModel : BindableBase
    {
        public string ViewTitle => "日程概览";

        public string ViewDescription => "日期维度还没接入领域模型，后续可以在这里按日期聚合便签。";
    }
}

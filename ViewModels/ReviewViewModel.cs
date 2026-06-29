using Prism.Mvvm;

namespace ConvenientNote.ViewModels
{
    public sealed class ReviewViewModel : BindableBase
    {
        public string ViewTitle => "数据复盘";

        public string ViewDescription => "完成率、趋势和统计图表可以放在这个独立 ViewModel 里。";
    }
}

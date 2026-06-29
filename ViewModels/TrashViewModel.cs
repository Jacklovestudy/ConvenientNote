using Prism.Mvvm;

namespace ConvenientNote.ViewModels
{
    public sealed class TrashViewModel : BindableBase
    {
        public string ViewTitle => "回收站";

        public string ViewDescription => "当前删除是硬删除。要启用回收站，需要先把便签删除改成软删除。";
    }
}

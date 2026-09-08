using ConvenientNote.Views;
using ConvenientNote.ViewModels;
using Prism.Ioc;

namespace ConvenientNote.Notes.UI;

public static class NotesModuleRegistration
{
    public static void RegisterNotesUI(this IContainerRegistry registry)
    {
        registry.RegisterForNavigation<NotesView, NotesViewModel>("NotesView");
        registry.RegisterForNavigation<TrashView, TrashViewModel>("TrashView");
    }
}

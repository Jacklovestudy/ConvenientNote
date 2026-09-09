using System.IO;
using ConvenientNote.DesktopPet.Application;
using ConvenientNote.DesktopPet.Contracts;
using ConvenientNote.DesktopPet.Infrastructure;
using ConvenientNote.DesktopPet.UI;
using ConvenientNote.Calendar.Application;
using ConvenientNote.Calendar.Contracts;
using ConvenientNote.Calendar.Infrastructure;
using ConvenientNote.Calendar.UI;
using ConvenientNote.ColorPicker.Application;
using ConvenientNote.ColorPicker.Contracts;
using ConvenientNote.ColorPicker.Infrastructure;
using ConvenientNote.ColorPicker.UI;
using ConvenientNote.Notes.Application;
using ConvenientNote.Notes.Contracts;
using ConvenientNote.Notes.Infrastructure;
using ConvenientNote.Notes.UI;
using ConvenientNote.Platform.Contracts;
using ConvenientNote.Platform.Infrastructure;
using ConvenientNote.Services;
using ConvenientNote.Todos.Application;
using ConvenientNote.Todos.Contracts;
using ConvenientNote.Todos.Infrastructure;
using ConvenientNote.UI.Common;
using ConvenientNote.ViewModels;
using ConvenientNote.Views;
using MaterialDesignThemes.Wpf;
using Prism.Ioc;

namespace ConvenientNote;

public static class DesktopModules
{
    public static void Register(IContainerRegistry registry, string directory)
    {
        var database = Path.Combine(directory, "ConvenientNote.Modules.db");
        var workspace = new SqliteWorkspaceContext(database);
        registry.RegisterInstance<IWorkspaceContext>(workspace);
        registry.RegisterSingleton<WorkspaceTransferRequestGate>();
        registry.RegisterSingleton<ITodoWeatherService, OpenMeteoWeatherService>();

        var notesRepository = new SqliteNotesRepository(database);
        var notes = new NotesApplicationService(notesRepository, workspace);
        var media = new NoteMediaService(Path.Combine(directory, "Notes", "Media"));
        registry.RegisterInstance<INotesRepository>(notesRepository);
        registry.RegisterInstance(notes);
        registry.RegisterInstance<INotesApi>(notes);
        registry.RegisterInstance<INoteMediaService>(media);
        registry.RegisterInstance(new RichTextDocumentService(media.MediaRoot));
        registry.RegisterInstance<INotesBackupService>(new NotesBackupService(notes, media));
        registry.RegisterInstance<INotesBackupPackageStager>(new NotesBackupPackageStager());
        registry.RegisterNotesUI();

        var todoRepository = new SqliteTodoRepository(database);
        var todos = new TodoApplicationService(todoRepository, workspace);
        registry.RegisterInstance<ITodoRepository>(todoRepository);
        registry.RegisterInstance(todos);
        registry.RegisterInstance<ITodoCalendarApi>(todos);
        registry.RegisterForNavigation<DayTodoView>();
        registry.RegisterForNavigation<InboxView>();
        registry.RegisterForNavigation<CompletedTodoView>();

        var calendarRepository = new SqliteCalendarRepository(database);
        var calendar = new CalendarApplicationService(calendarRepository, workspace, new TodoScheduleAdapter(todos));
        var calendarViewModel = new ScheduleViewModel(calendar);
        registry.RegisterInstance<ICalendarRepository>(calendarRepository);
        registry.RegisterInstance(calendar);
        registry.RegisterInstance<ICalendarApi>(calendar);
        registry.RegisterInstance(calendarViewModel);
        registry.RegisterInstance<ICompactModeProvider>(new CalendarCompactModeProvider(calendarViewModel));
        registry.RegisterForNavigation<ScheduleView>();

        var colorStore = new JsonColorHistoryStore(Path.Combine(directory, "ColorPicker", "history.json"));
        var colors = new ColorPickerService(colorStore);
        registry.RegisterInstance<IColorHistoryStore>(colorStore);
        registry.RegisterInstance(colors);
        registry.RegisterInstance<IColorHistoryQuery>(colors);
        registry.RegisterInstance<IScreenCapture>(new WindowsScreenCapture());
        registry.RegisterForNavigation<ColorPickerView>();
        registry.RegisterForNavigation<ReviewView>();
        var petPreferences = new PetPreferencesService(new JsonPetPreferencesStore(Path.Combine(directory, "DesktopPet", "settings.json")));
        var pet = new PetController(petPreferences);
        registry.RegisterInstance(pet);
        registry.RegisterInstance<IDesktopPet>(pet);
        registry.RegisterForNavigation<DesktopPetView>();

        registry.RegisterInstance(new NavigationCatalog([
            new(NavigationSection.DayTodo, nameof(DayTodoView), "今日待办", "今天要处理的待办", PackIconKind.CalendarToday),
            new(NavigationSection.Notes, nameof(NotesView), "笔记", "记录想法与资料", PackIconKind.NotebookEditOutline),
            new(NavigationSection.Schedule, nameof(ScheduleView), "日历", "日程与待办安排", PackIconKind.CalendarMonth),
            new(NavigationSection.Inbox, nameof(InboxView), "待办箱", "未完成事项", PackIconKind.Inbox),
            new(NavigationSection.ColorPicker, nameof(ColorPickerView), "取色器", "屏幕取色与颜色历史", PackIconKind.Eyedropper),
            new(NavigationSection.DesktopPet, nameof(DesktopPetView), "鹈鹕桌宠", "骑着单车的桌面伙伴", PackIconKind.Bird),
            new(NavigationSection.Review, nameof(ReviewView), "数据复盘", "完成情况", PackIconKind.ChartLine),
            new(NavigationSection.Completed, nameof(CompletedTodoView), "已达成", "已完成事项", PackIconKind.CheckCircleOutline),
            new(NavigationSection.Trash, nameof(TrashView), "回收站", "删除的笔记", PackIconKind.DeleteOutline)
        ]));
    }
}

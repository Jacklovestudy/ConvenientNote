using System.Security.Cryptography;
using System.Text;
using ConvenientNote.Calendar.Domain;

namespace ConvenientNote.Calendar.Application;

public sealed partial class CalendarApplicationService
{
    private ICalendarBatchRepository Batches => _repository as ICalendarBatchRepository
        ?? throw new InvalidOperationException("当前存储不支持批量导入。");

    public Task EditEventAsync(Guid id, string title, DateTime start, DateTime end, bool isAllDay, string details,
        CancellationToken cancellationToken = default, bool? isUnscheduled = null)
        => UpdateEventAsync(id, item => item.Edit(title, start, end, isAllDay, details, isUnscheduled), cancellationToken);

    public async Task<Guid> ImportAsync(ItineraryPreview preview, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(preview.Name) || preview.Name.Length > 200)
            throw new ArgumentException("请输入不超过200字的行程名称。");
        if (string.IsNullOrWhiteSpace(preview.SourceText) || preview.Items.Count is 0 or > 1000)
            throw new ArgumentException("请选择1至1000项安排后再导入。");
        var id = Guid.NewGuid();
        var items = preview.Items.Select(d => CalendarEvent.Restore(Guid.NewGuid(), d.Title, d.Start, d.End,
            d.IsAllDay, false, d.Details, id, preview.Name.Trim(), d.IsChecklist, d.IsUnscheduled)).ToArray();
        if (items.Any(i => i.Start.Year < 1902 || i.End.Year > 2199))
            throw new ArgumentException("日程日期须在1902年至2199年之间。");
        var source = string.Join('\n', preview.SourceText.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        var batch = new CalendarImportBatch(id, preview.Name.Trim(), fingerprint, preview.SourceText, preview.Overview, DateTime.UtcNow, items.Length);
        await WriteAsync((workspaceId, ct) => Batches.ImportAsync(workspaceId, batch, items, ct), cancellationToken);
        return id;
    }

    public async Task<IReadOnlyList<CalendarImportBatch>> ListBatchesAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await _workspace.GetCurrentAsync(cancellationToken);
        return await Batches.ListBatchesAsync(workspace.Id, cancellationToken);
    }

    public Task UndoImportAsync(Guid batchId, CancellationToken cancellationToken = default)
        => WriteAsync((workspace, ct) => Batches.UndoImportAsync(workspace, batchId, ct), cancellationToken);
}

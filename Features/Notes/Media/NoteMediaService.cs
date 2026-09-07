using System.IO;
using ConvenientNote.Domain.Notes;

namespace ConvenientNote.Services;

public sealed class NoteMediaService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"
    };
    private readonly string _mediaRoot;

    public NoteMediaService(string? mediaRoot = null)
    {
        _mediaRoot = mediaRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConvenientNote",
            "Media");
    }

    public string MediaRoot => _mediaRoot;

    public async Task<string> ImportAsync(NoteId noteId, string sourcePath, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(sourcePath);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("只支持 PNG、JPG、GIF、BMP 和 WebP 图片。");
        }
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("找不到要插入的图片。", sourcePath);
        }

        var noteDirectory = GetNoteDirectory(noteId);
        Directory.CreateDirectory(noteDirectory);
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var target = GetSafePath(Path.Combine(noteDirectory, fileName));
        await using var source = File.OpenRead(sourcePath);
        await using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await source.CopyToAsync(destination, cancellationToken);
        return Path.GetRelativePath(_mediaRoot, target);
    }

    public async Task DeleteOrphansAsync(NoteId noteId, IReadOnlySet<string> referencedPaths)
    {
        var directory = GetNoteDirectory(noteId);
        if (!Directory.Exists(directory))
        {
            return;
        }
        var referenced = referencedPaths
            .Select(path => GetSafePath(Path.Combine(_mediaRoot, path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (!referenced.Contains(Path.GetFullPath(file)))
            {
                File.Delete(file);
            }
        }
        await Task.CompletedTask;
    }

    public string GetAbsolutePath(string relativePath) => GetSafePath(Path.Combine(_mediaRoot, relativePath));

    public Task DeleteAllAsync(NoteId noteId)
    {
        var directory = GetNoteDirectory(noteId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
        return Task.CompletedTask;
    }

    private string GetNoteDirectory(NoteId noteId) => GetSafePath(Path.Combine(_mediaRoot, noteId.Value.ToString("N")));

    private string GetSafePath(string path)
    {
        var root = Path.GetFullPath(_mediaRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("媒体路径超出应用数据目录。");
        }
        return fullPath;
    }
}

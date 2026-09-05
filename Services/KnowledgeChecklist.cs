using System.IO;
using System.Text.RegularExpressions;

namespace ConvenientNote.Services;

public sealed record KnowledgeRow(int LineIndex, string Text, bool IsHeading, bool HasCheck, bool IsChecked)
{
    public bool IsCollapsed { get; init; }
    public string FoldGlyph => IsCollapsed ? "▶" : "▼";
    public string FoldHint => IsCollapsed ? "展开这一项" : "折叠这一项";
    public string HeadingKey => Regex.Replace(Text, @"已掌握\s*\d+\s*/\s*\d+", "").Trim();
}

public static class KnowledgeChecklist
{
    private static readonly Lazy<string> Default = new(() =>
    {
        using var stream = typeof(KnowledgeChecklist).Assembly.GetManifestResourceStream("ConvenientNote.Resources.KnowledgeChecklist.md")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
    public static string DefaultText => Default.Value;

    public static IReadOnlyList<KnowledgeRow> Parse(string text) => Split(text)
        .Select((line, index) =>
        {
            var trimmed = line.Trim();
            var check = Regex.Match(trimmed, "[☐☑]\\s*$");
            var heading = trimmed.StartsWith("### ", StringComparison.Ordinal);
            var label = check.Success ? trimmed[..check.Index].TrimEnd() : trimmed;
            if (heading) label = label[4..];
            label = label.Replace("`", "").Replace("**", "");
            return new KnowledgeRow(index, label, heading, check.Success, check.Success && check.Value.Trim() == "☑");
        }).Where(r => r.Text.Length > 0).ToList();

    public static string Toggle(string text, int lineIndex, bool isChecked)
    {
        var lines = Split(text);
        if (lineIndex < 0 || lineIndex >= lines.Length) return text;
        lines[lineIndex] = Regex.Replace(lines[lineIndex], "[☐☑](?=\\s*$)", isChecked ? "☑" : "☐");
        return Recount(string.Join(text.Contains("\r\n") ? "\r\n" : "\n", lines));
    }

    public static string Recount(string text)
    {
        var lines = Split(text);
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith("### ", StringComparison.Ordinal)) continue;
            var total = 0; var complete = 0;
            for (var j = i + 1; j < lines.Length && !lines[j].TrimStart().StartsWith("### ", StringComparison.Ordinal); j++)
            {
                if (Regex.IsMatch(lines[j], "[☐☑]\\s*$")) total++;
                if (Regex.IsMatch(lines[j], "☑\\s*$")) complete++;
            }
            lines[i] = Regex.Replace(lines[i], @"已掌握\s*\d+\s*/\s*\d+", $"已掌握 {complete}/{total}");
        }
        return string.Join(text.Contains("\r\n") ? "\r\n" : "\n", lines);
    }

    private static string[] Split(string text) => text.Replace("\r\n", "\n").Split('\n');
}

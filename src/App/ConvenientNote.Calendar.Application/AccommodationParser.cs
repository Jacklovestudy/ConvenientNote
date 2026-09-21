using System.Text.RegularExpressions;

namespace ConvenientNote.Calendar.Application;

public sealed record AccommodationInfo(string Name, string Notes, bool IsUnconfirmed)
{
    public string Label => Name + (IsUnconfirmed ? "（待确认）" : "");
    public string Description => Label + (Notes.Length > 0 ? "\n" + Notes : "");
}

/// <summary>Reads the explicit accommodation block from saved daily details, including older imports.</summary>
public static class AccommodationParser
{
    public static AccommodationInfo? Parse(string? details)
    {
        if (string.IsNullOrWhiteSpace(details)) return null;
        var lines = details.Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var heading = Regex.Match(lines[i], @"^\s*住宿\s*[:：]\s*(?<name>.*)$");
            if (!heading.Success) continue;
            var block = new List<string>();
            var inline = heading.Groups["name"].Value.Trim();
            if (inline.Length > 0) block.Add(inline);
            for (var j = i + 1; j < lines.Length; j++)
            {
                var line = lines[j].Trim();
                if (line.Length == 0)
                {
                    if (block.Count > 0) break;
                    continue;
                }
                if (line.StartsWith('【') || Regex.IsMatch(line, @"^[^:：]{1,16}[:：]\s*$")) break;
                block.Add(line);
            }
            if (block.Count == 0) return null;
            var first = block[0].TrimEnd('。', '.', '；', ';');
            var name = Regex.Replace(first, @"[，,]\s*(?:不换酒店|最后一晚|继续入住|连住).*$", "");
            var notes = string.Join('\n', block.Skip(1));
            if (name != first) notes = first[name.Length..].TrimStart('，', ',') + (notes.Length > 0 ? "\n" + notes : "");
            var uncertain = Regex.IsMatch(string.Join('\n', block), "待确认|待定|待订|未订|未预订|未见具体|需核对|另行预订");
            return new(name, notes, uncertain);
        }
        return null;
    }
}

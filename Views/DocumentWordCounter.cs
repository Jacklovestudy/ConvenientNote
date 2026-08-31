using System.Globalization;
using System.Text;

namespace ConvenientNote.Views;

internal static class DocumentWordCounter
{
    public static int Count(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;
        var insideWord = false;
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (IsEastAsianLetter(rune))
            {
                count++;
                insideWord = false;
            }
            else if (Rune.IsLetterOrDigit(rune))
            {
                if (!insideWord)
                {
                    count++;
                    insideWord = true;
                }
            }
            else if (category is UnicodeCategory.NonSpacingMark
                     or UnicodeCategory.SpacingCombiningMark
                     or UnicodeCategory.EnclosingMark)
            {
                // A combining mark belongs to the preceding word or character.
            }
            else
            {
                insideWord = false;
            }
        }

        return count;
    }

    private static bool IsEastAsianLetter(Rune rune)
    {
        var value = rune.Value;
        return Rune.IsLetter(rune) && value is >= 0x3400 and <= 0x4DBF
            or >= 0x4E00 and <= 0x9FFF
            or >= 0xF900 and <= 0xFAFF
            or >= 0x20000 and <= 0x2EE5F
            or >= 0x2F800 and <= 0x2FA1F
            or >= 0x30000 and <= 0x323AF
            or >= 0x3040 and <= 0x30FF
            or >= 0xAC00 and <= 0xD7AF;
    }
}

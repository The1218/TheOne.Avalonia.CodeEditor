using System.Globalization;
using Avalonia.Media.TextFormatting;

namespace TheOne.Avalonia.Controls;

// Logical UTF-16 ranges can occupy several visual rectangles in a bidi line.
// Cache grapheme boundaries with the shaped row, not with individual markers.
internal static class CodeTextRangeGeometry
{
    internal static IReadOnlyList<TextBounds> Bounds(TextLayout layout, string text, int first, int end,
        ref int[]? elements)
    {
        first = Math.Clamp(first, 0, text.Length);
        end = Math.Clamp(end, first, text.Length);
        if (end == first) return [];
        if (elements == null)
        {
            elements = [];
            foreach (var c in text)
                // Jamo and prepend characters can join a grapheme without being marks.
                if (c >= 0x300)
                {
                    elements = StringInfo.ParseCombiningCharacters(text);
                    break;
                }
        }

        if (elements.Length > 0)
        {
            var startIndex = Array.BinarySearch(elements, first);
            if (startIndex < 0) first = elements[Math.Max(0, ~startIndex - 1)];
            var endIndex = Array.BinarySearch(elements, end);
            if (endIndex < 0) end = ~endIndex < elements.Length ? elements[~endIndex] : text.Length;
        }

        return layout.TextLines[0].GetTextBounds(first, end - first);
    }
}
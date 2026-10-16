using System.Globalization;

namespace TheOne.Avalonia.Controls;

internal static class CodeNumericDiagnostics
{
    private static readonly string[] CsIntegers =
            ["ul", "lu", "u", "l"],
        CsNumbers = ["ul", "lu", "u", "l", "f", "d", "m"];

    private static readonly string[] CIntegers =
            ["ull", "llu", "ul", "lu", "u", "l"],
        CNumbers = ["ull", "llu", "ul", "lu", "u", "l", "f"];

    private static readonly string[] JavaIntegers = ["l"], JavaNumbers = ["l", "f", "d"];
    private static readonly string[] KotlinIntegers = ["ul", "u", "l"], KotlinNumbers = ["ul", "u", "l", "f"];

    private static readonly string[] JsNumbers =
            ["n"],
        PythonNumbers = ["j"],
        GoNumbers = ["i"],
        RubyNumbers = ["ri", "r", "i"];

    private static readonly string[] RustIntegers =
        ["usize", "isize", "u128", "i128", "u64", "i64", "u32", "i32", "u16", "i16", "u8", "i8"];

    private static readonly string[] RustNumbers =
        ["usize", "isize", "u128", "i128", "u64", "i64", "u32", "i32", "u16", "i16", "u8", "i8", "f64", "f32"];

    internal static bool Invalid(CodeAnalysis a, int index, out string literal)
    {
        var language = a.Language;
        literal = a.At(index);
        if (a.Tokens[index].Kind != CodeTokenKind.Number ||
            language is CodeType.Bash or CodeType.PowerShell or CodeType.Sql or CodeType.Css) return false;
        if (index > 0 && a.Tokens[index - 1].Kind == CodeTokenKind.Number &&
            a.Tokens[index - 1].End == a.Tokens[index].Start) return false;
        var last = index;
        while (last + 1 < a.Tokens.Count && a.Tokens[last + 1].Kind == CodeTokenKind.Number &&
               a.Tokens[last].End == a.Tokens[last + 1].Start) last++;
        if (last != index) literal = a.Document.Text[a.Tokens[index].Start..a.Tokens[last].End];
        var value = literal;
        if (value.Contains("..", StringComparison.Ordinal)) return false;
        if (value.Contains('.') && a.At(last + 1) == "(" && value.Any(char.IsLetter)) return false;
        if (language == CodeType.Cpp && value.Contains('_')) return false; // User-defined literal suffixes.
        value = value.Replace("_", "", StringComparison.Ordinal);
        if (language == CodeType.Cpp) value = value.Replace("'", "", StringComparison.Ordinal);
        if (value.Length == 0) return false;
        if (value[^1] is 'e' or 'E' && a.At(last + 1) is "+" or "-" && last + 2 < a.Tokens.Count &&
            a.Tokens[last + 2].Kind == CodeTokenKind.Number)
            value += a.At(last + 1) + a.At(last + 2);
        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (hex && value.Contains('p', StringComparison.OrdinalIgnoreCase))
            return false; // Hexadecimal floating-point forms.
        var suffixes = language switch
        {
            CodeType.CSharp => hex ? CsIntegers : CsNumbers,
            CodeType.C or CodeType.Cpp => hex ? CIntegers : CNumbers,
            CodeType.Java => hex ? JavaIntegers : JavaNumbers,
            CodeType.Kotlin => hex ? KotlinIntegers : KotlinNumbers,
            CodeType.JavaScript or CodeType.TypeScript => JsNumbers,
            CodeType.Python => PythonNumbers, CodeType.Go => GoNumbers, CodeType.Ruby => RubyNumbers,
            CodeType.Rust => hex ? RustIntegers : RustNumbers,
            _ => Array.Empty<string>()
        };
        string? suffix = null;
        foreach (var candidate in suffixes)
            if (value.EndsWith(candidate,
                    language == CodeType.Rust ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            {
                suffix = candidate;
                break;
            }

        if (suffix != null) value = value[..^suffix.Length];
        if (value.Length == 0) return true;
        if (hex) return value.Length <= 2 || !value[2..].All(char.IsAsciiHexDigit);
        if (value.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            return value.Length <= 2 || !value[2..].All(c => c is '0' or '1');
        if (value.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            return value.Length <= 2 || !value[2..].All(c => c is >= '0' and <= '7');
        if (language == CodeType.Ruby && value.StartsWith("0d", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        if (suffix == "n" && value.IndexOfAny(['.', 'e', 'E']) >= 0) return true;
        if (language == CodeType.Python && value.Length > 1 && value[0] == '0' && value.All(char.IsAsciiDigit) &&
            value.Any(c => c != '0')) return true;
        return !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }
}
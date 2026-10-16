namespace TheOne.Avalonia.Controls;

// Small immutable catalogs and bounded prefix scoring. No assembly loading or project scan.
internal static class CodeCompletionRules
{
    private static readonly string[] CsTypes =
    [
        "Boolean", "Byte", "Int32", "Int64", "Double", "Decimal", "String", "Object", "DateTime", "DateTimeOffset",
        "TimeSpan", "Guid", "Array", "List", "Dictionary", "HashSet", "Queue", "Stack", "IEnumerable", "IList",
        "IReadOnlyList", "IDisposable", "Task", "ValueTask", "CancellationToken", "CancellationTokenSource",
        "StringBuilder", "Console", "Math", "Convert", "Enumerable", "File", "Directory", "Path", "Stream",
        "MemoryStream", "HttpClient", "Uri", "Regex", "Exception", "ArgumentException", "ArgumentNullException",
        "InvalidOperationException", "NotImplementedException", "FormatException", "FormattableString"
    ];

    private static readonly string[] JavaTypes =
    [
        "String", "StringBuilder", "Object", "Integer", "Long", "Double", "Boolean", "List", "ArrayList", "Map",
        "HashMap", "Set", "HashSet", "Optional", "Stream", "System", "Math", "Exception", "RuntimeException"
    ];

    private static readonly string[] TsTypes =
    [
        "Array", "ReadonlyArray", "Promise", "Record", "Partial", "Required", "Pick", "Omit", "Map", "Set", "Date",
        "RegExp", "Error", "HTMLElement", "Event"
    ];

    internal static bool Modifier(string word)
    {
        return word is "public" or "private" or "protected" or "internal" or "static" or "abstract" or "virtual"
            or "override" or "sealed" or "readonly" or "volatile" or "async" or "extern" or "partial" or "required"
            or "unsafe" or "new";
    }

    internal static bool DeclarationKeyword(string word)
    {
        return Modifier(word) || word is "void" or "bool" or "byte" or "sbyte" or "short" or "ushort" or "int" or "uint"
            or "long" or "ulong" or "float" or "double" or "decimal" or "char" or "string" or "object" or "class"
            or "struct" or "interface" or "enum" or "record" or "delegate" or "event";
    }

    internal static bool TypeContext(CodeDocument document, CodeType language, int caret, string prefix)
    {
        if (language is not (CodeType.CSharp or CodeType.Java or CodeType.Cpp or CodeType.C or CodeType.TypeScript))
            return false;
        var line = document.Lines[document.LineAt(caret)];
        var before = line.Text[..Math.Clamp(caret - prefix.Length - line.Start, 0, line.Text.Length)].TrimEnd();
        var last = before.Split([' ', '\t', '{', ';', ')'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return last != null &&
               (Modifier(last) || last is "new" or "extends" or "implements" or "as" or "is" or "instanceof");
    }

    internal static int Score(string label, string prefix)
    {
        if (prefix.Length == 0) return 0;
        if (label.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return 0;
        if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return 1;
        return int.MaxValue;
    }

    internal static IEnumerable<CodeCompletionItem> Types(CodeType type)
    {
        var names = type switch
        {
            CodeType.CSharp => CsTypes,
            CodeType.Java => JavaTypes,
            CodeType.TypeScript => TsTypes,
            _ => Array.Empty<string>()
        };
        return names.Select(name => new CodeCompletionItem(name, name, "Standard type")
            { Kind = CodeCompletionKind.Type });
    }
}
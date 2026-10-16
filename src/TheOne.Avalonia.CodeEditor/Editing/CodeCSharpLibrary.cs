namespace TheOne.Avalonia.Controls;

internal static partial class CodeBuiltins
{
    private static readonly CodeAnalysis.Symbol[] MathAdditional =
    [
        Property("PI", "double", "Math"), Property("E", "double", "Math"), Property("Tau", "double", "Math"),
        Item("Round", "double", "Math", P("value", "double")),
        Item("Round", "double", "Math", P("value", "double"), P("digits", "int")),
        Item("Floor", "double", "Math", P("value", "double")), Item("Ceiling", "double", "Math", P("value", "double")),
        Item("Pow", "double", "Math", P("x", "double"), P("y", "double")),
        Item("Sin", "double", "Math", P("value", "double")),
        Item("Cos", "double", "Math", P("value", "double")),
        Item("Clamp", "double", "Math", P("value", "double"), P("min", "double"), P("max", "double"))
    ];

    private static readonly CodeAnalysis.Symbol[] StringAdditional =
    [
        Property("Empty", "string", "string"), Item("TrimStart", "string", "string"),
        Item("TrimEnd", "string", "string"),
        Item("Insert", "string", "string", P("startIndex", "int"), P("value", "string")),
        Item("Remove", "string", "string", P("startIndex", "int")),
        Item("Join", "string", "string", P("separator", "string"), P("values", "IEnumerable<string>")),
        Item("Format", "string", "string", P("format", "string"), P("...args", "object[]"))
    ];

    private static readonly CodeAnalysis.Symbol[] ListAdditional =
    [
        Property("Capacity", "int", "List"), Item("AddRange", "void", "List", P("collection", "IEnumerable<T>")),
        Item("Insert", "void", "List", P("index", "int"), P("item", "T")),
        Item("RemoveAt", "void", "List", P("index", "int")),
        Item("IndexOf", "int", "List", P("item", "T")), Item("Find", "T", "List", P("match", "Predicate<T>")),
        Item("Sort", "void", "List"), Item("Reverse", "void", "List"),
        Item("OrderBy", "IEnumerable<T>", "List", P("keySelector", "Func<T, TKey>"))
    ];

    private static readonly CodeAnalysis.Symbol[] TaskAdditional =
    [
        Property("IsCompleted", "bool", "Task"), Property("IsFaulted", "bool", "Task"),
        Property("IsCanceled", "bool", "Task"),
        Property("Result", "T", "Task"), Item("Run", "Task", "Task", P("action", "Action")),
        Item("ConfigureAwait", "ConfiguredTaskAwaitable<T>", "Task", P("continueOnCapturedContext", "bool"))
    ];

    private static readonly CodeAnalysis.Symbol[] GuidMembers =
    [
        Property("Empty", "Guid", "Guid"), Item("NewGuid", "Guid", "Guid"),
        Item("Parse", "Guid", "Guid", P("input", "string")), Item("ToString", "string", "Guid")
    ];

    private static readonly CodeAnalysis.Symbol[] ConvertMembers =
    [
        Item("ToInt32", "int", "Convert", P("value", "object")),
        Item("ToInt64", "long", "Convert", P("value", "object")),
        Item("ToDouble", "double", "Convert", P("value", "object")),
        Item("ToString", "string", "Convert", P("value", "object")),
        Item("ToBase64String", "string", "Convert", P("inArray", "byte[]"))
    ];

    private static readonly CodeAnalysis.Symbol[] DirectoryMembers =
    [
        Item("Exists", "bool", "Directory", P("path", "string")),
        Item("CreateDirectory", "DirectoryInfo", "Directory", P("path", "string")),
        Item("GetFiles", "string[]", "Directory", P("path", "string")),
        Item("GetDirectories", "string[]", "Directory", P("path", "string")),
        Item("GetCurrentDirectory", "string", "Directory")
    ];

    private static readonly CodeAnalysis.Symbol[] ArrayMembers =
    [
        Property("Length", "int", "Array"), Property("Rank", "int", "Array"),
        Item("GetLength", "int", "Array", P("dimension", "int")), Item("Clone", "object", "Array")
    ];

    private static readonly CodeAnalysis.Symbol[] CsEnumerable =
    [
        Item("Where", "IEnumerable<T>", "IEnumerable", P("predicate", "Func<T, bool>")),
        Item("Select", "IEnumerable<TResult>", "IEnumerable", P("selector", "Func<T, TResult>")),
        Item("FirstOrDefault", "T", "IEnumerable"), Item("ToList", "List<T>", "IEnumerable"),
        Item("ToArray", "T[]", "IEnumerable"),
        Item("Any", "bool", "IEnumerable"), Item("Count", "int", "IEnumerable"),
        Item("OrderBy", "IEnumerable<T>", "IEnumerable", P("keySelector", "Func<T, TKey>"))
    ];

    private static readonly CodeAnalysis.Symbol[] IListAdditional =
    [
        Item("Insert", "void", "IList", P("index", "int"), P("item", "T")),
        Item("RemoveAt", "void", "IList", P("index", "int")), Item("IndexOf", "int", "IList", P("item", "T"))
    ];

    internal static IEnumerable<CodeAnalysis.Symbol> CSharpAdditional(string type)
    {
        return type switch
        {
            "Math" => MathAdditional, "string" or "String" => StringAdditional,
            "List" or "list" => ListAdditional, "IList" => IListAdditional, "Task" => TaskAdditional,
            "Guid" => GuidMembers, "Convert" => ConvertMembers, "Directory" => DirectoryMembers,
            "Array" => ArrayMembers.Concat(CsEnumerable),
            _ => []
        };
    }

    internal static IEnumerable<CodeAnalysis.Symbol> Constructors(CodeType language, string name)
    {
        if (language != CodeType.CSharp) return [];
        return name switch
        {
            "List" =>
            [
                Item(name, "List<T>", name), Item(name, "List<T>", name, P("capacity", "int")),
                Item(name, "List<T>", name, P("collection", "IEnumerable<T>"))
            ],
            "Dictionary" =>
            [
                Item(name, "Dictionary<TKey,TValue>", name),
                Item(name, "Dictionary<TKey,TValue>", name, P("capacity", "int"))
            ],
            "StringBuilder" => [Item(name, name, name), Item(name, name, name, P("value", "string"))],
            "Exception" or "FormatException" or "InvalidOperationException" =>
            [
                Item(name, name, name), Item(name, name, name, P("message", "string")),
                Item(name, name, name, P("message", "string"), P("innerException", "Exception"))
            ],
            "DateTime" => [Item(name, name, name, P("year", "int"), P("month", "int"), P("day", "int"))],
            _ => []
        };
    }
}
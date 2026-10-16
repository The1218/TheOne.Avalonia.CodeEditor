namespace TheOne.Avalonia.Controls;

// Small static catalogs complement document symbols. Type names are intentionally
// language-specific; unknown external libraries never receive invented members.
internal static partial class CodeBuiltins
{
    private static readonly CodeAnalysis.Symbol[] PythonGlobals =
    [
        Item("print", "None", null, P("*values", "object"), P("sep", "str", "' '"), P("end", "str", "'\\n'")),
        Item("len", "int", null, P("obj", "Sized")), Item("range", "range", null, P("stop", "int")),
        Item("range", "range", null, P("start", "int"), P("stop", "int"), P("step", "int", "1")),
        Item("open", "TextIO", null, P("file", "str"), P("mode", "str", "'r'")),
        Item("sorted", "list", null, P("iterable"), P("key", null, "None"), P("reverse", "bool", "False")),
        Item("str", "string", null, P("object", null, "''")), Item("list", "list", null, P("iterable", null, "()")),
        Item("dict", "dict", null, P("**kwargs"))
    ];

    private static readonly CodeAnalysis.Symbol[] PythonString =
    [
        Item("upper", "str", "str"), Item("lower", "str", "str"),
        Item("strip", "str", "str", P("chars", "str", "None")),
        Item("split", "list", "str", P("sep", "str", "None"), P("maxsplit", "int", "-1")),
        Item("replace", "str", "str", P("old", "str"), P("new", "str"), P("count", "int", "-1")),
        Item("startswith", "bool", "str", P("prefix", "str")), Item("join", "str", "str", P("iterable")),
        Item("format", "str", "str", P("*args"), P("**kwargs"))
    ];

    private static readonly CodeAnalysis.Symbol[] PythonList =
    [
        Item("append", "None", "list", P("object")), Item("extend", "None", "list", P("iterable")),
        Item("insert", "None", "list", P("index", "int"), P("object")),
        Item("pop", "object", "list", P("index", "int", "-1")),
        Item("sort", "None", "list", P("key", null, "None"), P("reverse", "bool", "False")),
        Item("clear", "None", "list")
    ];

    private static readonly CodeAnalysis.Symbol[] PythonDict =
    [
        Item("get", "object", "dict", P("key"), P("default", null, "None")), Item("keys", "list", "dict"),
        Item("values", "list", "dict"), Item("items", "list", "dict"), Item("update", "None", "dict", P("other"))
    ];

    private static readonly CodeAnalysis.Symbol[] CsGlobals =
        [Item("nameof", "string", null, P("expression")), Item("typeof", "Type", null, P("type"))];

    private static readonly CodeAnalysis.Symbol[] ConsoleCs =
    [
        Item("WriteLine", "void", "Console"), Item("WriteLine", "void", "Console", P("value", "string")),
        Item("WriteLine", "void", "Console", P("format", "string"), P("arg0", "object")),
        Item("Write", "void", "Console", P("value", "string")), Item("ReadLine", "string", "Console")
    ];

    private static readonly CodeAnalysis.Symbol[] CsString =
    [
        Property("Length", "int", "string"), Item("Contains", "bool", "string", P("value", "string")),
        Item("StartsWith", "bool", "string", P("value", "string")),
        Item("Substring", "string", "string", P("startIndex", "int")),
        Item("Substring", "string", "string", P("startIndex", "int"), P("length", "int")),
        Item("Replace", "string", "string", P("oldValue", "string"), P("newValue", "string")),
        Item("Split", "list", "string", P("separator", "char")),
        Item("Trim", "string", "string"), Item("ToUpper", "string", "string"), Item("ToLower", "string", "string"),
        Item("IsNullOrEmpty", "bool", "string", P("value", "string")),
        Item("IsNullOrWhiteSpace", "bool", "string", P("value", "string")),
        Item("IndexOf", "int", "string", P("value", "string")), Item("EndsWith", "bool", "string", P("value", "string"))
    ];

    private static readonly CodeAnalysis.Symbol[] CsList =
    [
        Property("Count", "int", "List"), Item("Add", "void", "List", P("item", "T")),
        Item("Remove", "bool", "List", P("item", "T")), Item("Clear", "void", "List"),
        Item("Contains", "bool", "List", P("item", "T")), Item("ToArray", "list", "List"),
        Item("Where", "IEnumerable", "List", P("predicate", "Func<T, bool>")),
        Item("Select", "IEnumerable", "List", P("selector", "Func<T, TResult>")), Item("FirstOrDefault", "T", "List"),
        Item("Any", "bool", "List"), Item("ToList", "List", "List")
    ];

    private static readonly CodeAnalysis.Symbol[] CsDictionary =
    [
        Property("Count", "int", "Dictionary"), Property("Keys", "IEnumerable", "Dictionary"),
        Property("Values", "IEnumerable", "Dictionary"),
        Item("Add", "void", "Dictionary", P("key", "TKey"), P("value", "TValue")),
        Item("ContainsKey", "bool", "Dictionary", P("key", "TKey")),
        Item("TryGetValue", "bool", "Dictionary", P("key", "TKey"), P("value", "out TValue")),
        Item("Remove", "bool", "Dictionary", P("key", "TKey")), Item("Clear", "void", "Dictionary")
    ];

    private static readonly CodeAnalysis.Symbol[] CsStringBuilder =
    [
        Property("Length", "int", "StringBuilder"), Property("Capacity", "int", "StringBuilder"),
        Item("Append", "StringBuilder", "StringBuilder", P("value", "string")),
        Item("AppendLine", "StringBuilder", "StringBuilder", P("value", "string", "\"\"")),
        Item("Clear", "StringBuilder", "StringBuilder"), Item("ToString", "string", "StringBuilder")
    ];

    private static readonly CodeAnalysis.Symbol[] CsDateTime =
    [
        Property("Now", "DateTime", "DateTime"), Property("UtcNow", "DateTime", "DateTime"),
        Property("Today", "DateTime", "DateTime"), Property("Year", "int", "DateTime"),
        Property("Month", "int", "DateTime"), Property("Day", "int", "DateTime"),
        Item("AddDays", "DateTime", "DateTime", P("value", "double")), Item("ToString", "string", "DateTime"),
        Item("ToString", "string", "DateTime", P("format", "string")),
        Item("Parse", "DateTime", "DateTime", P("s", "string"))
    ];

    private static readonly CodeAnalysis.Symbol[] CsFile =
    [
        Item("Exists", "bool", "File", P("path", "string")), Item("ReadAllText", "string", "File", P("path", "string")),
        Item("ReadAllTextAsync", "Task", "File", P("path", "string"),
            P("cancellationToken", "CancellationToken", "default")),
        Item("WriteAllText", "void", "File", P("path", "string"), P("contents", "string")),
        Item("ReadAllLines", "list", "File", P("path", "string"))
    ];

    private static readonly CodeAnalysis.Symbol[] CsPath =
    [
        Item("Combine", "string", "Path", P("...paths", "string[]")),
        Item("GetFileName", "string", "Path", P("path", "string")),
        Item("GetExtension", "string", "Path", P("path", "string")),
        Item("GetDirectoryName", "string", "Path", P("path", "string"))
    ];

    private static readonly CodeAnalysis.Symbol[] CsException =
    [
        Property("Message", "string", "Exception"), Property("StackTrace", "string", "Exception"),
        Property("InnerException", "Exception", "Exception"), Item("ToString", "string", "Exception")
    ];

    private static readonly CodeAnalysis.Symbol[] TaskMembers =
    [
        Item("Delay", "Task", "Task", P("millisecondsDelay", "int")),
        Item("WhenAll", "Task", "Task", P("tasks", "Task[]"))
    ];

    private static readonly CodeAnalysis.Symbol[] JsGlobals =
    [
        Item("setTimeout", "number", null, P("callback", "Function"), P("delay", "number", "0")),
        Item("parseInt", "number", null, P("string", "string"), P("radix", "number", "10")),
        Item("fetch", "Promise", null, P("input", "string"), P("init", "RequestInit", "{}"))
    ];

    private static readonly CodeAnalysis.Symbol[] JsString =
    [
        Property("length", "number", "string"), Item("includes", "bool", "string", P("searchString", "string")),
        Item("slice", "string", "string", P("start", "number"), P("end", "number", "length")),
        Item("substring", "string", "string", P("start", "number"), P("end", "number", "length")),
        Item("split", "Array", "string", P("separator", "string")),
        Item("replace", "string", "string", P("searchValue", "string"), P("replaceValue", "string")),
        Item("trim", "string", "string"), Item("toUpperCase", "string", "string"),
        Item("toLowerCase", "string", "string")
    ];

    private static readonly CodeAnalysis.Symbol[] JsArray =
    [
        Property("length", "number", "Array"), Item("push", "number", "Array", P("...items", "T[]")),
        Item("pop", "T", "Array"), Item("map", "Array", "Array", P("callback", "Function")),
        Item("filter", "Array", "Array", P("predicate", "Function")),
        Item("forEach", "void", "Array", P("callback", "Function")),
        Item("join", "string", "Array", P("separator", "string", "','"))
    ];

    private static readonly CodeAnalysis.Symbol[] ConsoleJs =
    [
        Item("log", "void", "console", P("...data", "any[]")), Item("error", "void", "console", P("...data", "any[]")),
        Item("warn", "void", "console", P("...data", "any[]"))
    ];

    private static readonly CodeAnalysis.Symbol[] Math =
    [
        Item("abs", "number", "Math", P("value", "number")), Item("max", "number", "Math", P("...values", "number[]")),
        Item("min", "number", "Math", P("...values", "number[]")), Item("sqrt", "number", "Math", P("value", "number"))
    ];

    private static readonly CodeAnalysis.Symbol[] CsMath =
    [
        Item("Abs", "double", "Math", P("value", "double")),
        Item("Max", "double", "Math", P("val1", "double"), P("val2", "double")),
        Item("Min", "double", "Math", P("val1", "double"), P("val2", "double")),
        Item("Sqrt", "double", "Math", P("value", "double"))
    ];

    private static readonly CodeAnalysis.Symbol[] Json =
    [
        Item("parse", "object", "JSON", P("text", "string")),
        Item("stringify", "string", "JSON", P("value", "any"), P("replacer", "Function", "null"),
            P("space", "number", "0"))
    ];

    private static readonly CodeAnalysis.Symbol[] DomDocument =
    [
        Item("querySelector", "Element", "document", P("selectors", "string")),
        Item("getElementById", "Element", "document", P("id", "string")),
        Item("createElement", "Element", "document", P("tagName", "string"))
    ];

    private static readonly CodeAnalysis.Symbol[] DomElement =
    [
        Property("textContent", "string", "Element"), Property("innerHTML", "string", "Element"),
        Item("setAttribute", "void", "Element", P("name", "string"), P("value", "string")),
        Item("addEventListener", "void", "Element", P("type", "string"), P("listener", "Function"))
    ];

    private static readonly CodeAnalysis.Symbol[] JavaString =
    [
        Item("length", "int", "String"),
        Item("substring", "String", "String", P("beginIndex", "int"), P("endIndex", "int")),
        Item("contains", "boolean", "String", P("sequence", "CharSequence")), Item("trim", "String", "String"),
        Item("toUpperCase", "String", "String")
    ];

    private static CodeAnalysis.Symbol Item(string name, string type, string? owner,
        params CodeParameterInfo[] parameters)
    {
        return new CodeAnalysis.Symbol(name, type, owner, 0, 0, int.MaxValue,
            new CodeSignatureInfo(name, parameters, type));
    }

    private static CodeParameterInfo P(string name, string? type = null, string? value = null)
    {
        return new CodeParameterInfo(name, type, value);
    }

    private static CodeAnalysis.Symbol Property(string name, string type, string owner)
    {
        return new CodeAnalysis.Symbol(name, type, owner, 0, 0, int.MaxValue);
    }

    internal static IEnumerable<CodeAnalysis.Symbol> Globals(CodeType language)
    {
        return (language switch
        {
            CodeType.Python => PythonGlobals,
            CodeType.JavaScript or CodeType.TypeScript => JsGlobals,
            CodeType.CSharp => CsGlobals,
            _ => ExtendedGlobals(language)
        }).Concat(Namespaces(language));
    }

    internal static IEnumerable<CodeAnalysis.Symbol> Members(CodeType language, string type)
    {
        if (language == CodeType.Python)
            return type switch
            {
                "string" or "str" => PythonString, "list" or "List" => PythonList,
                "dict" or "object" => PythonDict, "math" => Math,
                _ => ExtendedMembers(language, type)
            };
        if (language is CodeType.JavaScript or CodeType.TypeScript)
            return type switch
            {
                "string" or "String" => JsString, "list" or "Array" => JsArray,
                "console" => ConsoleJs, "Math" => Math, "JSON" => Json,
                "document" => DomDocument, "Element" or "HTMLElement" => DomElement, _ => []
            };
        if (language == CodeType.CSharp)
            return (type switch
            {
                "string" or "String" => CsString, "Console" => ConsoleCs,
                "List" or "list" or "IList" => CsList,
                "IEnumerable" => CsEnumerable,
                "IReadOnlyList" or "IReadOnlyCollection" => new[] { Property("Count", "int", type) }.Concat(
                    CsEnumerable),
                "Math" => CsMath, "Task" => TaskMembers,
                "Dictionary" or "IDictionary" => CsDictionary, "StringBuilder" => CsStringBuilder,
                "DateTime" or "DateTimeOffset" => CsDateTime, "File" => CsFile,
                "Path" => CsPath,
                "Exception" or "ArgumentException" or "InvalidOperationException" or "FormatException" => CsException,
                _ => Array.Empty<CodeAnalysis.Symbol>()
            }).Concat(CSharpAdditional(type));
        if (language == CodeType.Java)
            return type switch
            {
                "String" or "string" => JavaString, "System" => [Property("out", "PrintStream", "System")],
                "PrintStream" => JavaPrint,
                _ => ExtendedMembers(language, type)
            };
        return ExtendedMembers(language, type);
    }
}
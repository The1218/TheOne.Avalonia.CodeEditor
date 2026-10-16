namespace TheOne.Avalonia.Controls;

internal static partial class CodeBuiltins
{
    private static readonly CodeAnalysis.Symbol[] CsNamespaces =
        [Container("Console"), Container("Math"), Container("Task"), Container("List"), Container("String")];

    private static readonly CodeAnalysis.Symbol[] JsNamespaces =
    [
        Container("console"), Container("document"), Container("JSON"), Container("Math"), Container("Array"),
        Container("String")
    ];

    private static readonly CodeAnalysis.Symbol[] GoNamespaces =
        [Container("fmt"), Container("strings"), Container("strconv")];

    private static readonly CodeAnalysis.Symbol[] RustNamespaces =
        [Container("String"), Container("Vec"), Container("Option"), Container("Result")];

    private static readonly CodeAnalysis.Symbol[] JavaNamespaces =
        [Container("System"), Container("String"), Container("ArrayList")];

    private static readonly CodeAnalysis.Symbol[] LuaNamespaces =
        [Container("string"), Container("table"), Container("math")];

    private static readonly CodeAnalysis.Symbol[] PythonNamespaces = [Container("math")];
    private static readonly CodeAnalysis.Symbol[] CppNamespaces = [Container("std")];

    private static readonly CodeAnalysis.Symbol[] CFunctions =
    [
        Item("printf", "int", null, P("format", "const char*"), P("...args")),
        Item("scanf", "int", null, P("format", "const char*"), P("...args")),
        Item("strlen", "size_t", null, P("str", "const char*")),
        Item("strcmp", "int", null, P("lhs", "const char*"), P("rhs", "const char*")),
        Item("malloc", "void*", null, P("size", "size_t")), Item("free", "void", null, P("ptr", "void*")),
        Item("memcpy", "void*", null, P("dest", "void*"), P("src", "const void*"), P("count", "size_t"))
    ];

    private static readonly CodeAnalysis.Symbol[] CppString =
    [
        Item("size", "size_t", "string"), Item("length", "size_t", "string"), Item("empty", "bool", "string"),
        Item("substr", "string", "string", P("pos", "size_t", "0"), P("count", "size_t", "npos")),
        Item("find", "size_t", "string", P("str", "string"), P("pos", "size_t", "0")),
        Item("append", "string", "string", P("str", "string")), Item("c_str", "const char*", "string")
    ];

    private static readonly CodeAnalysis.Symbol[] CppVector =
    [
        Item("size", "size_t", "vector"), Item("empty", "bool", "vector"),
        Item("push_back", "void", "vector", P("value", "T")), Item("pop_back", "void", "vector"),
        Item("clear", "void", "vector"), Item("begin", "iterator", "vector"), Item("end", "iterator", "vector")
    ];

    private static readonly CodeAnalysis.Symbol[] CppStd =
    [
        Item("string", "string", "std", P("value", "const char*")), Item("vector", "vector", "std"),
        Item("move", "T&&", "std", P("value", "T")), Item("make_unique", "unique_ptr", "std", P("...args")),
        Item("sort", "void", "std", P("first", "iterator"), P("last", "iterator"))
    ];

    private static readonly CodeAnalysis.Symbol[] GoFunctions =
    [
        Item("append", "list", null, P("slice", "[]T"), P("...values", "T")), Item("len", "int", null, P("value")),
        Item("cap", "int", null, P("value")), Item("make", "T", null, P("type"), P("...size", "int")),
        Item("delete", "void", null, P("map", "map[K]V"), P("key", "K")), Item("panic", "void", null, P("value", "any"))
    ];

    private static readonly CodeAnalysis.Symbol[] GoFmt =
    [
        Item("Println", "int, error", "fmt", P("...a", "any")),
        Item("Printf", "int, error", "fmt", P("format", "string"), P("...a", "any")),
        Item("Sprintf", "string", "fmt", P("format", "string"), P("...a", "any")),
        Item("Errorf", "error", "fmt", P("format", "string"), P("...a", "any"))
    ];

    private static readonly CodeAnalysis.Symbol[] GoStrings =
    [
        Item("Contains", "bool", "strings", P("s", "string"), P("substr", "string")),
        Item("Split", "list", "strings", P("s", "string"), P("sep", "string")),
        Item("Join", "string", "strings", P("elems", "[]string"), P("sep", "string")),
        Item("TrimSpace", "string", "strings", P("s", "string")),
        Item("ToUpper", "string", "strings", P("s", "string")), Item("ToLower", "string", "strings", P("s", "string"))
    ];

    private static readonly CodeAnalysis.Symbol[] GoStrconv =
    [
        Item("Itoa", "string", "strconv", P("i", "int")), Item("Atoi", "int, error", "strconv", P("s", "string")),
        Item("ParseBool", "bool, error", "strconv", P("str", "string"))
    ];

    private static readonly CodeAnalysis.Symbol[] RustFunctions =
    [
        Item("println!", "()", null, P("format", "&str"), P("...args")),
        Item("format!", "String", null, P("format", "&str"), P("...args")), Item("drop", "()", null, P("value", "T")),
        Item("Some", "Option<T>", null, P("value", "T")), Item("Ok", "Result<T, E>", null, P("value", "T")),
        Item("Err", "Result<T, E>", null, P("error", "E"))
    ];

    private static readonly CodeAnalysis.Symbol[] RustString =
    [
        Item("new", "String", "String"), Item("from", "String", "String", P("value", "&str")),
        Item("len", "usize", "String"), Item("is_empty", "bool", "String"),
        Item("push_str", "()", "String", P("string", "&str")), Item("contains", "bool", "String", P("pattern", "&str")),
        Item("trim", "str", "String"), Item("to_uppercase", "String", "String")
    ];

    private static readonly CodeAnalysis.Symbol[] RustVec =
    [
        Item("new", "Vec", "Vec"), Item("len", "usize", "Vec"), Item("push", "()", "Vec", P("value", "T")),
        Item("pop", "Option<T>", "Vec"), Item("iter", "Iterator", "Vec"), Item("clear", "()", "Vec")
    ];

    private static readonly CodeAnalysis.Symbol[] JavaFunctions =
        [Item("String", "String", null, P("value", "String"))];

    private static readonly CodeAnalysis.Symbol[] JavaPrint =
    [
        Item("println", "void", "PrintStream", P("value", "Object")),
        Item("print", "void", "PrintStream", P("value", "Object"))
    ];

    private static readonly CodeAnalysis.Symbol[] JavaList =
    [
        Item("size", "int", "List"), Item("add", "boolean", "List", P("element", "E")),
        Item("get", "E", "List", P("index", "int")), Item("remove", "E", "List", P("index", "int")),
        Item("isEmpty", "boolean", "List"), Item("clear", "void", "List")
    ];

    private static readonly CodeAnalysis.Symbol[] KotlinFunctions =
    [
        Item("println", "Unit", null, P("message", "Any?")), Item("listOf", "List", null, P("...elements", "T")),
        Item("mutableListOf", "MutableList", null, P("...elements", "T")),
        Item("require", "Unit", null, P("value", "Boolean"))
    ];

    private static readonly CodeAnalysis.Symbol[] KotlinString =
    [
        Property("length", "Int", "String"), Item("uppercase", "String", "String"),
        Item("lowercase", "String", "String"), Item("trim", "String", "String"),
        Item("contains", "Boolean", "String", P("other", "CharSequence")),
        Item("substring", "String", "String", P("startIndex", "Int"), P("endIndex", "Int"))
    ];

    private static readonly CodeAnalysis.Symbol[] KotlinList =
    [
        Property("size", "Int", "List"), Item("isEmpty", "Boolean", "List"),
        Item("map", "List", "List", P("transform", "(T) -> R")),
        Item("filter", "List", "List", P("predicate", "(T) -> Boolean")),
        Item("forEach", "Unit", "List", P("action", "(T) -> Unit"))
    ];

    private static readonly CodeAnalysis.Symbol[] SwiftFunctions =
    [
        Item("print", "Void", null, P("...items", "Any"), P("separator", "String", "\" \""),
            P("terminator", "String", "\"\\n\"")),
        Item("assert", "Void", null, P("condition", "Bool"), P("message", "String", "\"\""))
    ];

    private static readonly CodeAnalysis.Symbol[] SwiftString =
    [
        Property("count", "Int", "String"), Property("isEmpty", "Bool", "String"),
        Item("uppercased", "String", "String"), Item("lowercased", "String", "String"),
        Item("contains", "Bool", "String", P("other", "String")),
        Item("hasPrefix", "Bool", "String", P("prefix", "String"))
    ];

    private static readonly CodeAnalysis.Symbol[] SwiftArray =
    [
        Property("count", "Int", "Array"), Property("isEmpty", "Bool", "Array"),
        Item("append", "Void", "Array", P("newElement", "Element")), Item("removeLast", "Element", "Array"),
        Item("map", "Array", "Array", P("transform", "(Element) -> T"))
    ];

    private static readonly CodeAnalysis.Symbol[] DartFunctions =
    [
        Item("print", "void", null, P("object", "Object?")),
        Item("identical", "bool", null, P("a", "Object?"), P("b", "Object?"))
    ];

    private static readonly CodeAnalysis.Symbol[] DartString =
    [
        Property("length", "int", "String"), Property("isEmpty", "bool", "String"),
        Item("toUpperCase", "String", "String"), Item("toLowerCase", "String", "String"),
        Item("trim", "String", "String"),
        Item("substring", "String", "String", P("start", "int"), P("end", "int?", "null")),
        Item("split", "List", "String", P("pattern", "Pattern"))
    ];

    private static readonly CodeAnalysis.Symbol[] DartList =
    [
        Property("length", "int", "List"), Item("add", "void", "List", P("value", "E")),
        Item("remove", "bool", "List", P("value", "Object?")), Item("clear", "void", "List"),
        Item("map", "Iterable", "List", P("toElement", "T Function(E)"))
    ];

    private static readonly CodeAnalysis.Symbol[] PhpFunctions =
    [
        Item("strlen", "int", null, P("string", "string")), Item("count", "int", null, P("value", "Countable|array")),
        Item("json_encode", "string|false", null, P("value", "mixed"), P("flags", "int", "0")),
        Item("json_decode", "mixed", null, P("json", "string"), P("associative", "?bool", "null")),
        Item("array_map", "array", null, P("callback", "?callable"), P("array", "array"))
    ];

    private static readonly CodeAnalysis.Symbol[] PhpDateTime =
    [
        Item("format", "string", "DateTime", P("format", "string")),
        Item("modify", "DateTime", "DateTime", P("modifier", "string")), Item("getTimestamp", "int", "DateTime"),
        Item("setTimezone", "DateTime", "DateTime", P("timezone", "DateTimeZone"))
    ];

    private static readonly CodeAnalysis.Symbol[] RubyFunctions =
    [
        Item("puts", "nil", null, P("...objects")), Item("print", "nil", null, P("...objects")),
        Item("require", "bool", null, P("path", "String"))
    ];

    private static readonly CodeAnalysis.Symbol[] RubyString =
    [
        Item("length", "Integer", "String"), Item("upcase", "String", "String"), Item("downcase", "String", "String"),
        Item("strip", "String", "String"), Item("split", "Array", "String", P("pattern", "String", "nil")),
        Item("include?", "bool", "String", P("other", "String"))
    ];

    private static readonly CodeAnalysis.Symbol[] RubyArray =
    [
        Item("length", "Integer", "Array"), Item("push", "Array", "Array", P("...objects")),
        Item("pop", "Object", "Array"), Item("each", "Enumerator", "Array", P("block")),
        Item("map", "Array", "Array", P("block")), Item("join", "String", "Array", P("separator", "String", "nil"))
    ];

    private static readonly CodeAnalysis.Symbol[] LuaFunctions =
    [
        Item("print", "nil", null, P("...values")), Item("pairs", "iterator", null, P("table", "table")),
        Item("ipairs", "iterator", null, P("table", "table")), Item("type", "string", null, P("value")),
        Item("tostring", "string", null, P("value")), Item("tonumber", "number", null, P("value"))
    ];

    private static readonly CodeAnalysis.Symbol[] LuaString =
    [
        Item("sub", "string", "string", P("s", "string"), P("i", "integer"), P("j", "integer", "-1")),
        Item("gsub", "string, integer", "string", P("s", "string"), P("pattern", "string"), P("replacement")),
        Item("upper", "string", "string", P("s", "string")), Item("lower", "string", "string", P("s", "string")),
        Item("format", "string", "string", P("format", "string"), P("...values"))
    ];

    private static readonly CodeAnalysis.Symbol[] LuaTable =
    [
        Item("insert", "nil", "table", P("list", "table"), P("value")),
        Item("remove", "any", "table", P("list", "table"), P("pos", "integer", "#list")),
        Item("concat", "string", "table", P("list", "table"), P("sep", "string", "''")),
        Item("sort", "nil", "table", P("list", "table"), P("compare", "function", "nil"))
    ];

    private static readonly CodeAnalysis.Symbol[] LuaMath =
    [
        Item("floor", "integer", "math", P("x", "number")), Item("ceil", "integer", "math", P("x", "number")),
        Item("sqrt", "number", "math", P("x", "number")), Item("abs", "number", "math", P("x", "number"))
    ];

    private static readonly CodeAnalysis.Symbol[] ShellCommands =
    [
        Item("printf", "status", null, P("format"), P("...arguments")), Item("echo", "status", null, P("...arguments")),
        Item("read", "status", null, P("...names")), Item("export", "status", null, P("...assignments")),
        Item("source", "status", null, P("filename"))
    ];

    private static readonly CodeAnalysis.Symbol[] PowerShellCommands =
    [
        Item("Write-Host", "void", null, P("Object", "Object[]")),
        Item("Write-Output", "Object", null, P("InputObject", "Object[]")),
        Item("Get-Content", "string[]", null, P("Path", "string[]")),
        Item("Get-ChildItem", "FileSystemInfo[]", null, P("Path", "string[]")),
        Item("Where-Object", "Object", null, P("FilterScript", "ScriptBlock"))
    ];

    private static readonly CodeAnalysis.Symbol[] SqlFunctions =
    [
        Item("COUNT", "integer", null, P("expression")), Item("SUM", "number", null, P("expression")),
        Item("AVG", "number", null, P("expression")), Item("MIN", "any", null, P("expression")),
        Item("MAX", "any", null, P("expression")), Item("COALESCE", "any", null, P("...expressions")),
        Item("LOWER", "text", null, P("text")), Item("UPPER", "text", null, P("text"))
    ];

    private static CodeAnalysis.Symbol Container(string name)
    {
        return new CodeAnalysis.Symbol(name, name, null, 0, 0, int.MaxValue);
    }

    private static IEnumerable<CodeAnalysis.Symbol> Namespaces(CodeType language)
    {
        return language switch
        {
            CodeType.CSharp => CsNamespaces, CodeType.JavaScript or CodeType.TypeScript => JsNamespaces,
            CodeType.Go => GoNamespaces, CodeType.Rust => RustNamespaces, CodeType.Java => JavaNamespaces,
            CodeType.Lua => LuaNamespaces, CodeType.Python => PythonNamespaces, CodeType.Cpp => CppNamespaces, _ => []
        };
    }

    private static IEnumerable<CodeAnalysis.Symbol> ExtendedGlobals(CodeType language)
    {
        return language switch
        {
            CodeType.C => CFunctions, CodeType.Cpp => CFunctions,
            CodeType.Go => GoFunctions, CodeType.Rust => RustFunctions,
            CodeType.Java => JavaFunctions, CodeType.Kotlin => KotlinFunctions,
            CodeType.Swift => SwiftFunctions, CodeType.Php => PhpFunctions,
            CodeType.Ruby => RubyFunctions, CodeType.Lua => LuaFunctions,
            CodeType.Bash => ShellCommands, CodeType.PowerShell => PowerShellCommands,
            CodeType.Dart => DartFunctions, CodeType.Sql => SqlFunctions,
            _ => []
        };
    }

    private static IEnumerable<CodeAnalysis.Symbol> ExtendedMembers(CodeType language, string type)
    {
        return (language, type) switch
        {
            (CodeType.Go, "fmt") => GoFmt, (CodeType.Go, "strings") => GoStrings, (CodeType.Go, "strconv") => GoStrconv,
            (CodeType.Rust, "String" or "string" or "str") => RustString, (CodeType.Rust, "Vec" or "list") => RustVec,
            (CodeType.Cpp, "string" or "std::string") => CppString, (CodeType.Cpp, "vector" or "list") => CppVector,
            (CodeType.Cpp, "std") => CppStd,
            (CodeType.Java, "List" or "ArrayList" or "list") => JavaList,
            (CodeType.Kotlin, "String" or "string") => KotlinString,
            (CodeType.Kotlin, "List" or "list" or "MutableList") => KotlinList,
            (CodeType.Swift, "String" or "string") => SwiftString, (CodeType.Swift, "Array" or "list") => SwiftArray,
            (CodeType.Dart, "String" or "string") => DartString, (CodeType.Dart, "List" or "list") => DartList,
            (CodeType.Ruby, "String" or "string") => RubyString, (CodeType.Ruby, "Array" or "list") => RubyArray,
            (CodeType.Php, "DateTime" or "DateTimeImmutable") => PhpDateTime,
            (CodeType.Lua, "string") => LuaString, (CodeType.Lua, "table" or "list") => LuaTable,
            (CodeType.Lua, "math") => LuaMath,
            (CodeType.PowerShell, "string" or "String") => CsString,
            _ => []
        };
    }
}
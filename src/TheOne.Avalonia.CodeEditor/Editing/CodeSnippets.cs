using System.Text;

namespace TheOne.Avalonia.Controls;

internal sealed record CodeSnippetField(int Group, int Start, int Length);

internal sealed class CodeSnippetDefinition
{
    internal CodeSnippetDefinition(string shortcut, string template, string detail)
    {
        var text = new StringBuilder();
        var fields = new List<CodeSnippetField>();
        for (var n = 0; n < template.Length;)
        {
            if (template[n] == '$' && n + 1 < template.Length && template[n + 1] == '{')
            {
                var colon = template.IndexOf(':', n + 2);
                var end = colon < 0 ? -1 : template.IndexOf('}', colon + 1);
                if (end >= 0 && int.TryParse(template.AsSpan(n + 2, colon - n - 2), out var group))
                {
                    var value = template[(colon + 1)..end];
                    fields.Add(new CodeSnippetField(group, text.Length, value.Length));
                    text.Append(value);
                    n = end + 1;
                    continue;
                }
            }

            text.Append(template[n++]);
        }

        Fields = fields;
        Item = new CodeCompletionItem(shortcut, text.ToString(), detail + " · Tab Tab",
                fields.FirstOrDefault(f => f.Group > 0)?.Start ?? text.Length)
            { Kind = CodeCompletionKind.Snippet, Snippet = this };
    }

    internal CodeCompletionItem Item { get; }
    internal IReadOnlyList<CodeSnippetField> Fields { get; }
}

internal static class CodeSnippets
{
    private static readonly CodeSnippetDefinition[] CSharp =
    [
        S("for", "for (int ${1:i} = 0; ${1:i} < ${2:length}; ${1:i}++)\n{\n    ${0:}\n}", "Indexed loop"),
        S("foreach", "foreach (var ${1:item} in ${2:collection})\n{\n    ${0:}\n}", "Iteration loop"),
        S("forr", "for (int ${1:i} = ${2:length} - 1; ${1:i} >= 0; ${1:i}--)\n{\n    ${0:}\n}", "Reverse loop"),
        S("method", "public ${1:void} ${2:Method}(${3:})\n{\n    ${0:}\n}", "Method declaration"),
        S("func", "public ${1:void} ${2:Method}(${3:})\n{\n    ${0:}\n}", "Method declaration"),
        S("async", "public async Task${1:} ${2:MethodAsync}(${3:})\n{\n    ${0:}\n}", "Async method"),
        S("prop", "public ${1:string} ${2:Name} { get; set; }${0:}", "Auto property"),
        S("propg", "public ${1:string} ${2:Name} { get; private set; }${0:}", "Property with private setter"),
        S("propfull",
            "private ${1:string} _${2:name};\npublic ${1:string} ${3:Name}\n{\n    get => _${2:name};\n    set => _${2:name} = value;\n}${0:}",
            "Property with backing field"),
        S("if", "if (${1:condition})\n{\n    ${0:}\n}", "Conditional block"),
        S("while", "while (${1:condition})\n{\n    ${0:}\n}", "While loop"),
        S("try", "try\n{\n    ${1:}\n}\ncatch (Exception ${2:ex})\n{\n    ${0:}\n}", "Try/catch block"),
        S("switch", "switch (${1:expression})\n{\n    case ${2:value}:\n        ${0:}\n        break;\n}",
            "Switch statement"),
        S("class", "public class ${1:ClassName}\n{\n    ${0:}\n}", "Class declaration"),
        S("interface", "public interface ${1:IInterface}\n{\n    ${0:}\n}", "Interface declaration")
    ];

    private static readonly CodeSnippetDefinition[] JavaScript =
    [
        S("function", "function ${1:name}(${2:}) {\n    ${0:}\n}", "Function declaration"),
        S("func", "function ${1:name}(${2:}) {\n    ${0:}\n}", "Function declaration"),
        S("afn", "const ${1:name} = (${2:}) => {\n    ${0:}\n};", "Arrow function"),
        S("async", "async function ${1:name}(${2:}) {\n    ${0:}\n}", "Async function"),
        S("for", "for (let ${1:i} = 0; ${1:i} < ${2:length}; ${1:i}++) {\n    ${0:}\n}", "Indexed loop"),
        S("forof", "for (const ${1:item} of ${2:items}) {\n    ${0:}\n}", "Iteration loop"),
        S("if", "if (${1:condition}) {\n    ${0:}\n}", "Conditional block"),
        S("get", "get ${1:name}() {\n    return ${0:value};\n}", "Property getter")
    ];

    private static readonly CodeSnippetDefinition[] Python =
    [
        S("def", "def ${1:function_name}(${2:}):\n    ${0:pass}", "Function declaration"),
        S("func", "def ${1:function_name}(${2:}):\n    ${0:pass}", "Function declaration"),
        S("async", "async def ${1:function_name}(${2:}):\n    ${0:pass}", "Async function"),
        S("prop", "@property\ndef ${1:name}(self):\n    return self._${1:name}${0:}", "Property getter"),
        S("for", "for ${1:item} in ${2:iterable}:\n    ${0:pass}", "Iteration loop"),
        S("if", "if ${1:condition}:\n    ${0:pass}", "Conditional block"),
        S("class", "class ${1:ClassName}:\n    def __init__(self, ${2:}):\n        ${0:pass}", "Class declaration"),
        S("try", "try:\n    ${1:pass}\nexcept Exception as ${2:error}:\n    ${0:pass}", "Try/except block")
    ];

    private static readonly CodeSnippetDefinition[] CFamily =
    [
        S("func", "${1:void} ${2:function_name}(${3:})\n{\n    ${0:}\n}", "Function declaration"),
        S("for", "for (int ${1:i} = 0; ${1:i} < ${2:length}; ${1:i}++)\n{\n    ${0:}\n}", "Indexed loop")
    ];

    private static readonly CodeSnippetDefinition[] Go =
    [
        S("func", "func ${1:functionName}(${2:}) ${3:} {\n    ${0:}\n}", "Function declaration"),
        S("for", "for ${1:i} := 0; ${1:i} < ${2:length}; ${1:i}++ {\n    ${0:}\n}", "Indexed loop")
    ];

    private static readonly CodeSnippetDefinition[] Rust =
    [
        S("fn", "fn ${1:function_name}(${2:})${3:} {\n    ${0:}\n}", "Function declaration"),
        S("for", "for ${1:item} in ${2:items} {\n    ${0:}\n}", "Iteration loop")
    ];

    private static readonly CodeSnippetDefinition[] Kotlin =
        [S("fun", "fun ${1:functionName}(${2:})${3:} {\n    ${0:}\n}", "Function declaration")];

    private static readonly CodeSnippetDefinition[] Swift =
        [S("func", "func ${1:functionName}(${2:})${3:} {\n    ${0:}\n}", "Function declaration")];

    private static readonly CodeSnippetDefinition[] Php =
        [S("function", "function ${1:functionName}(${2:}) {\n    ${0:}\n}", "Function declaration")];

    private static readonly CodeSnippetDefinition[] Ruby =
        [S("def", "def ${1:function_name}(${2:})\n    ${0:}\nend", "Method declaration")];

    private static readonly CodeSnippetDefinition[] Lua =
        [S("function", "function ${1:function_name}(${2:})\n    ${0:}\nend", "Function declaration")];

    private static readonly CodeSnippetDefinition[] Bash =
        [S("func", "${1:function_name}() {\n    ${0::}\n}", "Shell function")];

    private static readonly CodeSnippetDefinition[] PowerShell =
        [S("function", "function ${1:FunctionName} {\n    param(${2:})\n    ${0:}\n}", "PowerShell function")];

    internal static string? EnclosingType(CodeDocument document, int caret)
    {
        var scopes = new Stack<string?>();
        string? pending = null;
        var expectName = false;
        var count = 0;
        var last = document.LineAt(caret);
        for (var n = Math.Max(0, last - 511); n <= last; n++)
        {
            var line = document.Lines[n];
            foreach (var span in line.Tokens)
            {
                if (++count > 16384) return null;
                if (line.Start + span.Start >= caret ||
                    span.Kind is CodeTokenKind.Comment or CodeTokenKind.String) continue;
                var value = line.Text.AsSpan(span.Start, Math.Min(span.Length, caret - line.Start - span.Start));
                if (value.SequenceEqual("class") || value.SequenceEqual("struct") || value.SequenceEqual("record"))
                {
                    expectName = true;
                    continue;
                }

                if (expectName && value.Length > 0 && CodeDocument.IsIdentifier(value[0]))
                {
                    pending = value.ToString().Trim();
                    expectName = false;
                }

                foreach (var c in value)
                    if (c == '{')
                    {
                        scopes.Push(pending);
                        pending = null;
                        expectName = false;
                    }
                    else if (c == '}')
                    {
                        if (scopes.Count > 0) scopes.Pop();
                        pending = null;
                    }
                    else if (c == ';')
                    {
                        pending = null;
                        expectName = false;
                    }
            }
        }

        return scopes.FirstOrDefault(name => name != null);
    }

    private static CodeSnippetDefinition S(string key, string template, string detail)
    {
        return new CodeSnippetDefinition(key, template, detail);
    }

    internal static IEnumerable<CodeCompletionItem> Get(CodeType type, string? owner = null)
    {
        var definitions = type switch
        {
            CodeType.CSharp => CSharp, CodeType.JavaScript or CodeType.TypeScript => JavaScript,
            CodeType.Python => Python, CodeType.Go => Go, CodeType.Rust => Rust,
            CodeType.Java or CodeType.C or CodeType.Cpp or CodeType.Dart => CFamily,
            CodeType.Kotlin => Kotlin, CodeType.Swift => Swift, CodeType.Php => Php,
            CodeType.Ruby => Ruby, CodeType.Lua => Lua, CodeType.Bash => Bash, CodeType.PowerShell => PowerShell,
            _ => []
        };
        foreach (var definition in definitions) yield return definition.Item;
        if (type == CodeType.CSharp)
            yield return S("ctor",
                "public " + (owner == null || owner.StartsWith('@') ? "${1:ClassName}" : owner) +
                "(${2:})\n{\n    ${0:}\n}", "Constructor").Item;
    }
}
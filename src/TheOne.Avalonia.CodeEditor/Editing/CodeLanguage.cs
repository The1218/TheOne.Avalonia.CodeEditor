namespace TheOne.Avalonia.Controls;

internal sealed record CodeLanguage(
    string Name,
    string Keywords,
    string? LineComment = "//",
    string? BlockStart = "/*",
    string? BlockEnd = "*/",
    bool Braces = true)
{
    private const string Common =
        "if else for while do break continue return switch case default try catch finally throw new class public private protected static const this true false null ";

    private static readonly Dictionary<CodeType, CodeLanguage> Profiles = new()
    {
        [CodeType.PlainText] = new CodeLanguage("Plain text", "", null, null, null, false),
        [CodeType.CSharp] = new CodeLanguage("C#",
            Common +
            "using namespace internal sealed partial struct record interface enum void string int long float double decimal bool object var async await base get set init required readonly override virtual abstract event delegate typeof nameof is as out ref in Task lock yield"),
        [CodeType.JavaScript] = new CodeLanguage("JavaScript",
            Common +
            "let var function extends import export from as typeof instanceof in of undefined async await delete debugger super void yield"),
        [CodeType.TypeScript] = new CodeLanguage("TypeScript",
            Common +
            "let var function extends import export from as typeof instanceof in of undefined async await interface type enum readonly implements declare namespace any unknown never number string boolean void keyof infer satisfies"),
        [CodeType.Json] = new CodeLanguage("JSON", "true false null", null, null, null),
        [CodeType.Xml] = new CodeLanguage("XML", "", null, "<!--", "-->", false),
        [CodeType.Axaml] = new CodeLanguage("AXAML", "", null, "<!--", "-->", false),
        [CodeType.Html] = new CodeLanguage("HTML",
            "doctype html head body div span a p script style input button class id", null, "<!--", "-->", false),
        [CodeType.Css] =
            new CodeLanguage("CSS", "important media supports keyframes import charset layer container", null),
        [CodeType.Sql] = new CodeLanguage("SQL",
            "select from where insert into values update set delete join left right inner outer on as group by order having limit offset and or not null is in case when then else end create table alter drop distinct asc desc union all with true false begin commit rollback primary key references",
            "--", Braces: false),
        [CodeType.Python] = new CodeLanguage("Python",
            "def class return if elif else for while in import from as with try except finally raise yield async await pass break continue lambda and or not is None True False global nonlocal assert del match case",
            "#", null, null, false),
        [CodeType.Yaml] = new CodeLanguage("YAML", "true false null yes no on off", "#", null, null, false),
        [CodeType.Markdown] = new CodeLanguage("Markdown", "", null, null, null, false),
        [CodeType.C] = new CodeLanguage("C",
            Common +
            "auto char double enum extern float int long register short signed sizeof struct typedef union unsigned void volatile"),
        [CodeType.Cpp] = new CodeLanguage("C++",
            Common +
            "auto char double enum extern float int long short signed sizeof struct typedef union unsigned void volatile namespace using template typename constexpr consteval nullptr include virtual override noexcept operator friend delete"),
        [CodeType.Java] = new CodeLanguage("Java",
            Common +
            "package import interface enum record extends implements final abstract void int long double boolean char byte short throws synchronized volatile transient instanceof var"),
        [CodeType.Go] = new CodeLanguage("Go",
            "package import func type struct interface map chan go defer select case default range for if else switch break continue return var const true false nil make new append len cap error"),
        [CodeType.Rust] = new CodeLanguage("Rust",
            "fn let mut const static pub crate use mod struct enum impl trait type where for while loop if else match return break continue async await move ref self Self super unsafe extern dyn in as true false Some None Ok Err"),
        [CodeType.Kotlin] = new CodeLanguage("Kotlin",
            Common +
            "fun val var object interface data sealed suspend inline reified when in is as package import override open internal companion lateinit null"),
        [CodeType.Swift] = new CodeLanguage("Swift",
            Common +
            "func let var struct enum protocol extension guard defer import optional nil self Self throws rethrows async await inout some any where"),
        [CodeType.Php] = new CodeLanguage("PHP",
            Common +
            "function echo print require include namespace use interface trait extends implements foreach as array isset unset empty fn match"),
        [CodeType.Ruby] = new CodeLanguage("Ruby",
            "def class module end if elsif else unless case when while until for in do begin rescue ensure raise return yield break next redo retry self nil true false require include attr_reader attr_accessor",
            "#", null, null, false),
        [CodeType.Bash] = new CodeLanguage("Bash",
            "if then elif else fi for while until do done case esac in function select local export readonly return break continue echo printf source set unset trap",
            "#", null, null, false),
        [CodeType.PowerShell] = new CodeLanguage("PowerShell",
            "function param begin process end if elseif else foreach for while do until switch try catch finally throw return break continue class enum using filter true false null",
            "#", "<#", "#>"),
        [CodeType.Dart] = new CodeLanguage("Dart",
            Common +
            "import export library part void int double bool String dynamic final var async await extends implements mixin with factory get set required late"),
        [CodeType.Lua] = new CodeLanguage("Lua",
            "and break do else elseif end false for function goto if in local nil not or repeat return then true until while",
            "--", "--[[", "]]", false),
        [CodeType.Toml] = new CodeLanguage("TOML", "true false", "#", null, null, false),
        [CodeType.Ini] = new CodeLanguage("INI", "true false yes no", ";", null, null, false)
    };

    internal HashSet<string> KeywordSet { get; } = new(Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries),
        Name == "SQL" ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal bool Markup => Name is "XML" or "AXAML" or "HTML";

    internal static CodeLanguage Get(CodeType type)
    {
        return Profiles.GetValueOrDefault(type) ?? Profiles[CodeType.PlainText];
    }
}
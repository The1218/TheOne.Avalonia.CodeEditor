using System.ComponentModel;
using System.Runtime.CompilerServices;
using TheOne.Avalonia.Controls;

namespace TheOne.DesignPreview;

public sealed class CodeEditorDemoViewModel : INotifyPropertyChanged
{
    public const string JsonSample = """
                                     {
                                       "application": {
                                         "name": "TheOne Studio",
                                         "theme": "Light",
                                         "language": "zh-CN"
                                       },
                                       "editor": {
                                         "codeType": "Json",
                                         "isReadOnly": false,
                                         "showLineNumbers": true,
                                         "syntaxHighlighting": true,
                                         "tabSize": 4
                                       },
                                       "features": [
                                         "native-input",
                                         "compiled-binding"
                                       ]
                                     }
                                     """;

    public const string DarkSample =
        "{\n  \"theme\": \"Dark\",\n  \"isReadOnly\": true,\n  \"showLineNumbers\": true,\n  \"codeType\": \"Json\"\n}";

    public const string NoNumbersSample =
        "{\n  \"theme\": \"Light\",\n  \"isReadOnly\": true,\n  \"showLineNumbers\": false,\n  \"codeType\": \"Json\"\n}";

    private string? _code = JsonSample;
    private CodeType _codeType = CodeType.Json;
    private bool _isReadOnly, _wordWrap;
    private bool _showLineNumbers = true, _highlight = true;
    public CodeType[] CodeTypes { get; } = Enum.GetValues<CodeType>();

    public string? Code
    {
        get => _code;
        set => Set(ref _code, value);
    }

    public CodeType SelectedCodeType
    {
        get => _codeType;
        set
        {
            if (Set(ref _codeType, value)) Notify(nameof(BindingExample));
        }
    }

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            if (Set(ref _isReadOnly, value)) Notify(nameof(BindingExample));
        }
    }

    public bool ShowLineNumbers
    {
        get => _showLineNumbers;
        set
        {
            if (Set(ref _showLineNumbers, value)) Notify(nameof(BindingExample));
        }
    }

    public bool EnableHighlighting
    {
        get => _highlight;
        set
        {
            if (Set(ref _highlight, value)) Notify(nameof(BindingExample));
        }
    }

    public bool WordWrap
    {
        get => _wordWrap;
        set => Set(ref _wordWrap, value);
    }

    public string BindingExample =>
        $"CodeType=\"{SelectedCodeType}\"\nIsReadOnly=\"{IsReadOnly}\"\nShowLineNumbers=\"{ShowLineNumbers}\"\nIsSyntaxHighlightingEnabled=\"{EnableHighlighting}\"";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void LoadSample()
    {
        Code = SelectedCodeType switch
        {
            CodeType.CSharp =>
                "using System;\n\nnamespace TheOne.Sample;\n\npublic sealed class Greeting\n{\n    // Native input, selection and undo\n    public string SayHello(string name)\n    {\n        return $\"你好，{name}\";\n    }\n}",
            CodeType.JavaScript =>
                "// A simple greeting\nconst greeting = (name) => {\n  return `Hello, ${name}`;\n};\n\nconsole.log(greeting(\"TheOne\"));",
            CodeType.TypeScript =>
                "interface Settings {\n  theme: string;\n  readOnly: boolean;\n}\n\nconst settings: Settings = {\n  theme: \"Light\",\n  readOnly: false\n};",
            CodeType.Json => JsonSample,
            CodeType.Xml =>
                "<?xml version=\"1.0\"?>\n<!-- Application settings -->\n<Settings theme=\"Light\">\n  <Editor tabSize=\"4\" />\n</Settings>",
            CodeType.Axaml =>
                "<UserControl xmlns=\"https://github.com/avaloniaui\"\n             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n  <StackPanel Spacing=\"12\">\n    <TextBlock Text=\"TheOne\" />\n    <Button Content=\"Hello\" />\n  </StackPanel>\n</UserControl>",
            CodeType.Html =>
                "<!DOCTYPE html>\n<html lang=\"zh-CN\">\n  <body>\n    <h1>Hello, TheOne</h1>\n  </body>\n</html>",
            CodeType.Css =>
                "/* Application theme */\n.editor {\n  color: #006061;\n  padding: 16px;\n  font-family: monospace;\n}\n",
            CodeType.Sql =>
                "-- Query active applications\nSELECT name, theme\nFROM applications\nWHERE enabled = true\nORDER BY name\nLIMIT 20;",
            CodeType.Python =>
                "# Application settings\ndef greet(name: str) -> str:\n    return f\"你好，{name}\"\n\nsettings = {\n    \"theme\": \"Light\",\n    \"readonly\": False\n}\nprint(greet(\"TheOne\"))",
            CodeType.Yaml =>
                "# Application settings\napplication:\n  name: TheOne Studio\n  theme: Light\neditor:\n  readOnly: false\n  tabSize: 4\n",
            CodeType.Markdown =>
                "# TheOne Studio\n\nA lightweight **code editor** for Avalonia.\n\n- Compiled binding\n- Read-only selection\n\n[Documentation](https://docs.avaloniaui.net/)\n\n```json\n{ \"theme\": \"Light\" }\n```",
            CodeType.C =>
                "#include <stdio.h>\n\nint main(void) {\n    // A lightweight editor\n    printf(\"Hello, TheOne\\n\");\n    return 0;\n}",
            CodeType.Cpp =>
                "#include <iostream>\n\nnamespace sample {\n    class Greeting {\n    public:\n        void hello() { std::cout << \"Hello, TheOne\"; }\n    };\n}",
            CodeType.Java =>
                "package sample;\n\npublic class Greeting {\n    public static void main(String[] args) {\n        System.out.println(\"Hello, TheOne\");\n    }\n}",
            CodeType.Go =>
                "package main\n\nimport \"fmt\"\n\nfunc main() {\n    message := \"Hello, TheOne\"\n    fmt.Println(message)\n}",
            CodeType.Rust =>
                "use std::collections::HashMap;\n\nfn main() {\n    let mut settings = HashMap::new();\n    settings.insert(\"theme\", \"Light\");\n    println!(\"{:?}\", settings);\n}",
            CodeType.Kotlin =>
                "package sample\n\nfun main() {\n    val message: String = \"Hello, TheOne\"\n    println(message)\n}",
            CodeType.Swift =>
                "import Foundation\n\nstruct Greeting {\n    let name: String\n    func hello() -> String {\n        return \"Hello, TheOne\"\n    }\n}",
            CodeType.Php =>
                "<?php\n// Application greeting\nfunction greet(string $name): string {\n    return \"Hello, \" . $name;\n}\necho greet(\"TheOne\");",
            CodeType.Ruby =>
                "# Application greeting\ndef greet(name)\n  puts \"Hello, #{name}\"\nend\n\ngreet(\"TheOne\")",
            CodeType.Bash =>
                "#!/usr/bin/env bash\n# Application greeting\nfunction greet() {\n    local name=\"TheOne\"\n    printf '%s\\n' \"Hello, $name\"\n}\ngreet",
            CodeType.PowerShell =>
                "# Application greeting\nfunction Get-Greeting {\n    param([string]$Name)\n    return \"Hello, $Name\"\n}\nGet-Greeting -Name 'TheOne'",
            CodeType.Dart =>
                "import 'dart:core';\n\nvoid main() {\n    final message = 'Hello, TheOne';\n    print(message);\n}",
            CodeType.Lua =>
                "-- Application greeting\nlocal function greet(name)\n    return 'Hello, ' .. name\nend\n\nprint(greet('TheOne'))",
            CodeType.Toml =>
                "# Application settings\n[application]\nname = \"TheOne Studio\"\nenabled = true\n\n[editor]\ntab_size = 4\n",
            CodeType.Ini =>
                "; Application settings\n[application]\nname = TheOne Studio\nenabled = true\n\n[editor]\ntab_size = 4\n",
            _ => "Plain text / 纯文本\n\nText can be selected, copied and edited.\n只读模式保留选择、复制和滚动。"
        };
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name);
        return true;
    }

    private void Notify(string? name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
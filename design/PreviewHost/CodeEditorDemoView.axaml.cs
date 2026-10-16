using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using TheOne.Avalonia.Controls;

namespace TheOne.DesignPreview;

public partial class CodeEditorDemoView : UserControl
{
    public CodeEditorDemoView()
    {
        DataContext = Model;
        AvaloniaXamlLoader.Load(this);
        SizeChanged += (_, _) => UpdateLayoutMode();
    }

    public CodeEditorDemoViewModel Model { get; } = new();

    private void LoadSample(object? sender, RoutedEventArgs e)
    {
        Model.LoadSample();
    }

    private void LoadLargeSample(object? sender, RoutedEventArgs e)
    {
        Model.SelectedCodeType = CodeType.CSharp;
        Model.IsReadOnly = false;
        Model.Code = string.Join('\n',
            Enumerable.Range(1, 10000).Select(i =>
                $"var item{i:D5} = new {{ Name = \"代码行 {i}\", Value = {i} }}; // lightweight editor"));
        var editor = this.FindControl<CodeEditor>("Editor")!;
        editor.Header = "10000-lines.cs";
        editor.CaretIndex = 0;
        editor.FocusEditor();
    }

    private async void Complete(object? sender, RoutedEventArgs e)
    {
        var editor = this.FindControl<CodeEditor>("Editor")!;
        editor.FocusEditor();
        await editor.RequestCompletionAsync();
    }

    private async void LoadIntelligenceSample(object? sender, RoutedEventArgs e)
    {
        Model.SelectedCodeType = CodeType.CSharp;
        Model.IsReadOnly = false;
        Model.Code =
            "class User\n{\n    public string Name { get; set; }\n    public string Greet(string name, int count = 1)\n    {\n        return name;\n    }\n}\n\nvar user = new User();\nvar broken = ; // Hover the underline or press F8\nuser.Greet(\"Ada\", )";
        var editor = this.FindControl<CodeEditor>("Editor")!;
        editor.Header = "intelligence.cs";
        editor.CaretIndex = Model.Code.Length - 1;
        editor.FocusEditor();
        await editor.RefreshDiagnosticsAsync();
        await editor.RequestSignatureHelpAsync();
    }

    private async void LoadSnippetSample(object? sender, RoutedEventArgs e)
    {
        Model.SelectedCodeType = CodeType.CSharp;
        Model.IsReadOnly = false;
        Model.Code =
            "class User\n{\n    // method / func / prop / propfull / ctor + Tab Tab\n    method\n}\n\n// for / foreach / forr + Tab Tab\n";
        var editor = this.FindControl<CodeEditor>("Editor")!;
        editor.Header = "snippets.cs";
        editor.CaretIndex = Model.Code.IndexOf("    method\n", StringComparison.Ordinal) + 10;
        editor.FocusEditor();
        await editor.RequestCompletionAsync();
    }

    private async void ShowSignature(object? sender, RoutedEventArgs e)
    {
        var editor = this.FindControl<CodeEditor>("Editor")!;
        editor.FocusEditor();
        await editor.RequestSignatureHelpAsync();
    }

    private async void CheckCode(object? sender, RoutedEventArgs e)
    {
        var editor = this.FindControl<CodeEditor>("Editor")!;
        await editor.RefreshDiagnosticsAsync();
        editor.GoToNextDiagnostic();
    }

    private void UpdateLayoutMode()
    {
        var narrow = Bounds.Width < 850;
        var main = this.FindControl<Grid>("MainLayout")!;
        var properties = this.FindControl<Border>("PropertiesSection")!;
        main.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,28,300");
        Grid.SetColumn(properties, narrow ? 0 : 2);
        Grid.SetRow(properties, narrow ? 1 : 0);
        properties.Margin = narrow ? new Thickness(0, 20, 0, 0) : default;
        properties.Padding = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(20, 0, 0, 0);
        properties.BorderThickness = narrow ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
        var samples = this.FindControl<Grid>("SamplesLayout")!;
        var second = this.FindControl<StackPanel>("NoNumbersSection")!;
        samples.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,24,*");
        Grid.SetColumn(second, narrow ? 0 : 2);
        Grid.SetRow(second, narrow ? 1 : 0);
        second.Margin = narrow ? new Thickness(0, 20, 0, 0) : default;
    }
}
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;

namespace TheOne.DesignPreview;

internal static class Program
{
    internal static bool IsHeadless { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            Run(args);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Environment.ExitCode = 1;
        }
    }

    private static void Run(string[] args)
    {
        if (args.Contains("--require-aot") && RuntimeFeature.IsDynamicCodeSupported)
            throw new InvalidOperationException("This check requires a real NativeAOT executable.");
        var command = args.FirstOrDefault(a => a.StartsWith("--verify-", StringComparison.Ordinal));
        IsHeadless = command is not null;
        var builder = AppBuilder.Configure<EditorPreviewApp>().UseSkia();
        if (!IsHeadless)
        {
            builder.UsePlatformDetect().StartWithClassicDesktopLifetime(args);
            return;
        }

        builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithLifetime(new ClassicDesktopStyleApplicationLifetime
                { ShutdownMode = ShutdownMode.OnExplicitShutdown });
        var index = Array.IndexOf(args, command!);
        var output = args.Length > index + 1 && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[index + 1]
            : null;

        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            Console.WriteLine("PASS: " + message);
        }

        switch (command)
        {
            case "--verify-codeeditor": CodeEditorPreview.Verify(Check, output); break;
            case "--verify-codeeditor-functional": CodeEditorPreview.VerifyFunctional(Check, output); break;
            case "--verify-codeeditor-performance": CodeEditorPreview.VerifyPerformance(Check, output); break;
            case "--verify-codeeditor-semantics": CodeEditorPreview.Semantics(Check, output); break;
            case "--verify-codeeditor-contention": CodeEditorPreview.Contention(Check, output); break;
            case "--verify-codeeditor-rendering": CodeEditorPreview.RangeAndSemanticRendering(Check, output); break;
            case "--verify-codeeditor-highlighting": CodeEditorPreview.Highlighting(Check, output); break;
            default: throw new ArgumentException("Unknown verification command: " + command);
        }
    }
}

internal sealed class EditorPreviewApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (!Program.IsHeadless && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new Window
            {
                Title = "CodeEditor · Independent Preview", Width = 1200, Height = 900,
                Content = new ScrollViewer { Content = new CodeEditorDemoView { Margin = new Thickness(24) } }
            };
        base.OnFrameworkInitializationCompleted();
    }
}
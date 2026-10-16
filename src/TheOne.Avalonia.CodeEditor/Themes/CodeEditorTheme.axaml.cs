using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace TheOne.Avalonia.Themes;

/// <summary>Standalone editor resources and native control styles. Register explicitly in Application.Styles.</summary>
public class CodeEditorTheme : Styles
{
    public CodeEditorTheme()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
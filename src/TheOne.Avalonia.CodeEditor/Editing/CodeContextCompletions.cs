namespace TheOne.Avalonia.Controls;

// Bounded line-context completion for declarative languages. Catalogs are shared;
// no regex, schema download, per-token controls, or whole-document scan per request.
internal static class CodeContextCompletions
{
    private static readonly string[] HtmlTags =
        "html head body title meta link main header footer nav section article aside div span p a button input label form select option textarea img ul ol li table thead tbody tr th td script style pre code canvas svg"
            .Split(' ');

    private static readonly HashSet<string> VoidTags =
        new("meta link input img br hr source area base col embed param track wbr".Split(' '),
            StringComparer.OrdinalIgnoreCase);

    private static readonly string[] HtmlAttributes =
        "id class style title href src alt type value name placeholder disabled checked selected role aria-label tabindex width height data-"
            .Split(' ');

    private static readonly string[] AxamlTags =
        "Grid StackPanel DockPanel WrapPanel Border Panel TextBlock TextBox Button ComboBox ListBox ItemsControl ContentControl ScrollViewer CheckBox ToggleSwitch PathIcon Image UserControl Window"
            .Split(' ');

    private static readonly string[] AxamlAttributes =
        "Name Width Height MinWidth MinHeight MaxWidth MaxHeight Margin Padding Background Foreground BorderBrush BorderThickness CornerRadius HorizontalAlignment VerticalAlignment Content Text IsVisible IsEnabled FontSize FontWeight DataContext Command ItemsSource SelectedItem Grid.Row Grid.Column"
            .Split(' ');

    private static readonly string[] CssProperties =
        "color background background-color display position width height min-width max-width min-height max-height margin margin-top margin-right margin-bottom margin-left padding padding-top padding-right padding-bottom padding-left border border-radius border-color box-shadow font-family font-size font-weight line-height text-align text-decoration opacity overflow overflow-x overflow-y gap grid-template-columns grid-template-rows align-items justify-content flex flex-direction flex-wrap transition transform cursor z-index"
            .Split(' ');

    private static readonly string[] CssValues =
        "auto none inherit initial unset block inline inline-block flex grid contents relative absolute fixed sticky static hidden visible scroll center start end stretch space-between row column wrap nowrap transparent currentColor solid pointer normal bold border-box content-box red green blue white black"
            .Split(' ');

    internal static IReadOnlyList<CodeCompletionItem>? Get(CodeDocument document, CodeType language, int caret,
        string prefix)
    {
        var line = document.Lines[document.LineAt(caret)];
        var text = line.Text[..Math.Clamp(caret - line.Start, 0, line.Text.Length)];
        IEnumerable<CodeCompletionItem>? items = null;
        if (CodeLanguage.Get(language).Markup)
        {
            var open = text.LastIndexOf('<');
            var close = text.LastIndexOf('>');
            if (open <= close || open < 0) return null;
            var fragment = text[(open + 1)..];
            var closing = fragment.StartsWith('/');
            if (closing) fragment = fragment[1..];
            var attributes = fragment.Any(char.IsWhiteSpace);
            var tags = language == CodeType.Html ? HtmlTags :
                language == CodeType.Axaml ? AxamlTags : document.Words.Where(w => w.Length > 0 && char.IsLetter(w[0]));
            var names = language == CodeType.Html ? HtmlAttributes : AxamlAttributes;
            if (attributes)
                items = names.Select(name => new CodeCompletionItem(name, name + "=\"\"", "Attribute", name.Length + 2)
                    { Kind = CodeCompletionKind.Property });
            else
                items = tags.Select(tag =>
                    new CodeCompletionItem(tag,
                        closing || (language == CodeType.Html && VoidTags.Contains(tag))
                            ? tag + ">"
                            : tag + "></" + tag + ">", "Element",
                        closing || VoidTags.Contains(tag) ? -1 : tag.Length + 1) { Kind = CodeCompletionKind.Snippet });
        }
        else if (language == CodeType.Css)
        {
            if (text.TrimStart().StartsWith('.') || text.TrimStart().StartsWith('#')) return null;
            var start = Math.Max(text.LastIndexOf(';'), text.LastIndexOf('{'));
            var value = text.LastIndexOf(':') > start;
            items = value
                ? CssValues.Select(name => new CodeCompletionItem(name, name, "CSS value"))
                : CssProperties.Select(name =>
                    new CodeCompletionItem(name, name + ": ;", "CSS property", name.Length + 2)
                        { Kind = CodeCompletionKind.Property });
        }

        return items?.Where(i => i.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(i => i.Label).Take(100).ToArray();
    }
}
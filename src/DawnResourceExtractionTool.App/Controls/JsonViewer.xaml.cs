using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using DawnResourceExtractionTool.App.Services;
using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.App.Controls;

/// <summary>
/// Read-only JSON view: indented, syntax coloured, searchable, and every
/// <c>/lol-game-data/…</c> value is a link to the asset it points at.
/// </summary>
public partial class JsonViewer : UserControl
{
    /// <summary>Matches an asset reference inside a JSON string value.</summary>
    private static readonly Regex LinkPattern = new("/lol-game-data/[^\"\\s,]*", RegexOptions.Compiled);

    private string _raw = string.Empty;
    private bool _showRaw;

    public JsonViewer()
    {
        InitializeComponent();
        Editor.SyntaxHighlighting = LoadHighlighting(IsDarkTheme());
        ApplyTextColors();
        SearchPanel.Install(Editor.TextArea);
        Editor.TextArea.TextView.MouseLeftButtonUp += OnMouseLeftButtonUp;
        Editor.TextArea.TextView.MouseMove += OnMouseMove;
        Loaded += (_, _) => ThemeService.Changed += OnThemeChanged;
        Unloaded += (_, _) => ThemeService.Changed -= OnThemeChanged;
    }

    /// <summary>The syntax colours follow the window: light text on dark for the dark theme.</summary>
    private void OnThemeChanged()
    {
        Editor.SyntaxHighlighting = LoadHighlighting(IsDarkTheme());
        ApplyTextColors();
    }

    /// <summary>AvalonEdit does not read the palette itself, so the text colour is handed over here.</summary>
    private void ApplyTextColors()
    {
        if (TryFindResource("BodyTextBrush") is not System.Windows.Media.Brush brush) return;
        Editor.Foreground = brush;
        Editor.TextArea.Foreground = brush;
    }

    private static bool IsDarkTheme() => ThemeService.Effective(ThemeService.Current) == AppTheme.Dark;

    /// <summary>Raised with the raw asset reference when a path in the text is clicked.</summary>
    public event EventHandler<string>? LinkActivated;

    /// <summary>Raw text of the entry; the view formats it when it parses as JSON.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(JsonViewer),
        new PropertyMetadata(null, (sender, _) => ((JsonViewer)sender).ApplyText()));

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>True when the text parsed as JSON and is shown indented.</summary>
    public bool IsJson { get; private set; }

    private void ApplyText()
    {
        _raw = Text ?? string.Empty;
        _showRaw = false;
        ModeButton.Content = Strings.T("Win.Json.Raw");
        ModeButton.IsEnabled = _raw.Length > 0;

        var formatted = Format(_raw, out var parsed);
        IsJson = parsed;
        Editor.Text = parsed ? formatted : _raw;
        HintText.Text = parsed
            ? Strings.T("Win.Json.Hint")
            : Strings.T("Win.Json.NotJson");
    }

    /// <summary>
    /// Indents the text with <see cref="Utf8JsonWriter"/>; <paramref name="parsed"/> is false when the
    /// text is not JSON at all (an <c>.bin</c> that slipped through, a truncated file).
    /// </summary>
    private static string Format(string text, out bool parsed)
    {
        parsed = false;
        if (string.IsNullOrWhiteSpace(text)) return text;

        var trimmed = text.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '[')) return text;

        try
        {
            using var document = JsonDocument.Parse(text);
            using var stream = new MemoryStream(text.Length * 2);
            // The default encoder would turn every Chinese name into \uXXXX escapes; keep the text readable.
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
                   {
                       Indented = true,
                       Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                   }))
            {
                document.WriteTo(writer);
            }

            parsed = true;
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return text;
        }
    }

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsJson) return;
        _showRaw = !_showRaw;
        ModeButton.Content = _showRaw ? Strings.T("Win.Json.Format") : Strings.T("Win.Json.Raw");
        Editor.Text = _showRaw ? _raw : Format(_raw, out _);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var link = LinkAt(e.GetPosition(Editor.TextArea.TextView));
        Editor.Cursor = link is null ? Cursors.IBeam : Cursors.Hand;
        Editor.ToolTip = link is null ? null : Strings.T("Win.Json.OpenLink", link);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (LinkAt(e.GetPosition(Editor.TextArea.TextView)) is not { } link) return;
        LinkActivated?.Invoke(this, link);
        e.Handled = true;
    }

    /// <summary>The asset reference under a point of the text view, or null.</summary>
    private string? LinkAt(Point point)
    {
        var position = Editor.GetPositionFromPoint(point);
        if (position is null) return null;

        var document = Editor.Document;
        if (position.Value.Line < 1 || position.Value.Line > document.LineCount) return null;
        var line = document.GetLineByNumber(position.Value.Line);
        var text = document.GetText(line);

        var column = Math.Max(0, position.Value.Column - 1);
        foreach (Match match in LinkPattern.Matches(text))
        {
            if (column < match.Index || column > match.Index + match.Length) continue;
            return match.Value.TrimEnd('.', ',', ')');
        }

        return null;
    }

    /// <summary>
    /// JSON highlighting with a colour of its own for keys. The built-in definition paints keys and
    /// values the same, which makes a 200k line table unreadable.
    /// </summary>
    private static IHighlightingDefinition LoadHighlighting(bool dark)
    {
        var (key, text, number, literal) = dark
            ? ("#9CC7FF", "#F08A8A", "#7FD1A6", "#C7A9F5")
            : ("#0451A5", "#B31D28", "#098658", "#8250DF");

        var xshd = $$"""
            <SyntaxDefinition name="JsonSource" extensions=".json" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="Key" foreground="{{key}}" fontWeight="bold" />
              <Color name="String" foreground="{{text}}" />
              <Color name="Number" foreground="{{number}}" />
              <Color name="Literal" foreground="{{literal}}" />
              <RuleSet>
                <Rule color="Key">"([^"\\]|\\.)*"\s*:</Rule>
                <Span color="String" begin="&quot;" end="&quot;" />
                <Rule color="Number">-?\b\d+(\.\d+)?([eE][+-]?\d+)?\b</Rule>
                <Rule color="Literal">\b(true|false|null)\b</Rule>
              </RuleSet>
            </SyntaxDefinition>
            """;

        using var reader = new StringReader(xshd);
        using var xml = System.Xml.XmlReader.Create(reader);
        return HighlightingLoader.Load(xml, HighlightingManager.Instance);
    }
}

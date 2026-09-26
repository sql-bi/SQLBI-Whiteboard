using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard.Import;

/// <summary>
/// What an import brought back: the deck, a picture per slide, and how to lay them out.
/// </summary>
internal sealed record DeckImport(
    DeckInfo Deck,
    IReadOnlyList<SlidePicture> Pictures,
    SlideArrangement Arrangement,
    bool Frames);

/// <summary>
/// The PowerPoint import dialog. It opens the deck in PowerPoint as it appears, so it
/// can say how many slides, hidden slides, and sections there are, and it stays open
/// while the slides are exported, with their progress and a way to stop.
/// </summary>
public partial class PowerPointImportWindow : Window
{
    private readonly string _path;
    private readonly PowerPointImportSettings _settings;
    private readonly Action _persistSettings;

    private PowerPointDeck? _deck;
    private CancellationTokenSource? _export;
    private bool _finished;
    private bool _closed;

    public PowerPointImportWindow(string path, PowerPointImportSettings settings, Action persistSettings)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(persistSettings);
        _path = path;
        _settings = settings;
        _persistSettings = persistSettings;
        InitializeComponent();
        DeckName.Text = Path.GetFileName(path);
        PopulateOptions();
    }

    /// <summary>
    /// The slides to put on the board, or null when nothing was imported.
    /// </summary>
    internal DeckImport? Result { get; private set; }

    private void PopulateOptions()
    {
        PicturesCombo.Items.Add(new Choice("Auto", SlidePictures.Auto));
        PicturesCombo.Items.Add(new Choice("Sharp at any zoom", SlidePictures.Svg));
        PicturesCombo.Items.Add(new Choice("Exact look", SlidePictures.Png));
        Select(PicturesCombo, _settings.Pictures);

        foreach (var width in PowerPointImportSettings.PngWidthChoices)
        {
            ResolutionCombo.Items.Add(new Choice($"{width} pixels wide", width));
        }

        Select(ResolutionCombo, _settings.PngWidth);

        LayoutCombo.Items.Add(new Choice("A row per section", SlideArrangement.RowPerSection));
        LayoutCombo.Items.Add(new Choice("One row", SlideArrangement.OneRow));
        LayoutCombo.Items.Add(new Choice("One column", SlideArrangement.OneColumn));
        Select(LayoutCombo, _settings.Arrangement);

        FramesSwitch.IsChecked = _settings.Frames;
        HiddenSwitch.IsChecked = _settings.IncludeHidden;
        ShowPicturesHint();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _deck = await PowerPointDeck.OpenAsync(_path);
        }
        catch (Exception exception)
        {
            DeckFacts.Text = "PowerPoint could not open it.";
            Summary.Text = exception.Message;
            CancelButton.Content = "Close";
            return;
        }

        // Closed while PowerPoint was still opening it: nothing else will let it go.
        if (_closed)
        {
            await _deck.DisposeAsync();
            _deck = null;
            return;
        }

        var info = _deck.Info;
        var hidden = info.Slides.Count(slide => slide.Hidden);
        var facts = new List<string> { Count(info.Slides.Count, "slide") };
        if (hidden > 0)
        {
            facts.Add($"{hidden} hidden");
        }

        if (info.Sections.Count > 1)
        {
            facts.Add(Count(info.Sections.Count, "section"));
        }

        DeckFacts.Text = string.Join(", ", facts);
        HiddenRow.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
        HiddenLabel.Text = $"Include hidden slides ({hidden})";
        Options.IsEnabled = true;
        ImportButton.IsEnabled = info.Slides.Count > 0;
        ImportButton.Focus();
    }

    private void PicturesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowPicturesHint();

    private void ShowPicturesHint()
    {
        var pictures = Selected<SlidePictures>(PicturesCombo);
        ResolutionCombo.IsEnabled = pictures != SlidePictures.Svg;
        PicturesHint.Text = pictures switch
        {
            SlidePictures.Svg => "Every slide as SVG: sharp at any zoom, text as PowerPoint wrote it.",
            SlidePictures.Png => "Every slide as PNG, exactly as PowerPoint draws it.",
            _ => "SVG, sharp at any zoom, and PNG for a slide whose fonts are not on this PC.",
        };
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_deck is null || _export is not null)
        {
            return;
        }

        if (_finished)
        {
            Close();
            return;
        }

        ReadSettings();
        _persistSettings();
        var slides = _deck.Info.Slides.Where(slide => _settings.IncludeHidden || !slide.Hidden).ToArray();
        if (slides.Length == 0)
        {
            Summary.Text = "Every slide in this deck is hidden.";
            return;
        }

        // SVG is read from the clipboard, so what was there is kept and put back.
        var clipboard = _settings.Pictures == SlidePictures.Png ? null : SlideClipboard.Snapshot.Take();
        _export = new CancellationTokenSource();
        SetBusy(true, slides.Length);
        IReadOnlyList<SlidePicture> pictures;
        try
        {
            var progress = new Progress<int>(done =>
            {
                Progress.Value = done;
                Summary.Text = $"Slide {Math.Min(done + 1, slides.Length)} of {slides.Length}";
            });
            pictures = await _deck.ExportAsync(
                slides,
                _settings.Pictures,
                _settings.PngWidth,
                SvgImageCodec.CanDrawAsAuthored,
                progress,
                _export.Token);
        }
        catch (OperationCanceledException)
        {
            clipboard?.Restore();
            Summary.Text = string.Empty;
            SetBusy(false, 0);
            return;
        }
        catch (Exception exception)
        {
            clipboard?.Restore();
            Summary.Text = exception.Message;
            SetBusy(false, 0);
            return;
        }
        finally
        {
            _export?.Dispose();
            _export = null;
        }

        var restored = clipboard?.Restore() ?? true;
        Result = new DeckImport(_deck.Info, pictures, _settings.Arrangement, _settings.Frames);

        var notes = new List<string>();
        var fonts = pictures.Count(picture => picture.Fallback == SlideFallback.Fonts);
        var capture = pictures.Count(picture => picture.Fallback == SlideFallback.Capture);
        if (fonts > 0)
        {
            notes.Add($"{Count(fonts, "slide")} {(fonts == 1 ? "uses" : "use")} a picture because {(fonts == 1 ? "its" : "their")} fonts are not on this PC.");
        }

        if (capture > 0)
        {
            notes.Add($"{Count(capture, "slide")} {(capture == 1 ? "uses" : "use")} a picture because PowerPoint did not hand over {(capture == 1 ? "its" : "their")} SVG.");
        }

        if (!restored)
        {
            notes.Add("What was on the clipboard before could not be put back.");
        }

        if (notes.Count == 0)
        {
            Close();
            return;
        }

        // Said before the dialog goes, so the board it returns to is not a surprise.
        _finished = true;
        SetBusy(false, 0);
        Options.IsEnabled = false;
        Summary.Text = string.Join(Environment.NewLine, notes);
        ImportButton.Content = "Done";
        CancelButton.Visibility = Visibility.Collapsed;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_export is not null)
        {
            _export.Cancel();
            return;
        }

        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (_finished)
        {
            Close();
        }
        else
        {
            CancelButton_Click(this, new RoutedEventArgs());
        }

        e.Handled = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e) => _export?.Cancel();

    private async void Window_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        if (_deck is { } deck)
        {
            _deck = null;
            await deck.DisposeAsync();
        }
    }

    private void ReadSettings()
    {
        _settings.Pictures = Selected<SlidePictures>(PicturesCombo);
        _settings.PngWidth = Selected<int>(ResolutionCombo);
        _settings.Arrangement = Selected<SlideArrangement>(LayoutCombo);
        _settings.Frames = FramesSwitch.IsChecked == true;
        _settings.IncludeHidden = HiddenSwitch.IsChecked == true;
    }

    private void SetBusy(bool busy, int total)
    {
        Options.IsEnabled = !busy;
        ImportButton.IsEnabled = !busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Progress.Maximum = Math.Max(1, total);
        Progress.Value = 0;
        if (busy)
        {
            Summary.Text = $"Slide 1 of {total}";
        }
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static void Select(ComboBox combo, object value) =>
        combo.SelectedItem = combo.Items.OfType<Choice>().FirstOrDefault(choice => choice.Value.Equals(value))
            ?? combo.Items[0];

    private static T Selected<T>(ComboBox combo) => combo.SelectedItem is Choice { Value: T value }
        ? value
        : (T)((Choice)combo.Items[0]).Value;

    // Title and IsSeparator are what the settings combo's item template binds,
    // so a choice here is drawn like one in Preferences.
    private sealed record Choice(string Title, object Value)
    {
        public bool IsSeparator => false;

        public override string ToString() => Title;
    }
}

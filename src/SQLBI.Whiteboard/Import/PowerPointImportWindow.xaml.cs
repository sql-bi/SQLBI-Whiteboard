using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
    private const double LayoutSampleWidth = 64;
    private const double LayoutSampleHeight = 40;

    private readonly string _path;
    private readonly PowerPointImportSettings _settings;
    private readonly Action _persistSettings;

    private PowerPointDeck? _deck;
    private CancellationTokenSource? _export;
    private bool _finished;
    private bool _closed;
    private SlidePictures _pictures;
    private SlideArrangement _arrangement;

    public PowerPointImportWindow(string path, PowerPointImportSettings settings, Action persistSettings)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(persistSettings);
        _path = path;
        _settings = settings;
        _persistSettings = persistSettings;
        _pictures = settings.Pictures;
        _arrangement = settings.Arrangement;
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
        // Each choice carries its description on the tile, so it is read before it is
        // chosen; a tooltip would ask for a hover that a pen or a finger cannot give.
        AddTiles(PicturesChoices, "Pictures", _pictures, value => _pictures = value, OnPicturesChanged,
        [
            (SlidePictures.Auto, TextTile("Auto", "SVG, or PNG for a slide whose fonts are missing")),
            (SlidePictures.Svg, TextTile("SVG", "Sharp at any zoom, drawn from the deck's fonts")),
            (SlidePictures.Png, TextTile("PNG", "Exactly as PowerPoint draws it, at a fixed size")),
        ]);

        AddTiles(LayoutChoices, "Layout", _arrangement, value => _arrangement = value, onChanged: null,
        [
            (SlideArrangement.RowPerSection, DrawnTile(RowPerSectionSample(), "A row per section")),
            (SlideArrangement.OneRow, DrawnTile(OneRowSample(), "One row")),
            (SlideArrangement.OneColumn, DrawnTile(OneColumnSample(), "One column")),
        ]);

        foreach (var width in PowerPointImportSettings.PngWidthChoices)
        {
            ResolutionCombo.Items.Add(new Choice($"{width} pixels wide", width));
        }

        ResolutionCombo.SelectedItem = ResolutionCombo.Items.OfType<Choice>()
            .FirstOrDefault(choice => choice.Value.Equals(_settings.PngWidth)) ?? ResolutionCombo.Items[0];
        ShowResolution();

        FramesSwitch.IsChecked = _settings.Frames;
        HiddenSwitch.IsChecked = _settings.IncludeHidden;
        OnPicturesChanged();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // PowerPoint takes a few seconds to start and to open a deck, and nothing here
        // can be chosen until it has said what is in it, so the wait is said out loud.
        Cursor = Cursors.Wait;
        var opening = new Progress<OpenProgress>(report =>
        {
            OpeningProgress.IsIndeterminate = report.Total == 0;
            if (report.Total > 0)
            {
                OpeningProgress.Maximum = report.Total;
                OpeningProgress.Value = report.Done;
                DeckFacts.Text = $"{report.Step}: {report.Done * 100 / report.Total}%";
            }
            else
            {
                DeckFacts.Text = report.Step + "…";
            }
        });

        try
        {
            _deck = await PowerPointDeck.OpenAsync(_path, opening);
        }
        catch (Exception exception)
        {
            Cursor = null;
            OpeningProgress.Visibility = Visibility.Collapsed;
            DeckFacts.Text = "PowerPoint could not open it.";
            Summary.Text = exception.Message;
            CancelButton.Content = "Close";
            return;
        }
        finally
        {
            Cursor = null;
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

        OpeningProgress.Visibility = Visibility.Collapsed;
        DeckFacts.Text = string.Join(", ", facts);

        // The switch stays when there is nothing for it to do, so it is where it is
        // expected on the next deck; it says why it cannot be used.
        HiddenSwitch.IsEnabled = hidden > 0;
        HiddenHint.Text = hidden > 0 ? $"{Count(hidden, "slide")} in this deck" : "This deck has none";
        Options.IsEnabled = true;
        ImportButton.IsEnabled = info.Slides.Count > 0;
        ImportButton.Focus();
    }

    private void OnPicturesChanged()
    {
        // Resolution is how wide a PNG is, and SVG makes none.
        var usesPng = _pictures != SlidePictures.Svg;
        ResolutionLine.Visibility = usesPng && ResolutionCombo.Visibility != Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!usesPng)
        {
            ResolutionCombo.Visibility = Visibility.Collapsed;
        }
    }

    private void ResolutionLink_Click(object sender, RoutedEventArgs e)
    {
        ResolutionLine.Visibility = Visibility.Collapsed;
        ResolutionCombo.Visibility = Visibility.Visible;
        ResolutionCombo.Focus();
        ResolutionCombo.IsDropDownOpen = true;
    }

    private void ResolutionCombo_DropDownClosed(object? sender, EventArgs e)
    {
        ShowResolution();
        ResolutionCombo.Visibility = Visibility.Collapsed;
        OnPicturesChanged();
    }

    private void ShowResolution() =>
        ResolutionText.Text = ResolutionCombo.SelectedItem is Choice choice ? choice.Title : string.Empty;

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
        // Escape in the open resolution list closes the list, not the dialog.
        if (e.Key != Key.Escape || ResolutionCombo.IsDropDownOpen)
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
        _settings.Pictures = _pictures;
        _settings.PngWidth = ResolutionCombo.SelectedItem is Choice { Value: int width }
            ? width
            : PowerPointImportSettings.DefaultPngWidth;
        _settings.Arrangement = _arrangement;
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

    /// <summary>
    /// A row of tiles of which one is chosen, drawn like the pictured rows in Preferences.
    /// </summary>
    private void AddTiles<T>(
        UniformGrid host,
        string name,
        T current,
        Action<T> choose,
        Action? onChanged,
        IReadOnlyList<(T Value, (FrameworkElement Content, string Title) Tile)> choices)
        where T : struct, Enum
    {
        var tiles = new List<ToggleButton>();
        foreach (var (value, (content, title)) in choices)
        {
            var tile = new ToggleButton
            {
                Style = (Style)FindResource("SettingsSampleSegment"),
                Content = content,
                IsChecked = value.Equals(current),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(tile, $"{name}: {title}");
            tile.Click += (_, _) =>
            {
                foreach (var other in tiles)
                {
                    other.IsChecked = ReferenceEquals(other, tile);
                }

                choose(value);
                onChanged?.Invoke();
            };
            tiles.Add(tile);
            host.Children.Add(tile);
        }
    }

    private (FrameworkElement Content, string Title) TextTile(string title, string description)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("SettingsTextBrush"),
            TextAlignment = TextAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("ImportHint"),
            Text = description,
            Margin = new Thickness(0, 4, 0, 0),
            TextAlignment = TextAlignment.Center,
        });
        return (content, title);
    }

    private (FrameworkElement Content, string Title) DrawnTile(FrameworkElement sample, string title)
    {
        var content = new StackPanel();
        sample.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(sample);
        content.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("SettingsValueLabel"),
            Text = title,
            Margin = new Thickness(0, 8, 0, 0),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        });
        return (content, title);
    }

    // The three layouts as the board will hold them: slides as small cards, the first
    // one in the accent so the reading order is visible.
    private static FrameworkElement RowPerSectionSample() => LayoutSample([(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (0, 2), (1, 2), (2, 2)]);

    private static FrameworkElement OneRowSample() => LayoutSample([(0, 1), (1, 1), (2, 1), (3, 1), (4, 1)]);

    private static FrameworkElement OneColumnSample() => LayoutSample([(2, 0), (2, 1), (2, 2)]);

    private static FrameworkElement LayoutSample(IReadOnlyList<(int Column, int Row)> slides)
    {
        const double SlideWidth = 10;
        const double SlideHeight = 6;
        const double Gap = 2;
        var scene = PreferencesWindow.SampleScene(LayoutSampleWidth, LayoutSampleHeight);
        var columns = slides.Max(slide => slide.Column) + 1;
        var rows = 3;
        var left = (scene.Width - (columns * SlideWidth) - ((columns - 1) * Gap)) / 2;
        var top = (scene.Height - (rows * SlideHeight) - ((rows - 1) * Gap)) / 2;
        for (var index = 0; index < slides.Count; index++)
        {
            var (column, row) = slides[index];
            PreferencesWindow.SampleBlock(
                scene,
                left + (column * (SlideWidth + Gap)),
                top + (row * (SlideHeight + Gap)),
                SlideWidth,
                SlideHeight,
                index == 0 ? PreferencesWindow.SampleAccentBrush : PreferencesWindow.SampleGhostBrush,
                radius: 1);
        }

        return PreferencesWindow.SampleFrame(scene);
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    // Title and IsSeparator are what the settings combo's item template binds,
    // so a choice here is drawn like one in Preferences.
    private sealed record Choice(string Title, object Value)
    {
        public bool IsSeparator => false;

        public override string ToString() => Title;
    }
}

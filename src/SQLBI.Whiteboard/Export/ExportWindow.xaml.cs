using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SQLBI.Whiteboard.Core.Export;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Settings;
using SQLBI.Whiteboard.Core.Viewport;

namespace SQLBI.Whiteboard.Export;

/// <summary>
/// The export dialog: the board with the proposed areas outlined and numbered,
/// and the few settings that change that preview live.
/// </summary>
public partial class ExportWindow : Window
{
    private const int PreviewBoxWidth = 1400;
    private const int PreviewBoxHeight = 900;

    private readonly BoardDocument _document;
    private readonly ExportSettings _settings;
    private readonly Func<Guid, ImageSource?>? _liveViewImageSourceProvider;
    private readonly Func<BoardObject, string?>? _titleResolver;
    private readonly string? _boardPath;
    private readonly Action _persistSettings;

    private RectD _content;
    private BitmapSource? _boardBitmap;
    private IReadOnlyList<ExportArea> _areas = [];
    private bool _suppressChange = true;
    private CancellationTokenSource? _export;

    public ExportWindow(
        BoardDocument document,
        ExportSettings settings,
        Func<Guid, ImageSource?>? liveViewImageSourceProvider,
        Func<BoardObject, string?>? titleResolver,
        string? boardPath,
        Action persistSettings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(persistSettings);
        _document = document;
        _settings = settings;
        _liveViewImageSourceProvider = liveViewImageSourceProvider;
        _titleResolver = titleResolver;
        _boardPath = boardPath;
        _persistSettings = persistSettings;
        _content = document.ContentBounds ?? new RectD(0, 0, 1, 1);
        InitializeComponent();
        PopulateOptions();
        _suppressChange = false;
        Rebuild();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Rendered once the window is on screen, so the dialog appears before a
        // large board has been drawn rather than after.
        Dispatcher.BeginInvoke(() =>
        {
            var (width, height) = BoardRasterizer.FitPixelSize(_content, PreviewBoxWidth, PreviewBoxHeight);
            _boardBitmap = BoardRasterizer.Render(
                _document,
                _content,
                width,
                height,
                _liveViewImageSourceProvider);
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            RefreshPreview();
        });
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        CancelButton_Click(this, new RoutedEventArgs());
        e.Handled = true;
    }

    private void Window_Closing(object sender, CancelEventArgs e) => _export?.Cancel();

    private void PopulateOptions()
    {
        FormatCombo.Items.Add(new Choice("PowerPoint", ExportFormat.PowerPoint));
        FormatCombo.Items.Add(new Choice("PDF", ExportFormat.Pdf));
        Select(FormatCombo, _settings.Format);

        PageSizeCombo.Items.Add(new Choice("A4, landscape", ExportPageSize.A4));
        PageSizeCombo.Items.Add(new Choice("Letter, landscape", ExportPageSize.Letter));
        Select(PageSizeCombo, _settings.PageSize);
        FooterSwitch.IsChecked = _settings.IncludeFooter;

        PageModelCombo.Items.Add(new Choice("One slide per area", ExportPageModel.OnePerArea));
        PageModelCombo.Items.Add(new Choice("Whole board on one slide", ExportPageModel.WholeBoard));
        Select(PageModelCombo, _settings.PageModel);

        OrderCombo.Items.Add(new Choice("Drawing order", AreaOrder.Drawing));
        OrderCombo.Items.Add(new Choice("Reading order", AreaOrder.Reading));
        Select(OrderCombo, _settings.Order);

        foreach (var points in ExportSettings.SmallestTextChoices)
        {
            SmallestTextCombo.Items.Add(new Choice(
                points.ToString(CultureInfo.CurrentCulture) + " pt",
                points));
        }

        Select(SmallestTextCombo, _settings.SmallestTextPoints);

        AspectCombo.Items.Add(new Choice("Widescreen (16:9)", ExportSlideAspect.Wide));
        AspectCombo.Items.Add(new Choice("Standard (4:3)", ExportSlideAspect.Standard));
        Select(AspectCombo, _settings.SlideAspect);

        GapSlider.Value = _settings.GapThreshold;
        OverviewSwitch.IsChecked = _settings.IncludeOverview;
        NotesSwitch.IsChecked = _settings.IncludeNotes;
    }

    private static void Select(ComboBox combo, object value)
    {
        foreach (var item in combo.Items)
        {
            if (item is Choice choice && Equals(choice.Value, value))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private static T Selected<T>(ComboBox combo, T fallback) =>
        combo.SelectedItem is Choice { Value: T value } ? value : fallback;

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressChange)
        {
            return;
        }

        ReadOptions();
        Rebuild();
    }

    private void GapSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        GapValue.Text = ((int)Math.Round(GapSlider.Value)).ToString(CultureInfo.CurrentCulture);
        Option_Changed(sender, e);
    }

    private void ReadOptions()
    {
        _settings.Format = Selected(FormatCombo, ExportFormat.PowerPoint);
        _settings.PageModel = Selected(PageModelCombo, ExportPageModel.OnePerArea);
        _settings.Order = Selected(OrderCombo, AreaOrder.Drawing);
        _settings.SmallestTextPoints = Selected(SmallestTextCombo, ExportLayoutOptions.DefaultSmallestTextPoints);
        _settings.SlideAspect = Selected(AspectCombo, ExportSlideAspect.Wide);
        _settings.PageSize = Selected(PageSizeCombo, ExportPageSize.A4);
        _settings.GapThreshold = GapSlider.Value;
        _settings.IncludeOverview = OverviewSwitch.IsChecked == true;
        _settings.IncludeNotes = NotesSwitch.IsChecked == true;
        _settings.IncludeFooter = FooterSwitch.IsChecked == true;
    }

    private void Rebuild()
    {
        GapValue.Text = ((int)Math.Round(GapSlider.Value)).ToString(CultureInfo.CurrentCulture);
        _areas = BoardExporter.Areas(_document, _settings, _titleResolver);

        var pdf = _settings.Format == ExportFormat.Pdf;
        var unit = pdf ? "page" : "slide";
        PageModelLabel.Text = pdf ? "Pages" : "Slides";
        RelabelPageModel(unit);
        SmallestTextLabel.Text = "Smallest text on a " + unit;
        OverviewLabel.Text = "Overview " + unit + " first";
        AspectLabel.Visibility = pdf ? Visibility.Collapsed : Visibility.Visible;
        AspectCombo.Visibility = AspectLabel.Visibility;
        NotesRow.Visibility = AspectLabel.Visibility;
        PageSizeLabel.Visibility = pdf ? Visibility.Visible : Visibility.Collapsed;
        PageSizeCombo.Visibility = PageSizeLabel.Visibility;
        FooterRow.Visibility = PageSizeLabel.Visibility;

        var perArea = _settings.PageModel == ExportPageModel.OnePerArea;
        OrderCombo.IsEnabled = perArea;
        GapSlider.IsEnabled = perArea;
        SmallestTextCombo.IsEnabled = perArea;
        OverviewSwitch.IsEnabled = perArea && _areas.Count > 1;

        var count = _areas.Count + (BoardExporter.HasOverview(_settings, _areas) ? 1 : 0);
        Summary.Text = count == 1
            ? "1 " + unit
            : $"{count} {unit}s" + (BoardExporter.HasOverview(_settings, _areas) ? ", the first an overview" : "");

        var scaled = _areas.Where(area => area.IsScaledDown).ToArray();
        if (scaled.Length == 0)
        {
            Warning.Visibility = Visibility.Collapsed;
        }
        else
        {
            var text = new StringBuilder();
            foreach (var area in scaled.Take(4))
            {
                if (text.Length > 0)
                {
                    text.Append(", ");
                }

                text.Append("area ").Append(area.Number).Append(" at ").Append(area.TextScalePercent).Append('%');
            }

            if (scaled.Length > 4)
            {
                text.Append(", and ").Append(scaled.Length - 4).Append(" more");
            }

            Warning.Text = "Nothing could be cut in " + text + ": the text there is smaller than asked.";
            Warning.Visibility = Visibility.Visible;
        }

        RefreshPreview();
    }

    // The page-model choices name the unit, so their labels follow the format.
    private void RelabelPageModel(string unit)
    {
        _suppressChange = true;
        var selected = Selected(PageModelCombo, ExportPageModel.OnePerArea);
        PageModelCombo.Items.Clear();
        PageModelCombo.Items.Add(new Choice($"One {unit} per area", ExportPageModel.OnePerArea));
        PageModelCombo.Items.Add(new Choice($"Whole board on one {unit}", ExportPageModel.WholeBoard));
        Select(PageModelCombo, selected);
        _suppressChange = false;
    }

    private void RefreshPreview()
    {
        if (_boardBitmap is null)
        {
            return;
        }

        var width = _boardBitmap.PixelWidth;
        var height = _boardBitmap.PixelHeight;
        var camera = new Camera2D();
        camera.Resize(width, height);
        camera.Frame(_content, BoardRasterizer.DefaultPaddingFraction);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(_boardBitmap, new Rect(0, 0, width, height));
            ExportOverlay.DrawAreas(context, camera, _areas, Math.Max(0.75, width / 1000d));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        PreviewImage.Source = bitmap;
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_export is not null || _areas.Count == 0)
        {
            return;
        }

        var boardName = string.IsNullOrWhiteSpace(_boardPath)
            ? "Untitled board"
            : Path.GetFileNameWithoutExtension(_boardPath);
        var pdf = _settings.Format == ExportFormat.Pdf;
        var dialog = new SaveFileDialog
        {
            Title = pdf ? "Export to PDF" : "Export to PowerPoint",
            Filter = pdf ? "PDF document|*.pdf" : "PowerPoint presentation|*.pptx",
            DefaultExt = pdf ? ".pdf" : ".pptx",
            AddExtension = true,
            FileName = boardName + (pdf ? ".pdf" : ".pptx"),
            InitialDirectory = string.IsNullOrWhiteSpace(_boardPath)
                ? null
                : Path.GetDirectoryName(_boardPath),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _export = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            var progress = new Progress<ExportProgress>(report =>
            {
                Progress.Maximum = Math.Max(1, report.Total);
                Progress.Value = report.Done;
                Summary.Text = report.Message;
            });
            await BoardExporter.ExportAsync(
                _document,
                _settings,
                _areas,
                dialog.FileName,
                boardName,
                _liveViewImageSourceProvider,
                _titleResolver,
                progress,
                _export.Token);
            _persistSettings();
            Close();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not export", MessageBoxButton.OK, MessageBoxImage.Error);
            SetBusy(false);
            Rebuild();
        }
        finally
        {
            _export?.Dispose();
            _export = null;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_export is not null)
        {
            _export.Cancel();
            return;
        }

        // The settings are remembered even when nothing was exported: a person
        // who tuned the threshold and left will want it back next time.
        _persistSettings();
        Close();
    }

    private void SetBusy(bool busy)
    {
        Options.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Warning.Visibility = busy ? Visibility.Collapsed : Warning.Visibility;
        if (busy)
        {
            Progress.Value = 0;
        }
    }

    // Title and IsSeparator are what the settings combo's item template binds,
    // so a choice here is drawn like one in Preferences.
    private sealed record Choice(string Title, object Value)
    {
        public bool IsSeparator => false;

        public override string ToString() => Title;
    }
}

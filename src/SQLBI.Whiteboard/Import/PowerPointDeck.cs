using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard.Import;

/// <summary>
/// How far opening a deck has got. <see cref="Total"/> is zero while the step has
/// no count to report, such as PowerPoint starting.
/// </summary>
internal readonly record struct OpenProgress(string Step, int Done, int Total);

internal sealed record DeckSlide(int Number, string? Title, int Section, bool Hidden);

internal sealed record DeckInfo(
    string Path,
    double SlideWidth,
    double SlideHeight,
    IReadOnlyList<string> Sections,
    IReadOnlyList<DeckSlide> Slides);

/// <summary>
/// Why a slide meant to be SVG is PNG instead.
/// </summary>
internal enum SlideFallback
{
    None = 0,

    /// <summary>
    /// The SVG names a font that cannot be found here, or does not decode.
    /// </summary>
    Fonts = 1,

    /// <summary>
    /// PowerPoint did not put the slide's SVG on the clipboard.
    /// </summary>
    Capture = 2,
}

/// <summary>
/// One slide as it goes on the board.
/// </summary>
internal sealed record SlidePicture(DeckSlide Slide, byte[] Bytes, bool IsSvg, SlideFallback Fallback);

/// <summary>
/// A deck open in PowerPoint, driven through COM. Every call runs on one thread of
/// its own, set up for COM, so the window stays responsive while PowerPoint works.
/// A PowerPoint the person already had open is used and left as it was, including
/// a deck they have open in it; one this started is closed when it is disposed.
/// </summary>
internal sealed class PowerPointDeck : IAsyncDisposable
{
    private const int MsoTrue = -1;
    private const int MsoFalse = 0;
    private const int PpAlertsNone = 1;

    private readonly ComThread _thread;
    private readonly string _work;
    private dynamic? _application;
    private dynamic? _presentation;
    private bool _startedPowerPoint;
    private bool _openedPresentation;
    private int? _displayAlerts;

    private PowerPointDeck(ComThread thread, string work)
    {
        _thread = thread;
        _work = work;
    }

    public DeckInfo Info { get; private set; } = null!;

    public static bool IsAvailable => Type.GetTypeFromProgID("PowerPoint.Application") is not null;

    public static async Task<PowerPointDeck> OpenAsync(string path, IProgress<OpenProgress>? progress = null)
    {
        var deck = new PowerPointDeck(new ComThread(), Directory.CreateTempSubdirectory("whiteboard-pptx-").FullName);
        try
        {
            deck.Info = await deck._thread.Run(() => deck.Open(Path.GetFullPath(path), progress));
            return deck;
        }
        catch
        {
            await deck.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The slides as pictures. SVG is read from the clipboard, because the automation
    /// model has no SVG export and copying a slide puts SVG there; the caller keeps
    /// what the clipboard held. <paramref name="canDrawSvg"/> says whether an SVG will
    /// look as PowerPoint drew it, and Auto uses PNG for a slide where it will not.
    /// </summary>
    public Task<IReadOnlyList<SlidePicture>> ExportAsync(
        IReadOnlyList<DeckSlide> slides,
        SlidePictures pictures,
        int pngWidth,
        Func<byte[], bool> canDrawSvg,
        IProgress<int>? progress,
        CancellationToken cancellationToken) =>
        _thread.Run<IReadOnlyList<SlidePicture>>(() =>
        {
            var result = new List<SlidePicture>(slides.Count);
            var pngHeight = (int)Math.Round(pngWidth * Info.SlideHeight / Info.SlideWidth);
            foreach (var slide in slides)
            {
                cancellationToken.ThrowIfCancellationRequested();
                dynamic comSlide = _presentation!.Slides[slide.Number];
                byte[]? svg = null;
                var fallback = SlideFallback.None;
                if (pictures != SlidePictures.Png)
                {
                    svg = SlideClipboard.CopySvg(comSlide);
                    if (svg is null)
                    {
                        fallback = SlideFallback.Capture;
                    }
                    else if (pictures == SlidePictures.Auto && !canDrawSvg(svg))
                    {
                        svg = null;
                        fallback = SlideFallback.Fonts;
                    }
                }

                if (svg is not null)
                {
                    result.Add(new SlidePicture(slide, svg, IsSvg: true, SlideFallback.None));
                }
                else
                {
                    var file = Path.Combine(_work, $"slide{slide.Number}.png");
                    comSlide.Export(file, "PNG", pngWidth, pngHeight);
                    result.Add(new SlidePicture(
                        slide,
                        File.ReadAllBytes(file),
                        IsSvg: false,
                        fallback));
                    File.Delete(file);
                }

                progress?.Report(result.Count);
            }

            return result;
        });

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _thread.Run(Close);
        }
        finally
        {
            _thread.Dispose();
            try
            {
                Directory.Delete(_work, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private DeckInfo Open(string path, IProgress<OpenProgress>? progress)
    {
        _startedPowerPoint = Process.GetProcessesByName("POWERPNT").Length == 0;
        progress?.Report(new OpenProgress(_startedPowerPoint ? "Starting PowerPoint" : "Connecting to PowerPoint", 0, 0));
        var type = Type.GetTypeFromProgID("PowerPoint.Application")
            ?? throw new InvalidOperationException(NotInstalledMessage);
        _application = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException(NotInstalledMessage);

        // PowerPoint has no visible window here, so its alerts are turned off and it
        // reports errors instead. The setting is restored on close, because this may be
        // the PowerPoint the person is using.
        _displayAlerts = (int)_application.DisplayAlerts;
        _application.DisplayAlerts = PpAlertsNone;

        // A deck that is already open in PowerPoint is used as it is. Opening it again
        // and closing it afterwards would close the person's open copy.
        int openCount = _application.Presentations.Count;
        for (var index = 1; index <= openCount; index++)
        {
            dynamic open = _application.Presentations[index];
            if (string.Equals((string)open.FullName, path, StringComparison.OrdinalIgnoreCase))
            {
                _presentation = open;
                break;
            }
        }

        if (_presentation is null)
        {
            progress?.Report(new OpenProgress("Opening the deck", 0, 0));
            _presentation = _application.Presentations.Open(path, MsoTrue, MsoFalse, MsoFalse);
            _openedPresentation = true;
        }

        var sections = new List<string>();
        dynamic sectionProperties = _presentation.SectionProperties;
        int sectionCount = sectionProperties.Count;
        for (var index = 1; index <= sectionCount; index++)
        {
            sections.Add((string)sectionProperties.Name(index));
        }

        var slides = new List<DeckSlide>();
        int slideCount = _presentation.Slides.Count;
        for (var number = 1; number <= slideCount; number++)
        {
            progress?.Report(new OpenProgress("Reading the slides", number - 1, slideCount));
            dynamic slide = _presentation.Slides[number];
            slides.Add(new DeckSlide(
                number,
                TitleOf(slide),
                sectionCount > 0 ? (int)slide.sectionIndex : 0,
                (int)slide.SlideShowTransition.Hidden == MsoTrue));
        }

        return new DeckInfo(
            path,
            (double)_presentation.PageSetup.SlideWidth,
            (double)_presentation.PageSetup.SlideHeight,
            sections,
            slides);
    }

    private void Close()
    {
        if (_application is null)
        {
            return;
        }

        try
        {
            if (_openedPresentation)
            {
                _presentation?.Close();
            }

            if (_displayAlerts is { } alerts)
            {
                _application.DisplayAlerts = alerts;
            }

            if (_startedPowerPoint && (int)_application.Presentations.Count == 0)
            {
                _application.Quit();
            }
        }
        catch (COMException)
        {
            // PowerPoint has already exited.
        }
        finally
        {
            Marshal.FinalReleaseComObject(_application);
            _application = null;
            _presentation = null;
        }
    }

    private static string? TitleOf(dynamic slide)
    {
        try
        {
            if ((int)slide.Shapes.HasTitle != MsoTrue)
            {
                return null;
            }

            string text = slide.Shapes.Title.TextFrame.TextRange.Text;
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (COMException)
        {
            return null;
        }
    }

    public const string NotInstalledMessage =
        "Importing a PowerPoint deck needs PowerPoint from Microsoft 365 on this PC.";

    /// <summary>
    /// One thread, set up for COM, that runs whatever it is handed in order. PowerPoint's
    /// objects belong to the thread that made them, so every call has to come from here.
    /// </summary>
    private sealed class ComThread : IDisposable
    {
        private readonly BlockingCollection<Action> _work = [];
        private readonly Thread _thread;

        public ComThread()
        {
            _thread = new Thread(() =>
            {
                foreach (var action in _work.GetConsumingEnumerable())
                {
                    action();
                }
            })
            {
                IsBackground = true,
                Name = "PowerPoint import",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public Task Run(Action action) => Run(() =>
        {
            action();
            return true;
        });

        public Task<T> Run<T>(Func<T> function)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _work.Add(() =>
            {
                try
                {
                    completion.SetResult(function());
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });
            return completion.Task;
        }

        public void Dispose()
        {
            _work.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(10));
            _work.Dispose();
        }
    }
}

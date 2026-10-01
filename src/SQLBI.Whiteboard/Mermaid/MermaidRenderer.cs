using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace SQLBI.Whiteboard;

/// <summary>One off-canvas browser renders locally; the board retains only frozen drawings.</summary>
internal sealed class MermaidRenderer(Func<IntPtr> parentWindow, string? profileDirectory = null)
    : IMermaidRenderer, IDisposable
{
    private const string Origin = "https://mermaid.whiteboard.invalid/";
    private readonly Dictionary<string, Task<MermaidDiagram>> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _queue = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private CoreWebView2Controller? _controller;
    private CoreWebView2Environment? _environment;
    private TaskCompletionSource<string>? _response;
    private string? _requestId;
    private bool _disposed;
    private string? _unavailable;
    public int RenderCount { get; private set; }

    public Task<MermaidDiagram> RenderAsync(string source)
    {
        if (_disposed) return Task.FromResult(MermaidDiagram.Failure("The diagram renderer has closed."));
        if (MermaidSource.Validate(source) is string error) return Task.FromResult(MermaidDiagram.Failure(error));
        if (_cache.TryGetValue(source, out var cached)) return cached;
        if (_cache.Count >= 32)
        {
            var finished = _cache.FirstOrDefault(item => item.Value.IsCompleted);
            if (finished.Key is not null) _cache.Remove(finished.Key);
            else return Task.FromResult(MermaidDiagram.Failure("Too many diagrams are waiting. Restart Whiteboard to try again."));
        }
        var task = RenderQueuedAsync(source);
        _cache.Add(source, task);
        return task;
    }

    private async Task<MermaidDiagram> RenderQueuedAsync(string source)
    {
        bool entered = false;
        try
        {
            await _queue.WaitAsync(_lifetime.Token);
            entered = true;
            if (_unavailable is not null) return MermaidDiagram.Failure(_unavailable);
            await EnsureBrowserAsync();
            _lifetime.Token.ThrowIfCancellationRequested();
            _requestId = Guid.NewGuid().ToString("N");
            _response = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var clock = Stopwatch.StartNew();
            RenderCount++;
            _controller!.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id = _requestId, source }));
            string svg = await _response.Task.WaitAsync(TimeSpan.FromSeconds(12), _lifetime.Token);
            var image = await Task.Run(() => MermaidSvg.Decode(svg), _lifetime.Token);
            Debug.WriteLine($"[Mermaid] rendered {source.Length} characters in {clock.ElapsedMilliseconds} ms");
            return new(image, null, svg);
        }
        catch (OperationCanceledException) { return MermaidDiagram.Failure("Diagram rendering was cancelled."); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Debug.WriteLine($"[Mermaid] {exception}");
            if (exception is TimeoutException || _controller is null)
            {
                _unavailable = "The local diagram renderer is unavailable. Check the WebView2 Runtime and restart Whiteboard.";
                _controller?.Close();
                _controller = null;
            }
            return MermaidDiagram.Failure(_unavailable ?? "Mermaid could not render this diagram. Check its syntax.");
        }
        finally
        {
            _response = null;
            _requestId = null;
            if (entered) _queue.Release();
        }
    }

    private async Task EnsureBrowserAsync()
    {
        if (_controller is not null) return;
        var profile = profileDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SQLBI", "Whiteboard", "MermaidPrototype");
        _environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile)
            .WaitAsync(TimeSpan.FromSeconds(20), _lifetime.Token);
        _lifetime.Token.ThrowIfCancellationRequested();
        var creating = _environment.CreateCoreWebView2ControllerAsync(parentWindow());
        CoreWebView2Controller controller;
        try
        {
            controller = await creating.WaitAsync(TimeSpan.FromSeconds(20), _lifetime.Token);
        }
        catch
        {
            _ = CloseWhenReadyAsync(creating);
            throw;
        }
        if (_disposed) { controller.Close(); throw new OperationCanceledException(); }
        _controller = controller;
        controller.IsVisible = false;
        controller.Bounds = new System.Drawing.Rectangle(0, 0, 1600, 1200);
        var web = controller.CoreWebView2;
        web.Settings.AreDefaultContextMenusEnabled = false;
        web.Settings.AreDevToolsEnabled = false;
        web.Settings.AreDefaultScriptDialogsEnabled = false;
        web.Settings.IsStatusBarEnabled = false;
        web.Settings.AreHostObjectsAllowed = false;
        web.Settings.IsBuiltInErrorPageEnabled = false;
        web.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        web.NewWindowRequested += (_, e) => e.Handled = true;
        web.DownloadStarting += (_, e) => e.Cancel = true;
        web.NavigationStarting += (_, e) => e.Cancel = e.Uri != Origin + "index.html";
        web.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        web.WebResourceRequested += ResourceRequested;
        web.ProcessFailed += (_, _) =>
        {
            _unavailable = "The local diagram renderer stopped. Restart Whiteboard to try again.";
            _response?.TrySetException(new InvalidOperationException("Diagram browser stopped."));
        };
        web.WebMessageReceived += (_, e) =>
        {
            if (e.Source != Origin + "index.html") return;
            try
            {
                using var message = JsonDocument.Parse(e.WebMessageAsJson);
                var root = message.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return;
                if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                    id.GetString() != _requestId) return;
                if (root.TryGetProperty("error", out var error))
                    _response?.TrySetException(new InvalidDataException("Invalid Mermaid source."));
                else if (root.TryGetProperty("svg", out var svg) && svg.ValueKind == JsonValueKind.String)
                    _response?.TrySetResult(svg.GetString() ?? "");
            }
            catch (JsonException) { /* Ignore unrelated messages. */ }
        };
        _requestId = "ready";
        _response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        web.Navigate(Origin + "index.html");
        await _response.Task.WaitAsync(TimeSpan.FromSeconds(20), _lifetime.Token);
    }

    private static async Task CloseWhenReadyAsync(Task<CoreWebView2Controller> creating)
    {
        // Controller creation cannot be cancelled. Release a late result after
        // the owning window closes or the startup timeout expires.
        try { (await creating).Close(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Debug.WriteLine($"[Mermaid] closing late controller: {exception.Message}");
        }
    }

    private void ResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var name = e.Request.Uri switch
        {
            Origin + "index.html" => "index.html",
            Origin + "host.js" => "host.js",
            Origin + "mermaid.min.js" => "mermaid.min.js",
            _ => null,
        };
        if (name is null)
        {
            e.Response = _environment!.CreateWebResourceResponse(null, 403, "Blocked", "");
            return;
        }
        try
        {
            var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Mermaid", "Web", name));
            string type = name.EndsWith(".html", StringComparison.Ordinal) ? "text/html" : "text/javascript";
            e.Response = _environment!.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK",
                $"Content-Type: {type}; charset=utf-8\r\nCache-Control: no-store");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _unavailable = "The local diagram files are missing or unreadable. Rebuild or reinstall Whiteboard.";
            e.Response = _environment!.CreateWebResourceResponse(null, 404, "Not found", "");
            _response?.TrySetException(exception);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _controller?.Close();
        _controller = null;
        _cache.Clear();
    }
}

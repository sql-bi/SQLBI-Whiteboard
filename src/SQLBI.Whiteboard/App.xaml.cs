using System.IO;
using System.Windows;
using SQLBI.Whiteboard.LiveView;

namespace SQLBI.Whiteboard;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        WinRtThreading.EnsureDispatcherQueue();
        var window = new MainWindow(FindBoardPath(e.Args));
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// Returns the first existing file passed on the command line, as supplied by the
    /// shell when a <c>.wboard</c> or <c>.wimport</c> file is opened from Explorer.
    /// </summary>
    private static string? FindBoardPath(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith('-') || arg.StartsWith('/'))
            {
                continue;
            }

            if (File.Exists(arg))
            {
                return Path.GetFullPath(arg);
            }
        }

        return null;
    }
}

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard.SmokeTests;

internal static class PreferencesSmokeTests
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                CheckImportSliders();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("Preferences smoke tests failed.", failure);
        }
    }

    private static void CheckImportSliders()
    {
        // Load the real styles without starting the application or touching the user's settings.
        var app = new App();
        app.InitializeComponent();
        var settings = new AppSettings();
        var saved = string.Empty;
        var changes = 0;
        var window = new PreferencesWindow(settings, () =>
        {
            saved = AppSettingsSerializer.Format(settings);
            changes++;
        });
        try
        {
            var horizontal = FindSlider(window, "Horizontal spacing");
            var vertical = FindSlider(window, "Vertical spacing");
            Assert(
                horizontal.Value == 32 && vertical.Value == 32 && changes == 0,
                "Opening Preferences should show the old gaps without applying any changes.");
            Assert(
                horizontal.Orientation == Orientation.Horizontal && vertical.Orientation == Orientation.Horizontal &&
                horizontal.Minimum == 0 && horizontal.Maximum == 500 &&
                vertical.Minimum == 0 && vertical.Maximum == 500 &&
                horizontal.TickFrequency == 1 && vertical.TickFrequency == 1 &&
                horizontal.IsSnapToTickEnabled && vertical.IsSnapToTickEnabled,
                "Import should offer horizontal sliders with whole-pixel steps, including zero.");

            horizontal.Value = 80;
            Assert(
                settings.Import is { HorizontalSpacing: 80, VerticalSpacing: 32 } && ValueLabel(horizontal) == "80 px",
                "The horizontal slider should change only its gap and display pixels.");
            vertical.Value = 120;
            Assert(
                changes == 2 && ValueLabel(vertical) == "120 px" &&
                AppSettingsSerializer.Parse(saved).Import is { HorizontalSpacing: 80, VerticalSpacing: 120 },
                "Both slider changes should reach the persistence callback with their independent values.");

            var hold = FindSlider(window, "Trail duration");
            var fade = FindSlider(window, "Fade duration");
            Assert(
                hold.Value == LaserSettings.DefaultHoldSeconds && hold.TickFrequency == 0.25 &&
                hold.SmallChange == 0.25 && hold.LargeChange == 1 &&
                fade.Value == LaserSettings.DefaultFadeSeconds && fade.TickFrequency == 0.05 &&
                fade.SmallChange == 0.05 && fade.LargeChange == 0.5,
                "Adding pixel sliders must not change the laser timing controls.");
            hold.Value = 3;
            fade.Value = 1;
            Assert(
                settings.Laser is { HoldSeconds: 3, FadeSeconds: 1 } &&
                ValueLabel(hold) == "3 s" && ValueLabel(fade) == "1 s" &&
                settings.Import is { HorizontalSpacing: 80, VerticalSpacing: 120 },
                "Laser sliders should still update seconds without changing import spacing.");

            var category = Descendants(window).OfType<ToggleButton>()
                .Single(button => button.Content is "Import");
            category.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(
                Descendants(window).OfType<Slider>().Count() == 2 &&
                FindSlider(window, "Horizontal spacing").Value == 80 &&
                FindSlider(window, "Vertical spacing").Value == 120 && changes == 4,
                "The Import category should show only its sliders and retain values when rebuilt.");

            var reopened = new PreferencesWindow(AppSettingsSerializer.Parse(saved), () =>
                throw new InvalidOperationException("Reopening Preferences must not apply a change."));
            try
            {
                Assert(
                    FindSlider(reopened, "Horizontal spacing").Value == 80 &&
                    FindSlider(reopened, "Vertical spacing").Value == 120,
                    "Saved spacing should be restored into the actual controls.");
            }
            finally
            {
                reopened.Close();
            }
        }
        finally
        {
            window.Close();
            app.Shutdown();
        }
    }

    private static Slider FindSlider(DependencyObject root, string name) =>
        Descendants(root).OfType<Slider>().Single(slider => AutomationProperties.GetName(slider) == name);

    private static string ValueLabel(Slider slider) =>
        ((Grid)slider.Parent).Children.OfType<TextBlock>().Single().Text;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

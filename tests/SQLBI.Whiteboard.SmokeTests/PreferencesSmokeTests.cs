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
            // Load the real styles without starting the application or touching
            // the user's settings. One Application to a process, so every check
            // in here shares it.
            var app = new App();
            app.InitializeComponent();
            try
            {
                CheckImportSliders();
                CheckDrawnChoices();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                app.Shutdown();
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

            var liveViewCategory = Descendants(window).OfType<ToggleButton>()
                .Single(button => button.Content is "Live View");
            liveViewCategory.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var pause = Descendants(window).OfType<CheckBox>().Single();
            Assert(pause.IsChecked == false &&
                AutomationProperties.GetName(pause) == "Pause when Whiteboard loses focus",
                "Live View should expose an accessible, initially unchecked checkbox.");
            pause.IsChecked = true;
            Assert(changes == 5 && settings.PauseLiveViewsWhenUnfocused &&
                AppSettingsSerializer.Parse(saved).PauseLiveViewsWhenUnfocused,
                "Checking the LiveView option should apply and persist immediately.");
            var liveViewReopened = new PreferencesWindow(AppSettingsSerializer.Parse(saved), () =>
                throw new InvalidOperationException("Reopening must not apply a setting."));
            try
            {
                Assert(Descendants(liveViewReopened).OfType<CheckBox>().Single().IsChecked == true,
                    "The LiveView checkbox should restore its saved value.");
            }
            finally
            {
                liveViewReopened.Close();
            }

            pause.IsChecked = false;
            Assert(changes == 6 && !settings.PauseLiveViewsWhenUnfocused &&
                !AppSettingsSerializer.Parse(saved).PauseLiveViewsWhenUnfocused,
                "Unchecking the option should also apply immediately.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Every drawn row has a picture for every choice it offers, and a drawn
    /// boolean reads and writes the setting the switches used to. A missing
    /// picture is a choice the dialog silently drops, which the eye would only
    /// catch on the one row nobody opened.
    /// </summary>
    private static void CheckDrawnChoices()
    {
        var settings = new AppSettings
        {
            Mode = BoardMode.Custom,
            DesignTools = true,
            PropertyBar = true,
            ExtendedSelection = true,
            DepthAndDuplicate = true,
        };
        var changes = 0;
        var window = new PreferencesWindow(settings, () => changes++);
        try
        {
            var drawn = SettingsCatalog.All
                .Where(setting => setting.Editor == SettingEditorKind.DrawnChoice)
                .ToArray();
            Assert(drawn.Length == 11, "Every row meant to be drawn should say so in the catalog.");
            foreach (var setting in drawn)
            {
                Assert(setting.Choices.Count >= 2, $"{setting.Id} should offer choices to draw.");
                foreach (var choice in setting.Choices)
                {
                    Assert(
                        window.DrawnSample(setting.Id, choice.Id) is not null,
                        $"{setting.Id} should draw a sample for {choice.Id}.");
                }
            }

            Assert(
                window.DrawnSample(SettingsCatalog.Ids.Grid, "Sideways") is null &&
                window.DrawnSample(SettingsCatalog.Ids.DesignTools, "Maybe") is null &&
                window.DrawnSample(SettingsCatalog.Ids.CheckForUpdates, "On") is null,
                "A row and a choice that do not go together should draw nothing.");

            // A drawn boolean is still a boolean: the picture that is in force
            // is the one the setting holds, and pressing the other writes it.
            var segments = Descendants(window).OfType<ToggleButton>()
                .Where(button => button.Tag is string)
                .ToArray();
            var off = Segment(segments, SettingsCatalog.Ids.InsertPalette, SettingsCatalog.BooleanChoice.Off);
            var on = Segment(segments, SettingsCatalog.Ids.InsertPalette, SettingsCatalog.BooleanChoice.On);
            Assert(
                off.IsChecked == true && on.IsChecked == false && changes == 0,
                "A drawn boolean should open showing the value it holds, and apply nothing.");
            on.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(
                settings.InsertPaletteShown && changes == 1 && on.IsChecked == true && off.IsChecked == false,
                "Pressing the other picture should write the boolean once.");
            off.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(
                !settings.InsertPaletteShown && changes == 2,
                "Pressing the first picture again should write it back.");

            // The four group rows are what Custom is made of, so they are shown
            // greyed rather than hidden anywhere else.
            var design = new PreferencesWindow(new AppSettings { Mode = BoardMode.Design }, () =>
                throw new InvalidOperationException("Opening Preferences must not apply a change."));
            try
            {
                var groupRow = Descendants(design).OfType<ToggleButton>()
                    .Single(button =>
                        (button.Tag as string) == SettingsCatalog.BooleanChoice.On &&
                        AncestorTitle(button) == "Design tools");
                Assert(!groupRow.IsEnabled, "A group row outside Custom should be drawn but unusable.");
            }
            finally
            {
                design.Close();
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static ToggleButton Segment(
        IEnumerable<ToggleButton> segments,
        string settingId,
        string choiceId)
    {
        var title = SettingsCatalog.All.Single(setting => setting.Id == settingId).Title;
        return segments.Single(button =>
            (button.Tag as string) == choiceId && AncestorTitle(button) == title);
    }

    // The title the row carries, which is the first line of its own words.
    private static string? AncestorTitle(DependencyObject element)
    {
        for (var parent = LogicalTreeHelper.GetParent(element); parent is not null;
             parent = LogicalTreeHelper.GetParent(parent))
        {
            if (parent is Border { Child: Grid body } &&
                body.Children.OfType<StackPanel>().FirstOrDefault() is { } copy &&
                copy.Children.OfType<TextBlock>().FirstOrDefault() is { } title)
            {
                return title.Text;
            }
        }

        return null;
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

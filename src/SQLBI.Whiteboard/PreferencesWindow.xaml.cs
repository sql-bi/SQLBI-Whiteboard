using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard;

public partial class PreferencesWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _applied;
    private readonly IReadOnlyList<DisplayMonitor> _monitors;
    private string? _selectedCategory;
    private bool _suppressChange;

    public PreferencesWindow(AppSettings settings, Action applied)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(applied);
        _settings = settings;
        _applied = applied;
        _monitors = MonitorStartupPlacement.Enumerate();
        InitializeComponent();
        Rebuild();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => SearchBox.Focus();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        Close();
        e.Handled = true;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        Rebuild();
    }

    private void Rebuild()
    {
        var query = SearchBox.Text;
        var matching = SettingsCatalog.Filter(query, category: null);
        var visibleCategories = SettingsCatalog.CategoriesFor(matching);
        if (_selectedCategory is not null &&
            !visibleCategories.Contains(_selectedCategory, StringComparer.Ordinal))
        {
            _selectedCategory = null;
        }

        var visible = SettingsCatalog.Filter(query, _selectedCategory);
        RebuildCategories(visibleCategories);
        RebuildSettings(visible);
    }

    private void RebuildCategories(IReadOnlyList<string> categories)
    {
        CategoryHost.Children.Clear();
        foreach (var category in categories)
        {
            var button = new ToggleButton
            {
                Style = (Style)FindResource("SettingsCategoryButton"),
                Content = category,
                Tag = category,
                IsChecked = category == _selectedCategory,
            };
            button.Click += CategoryButton_Click;
            CategoryHost.Children.Add(button);
        }
    }

    private void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string category })
        {
            return;
        }

        _selectedCategory = category == _selectedCategory ? null : category;
        Rebuild();
    }

    private void RebuildSettings(IReadOnlyList<SettingDescriptor> settings)
    {
        SettingsHost.Children.Clear();
        var empty = settings.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
        {
            return;
        }

        var showHeadings = DistinctCategoryCount(settings) > 1;
        string? lastCategory = null;
        var firstHeading = true;
        _suppressChange = true;
        try
        {
            foreach (var setting in settings)
            {
                if (showHeadings && setting.Category != lastCategory)
                {
                    var heading = new TextBlock
                    {
                        Style = (Style)FindResource("SettingsHeading"),
                        Text = setting.Category.ToUpperInvariant(),
                    };
                    if (firstHeading)
                    {
                        heading.Margin = new Thickness(4, 0, 4, 4);
                        firstHeading = false;
                    }

                    SettingsHost.Children.Add(heading);
                    lastCategory = setting.Category;
                }

                SettingsHost.Children.Add(CreateRow(setting));
            }
        }
        finally
        {
            _suppressChange = false;
        }
    }

    private Border CreateRow(SettingDescriptor setting)
    {
        var editor = CreateEditor(setting);
        var copy = new StackPanel();
        copy.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("SettingsTitle"),
            Text = setting.Title,
        });
        copy.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("SettingsDescription"),
            Text = setting.Description,
        });

        var body = new Grid();
        if (setting.Editor == SettingEditorKind.DoubleRange)
        {
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var slider = (Slider)editor;
            var value = new TextBlock
            {
                Style = (Style)FindResource("SettingsValueLabel"),
                Text = FormatSeconds(slider.Value),
            };
            slider.ValueChanged += (_, _) => value.Text = FormatSeconds(slider.Value);
            Grid.SetColumn(value, 1);
            Grid.SetRow(slider, 1);
            Grid.SetColumnSpan(slider, 2);
            body.Children.Add(copy);
            body.Children.Add(value);
            body.Children.Add(slider);
        }
        else
        {
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(editor, 1);
            editor.Margin = new Thickness(16, 0, 0, 0);
            editor.VerticalAlignment = VerticalAlignment.Center;
            body.Children.Add(copy);
            body.Children.Add(editor);
        }

        return new Border
        {
            Style = (Style)FindResource("SettingsRow"),
            Child = body,
        };
    }

    private FrameworkElement CreateEditor(SettingDescriptor setting) =>
        setting.Editor switch
        {
            SettingEditorKind.BooleanSwitch => CreateSwitch(setting),
            SettingEditorKind.DoubleRange => CreateSlider(setting),
            SettingEditorKind.MonitorChoice => CreateMonitorCombo(),
            _ => CreateEnumCombo(setting),
        };

    private ToggleButton CreateSwitch(SettingDescriptor setting)
    {
        var button = new ToggleButton
        {
            Style = (Style)FindResource("SettingsSwitch"),
            IsChecked = setting.Id == SettingsCatalog.Ids.StartFullScreen && _settings.StartFullScreen,
        };
        button.Checked += (_, _) => SetBoolean(setting, true);
        button.Unchecked += (_, _) => SetBoolean(setting, false);
        return button;
    }

    private Slider CreateSlider(SettingDescriptor setting)
    {
        var value = setting.Id == SettingsCatalog.Ids.LaserFadeSeconds
            ? _settings.Laser.FadeSeconds
            : _settings.Laser.HoldSeconds;
        var slider = new Slider
        {
            Style = (Style)FindResource("SettingsSlider"),
            Minimum = setting.Minimum,
            Maximum = setting.Maximum,
            SmallChange = setting.Id == SettingsCatalog.Ids.LaserFadeSeconds ? 0.05 : 0.25,
            LargeChange = setting.Id == SettingsCatalog.Ids.LaserFadeSeconds ? 0.5 : 1,
            TickFrequency = setting.Id == SettingsCatalog.Ids.LaserFadeSeconds ? 0.05 : 0.25,
            IsSnapToTickEnabled = true,
            Value = value,
        };
        slider.ValueChanged += (_, _) => SetRange(setting, slider.Value);
        return slider;
    }

    private ComboBox CreateEnumCombo(SettingDescriptor setting)
    {
        var combo = new ComboBox
        {
            Style = (Style)FindResource("SettingsComboBox"),
        };
        foreach (var choice in setting.Choices)
        {
            combo.Items.Add(choice);
        }

        combo.SelectedItem = setting.Choices.FirstOrDefault(choice =>
            choice.Id == CurrentEnumId(setting));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is SettingChoice choice)
            {
                SetEnum(setting, choice.Id);
            }
        };
        return combo;
    }

    private ComboBox CreateMonitorCombo()
    {
        var combo = new ComboBox
        {
            Style = (Style)FindResource("SettingsComboBox"),
        };
        foreach (var item in BuildMonitorItems())
        {
            combo.Items.Add(item);
        }

        SelectMonitorItem(combo);
        combo.SelectionChanged += MonitorCombo_SelectionChanged;
        return combo;
    }

    private List<MonitorChoiceItem> BuildMonitorItems()
    {
        var items = new List<MonitorChoiceItem>
        {
            new()
            {
                Kind = StartupMonitorKind.WacomIfPresent,
                Title = "Wacom / Cintiq if present",
            },
            new()
            {
                Kind = StartupMonitorKind.Primary,
                Title = "Primary monitor",
            },
        };

        if (_monitors.Count > 0)
        {
            items.Add(new() { IsSeparator = true, Title = string.Empty });
            foreach (var monitor in _monitors)
            {
                items.Add(new()
                {
                    Kind = StartupMonitorKind.Named,
                    Name = monitor.DisplayName,
                    Title = monitor.DisplayName,
                });
            }
        }

        if (_settings.StartupMonitor == StartupMonitorKind.Named &&
            !string.IsNullOrWhiteSpace(_settings.StartupMonitorName) &&
            !_monitors.Any(monitor =>
                string.Equals(
                    monitor.DisplayName,
                    _settings.StartupMonitorName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(new()
            {
                Kind = StartupMonitorKind.Named,
                Name = _settings.StartupMonitorName,
                Title = $"{_settings.StartupMonitorName} (not connected)",
                IsAvailable = false,
            });
        }

        return items;
    }

    private void SelectMonitorItem(ComboBox combo)
    {
        foreach (var choice in combo.Items.OfType<MonitorChoiceItem>())
        {
            var selected = choice.Kind == _settings.StartupMonitor &&
                           (choice.Kind != StartupMonitorKind.Named ||
                            string.Equals(
                                choice.Name,
                                _settings.StartupMonitorName,
                                StringComparison.OrdinalIgnoreCase));
            if (selected)
            {
                combo.SelectedItem = choice;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private void MonitorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressChange ||
            sender is not ComboBox combo ||
            combo.SelectedItem is not MonitorChoiceItem choice ||
            !choice.IsAvailable ||
            choice.IsSeparator)
        {
            return;
        }

        _settings.StartupMonitor = choice.Kind;
        _settings.StartupMonitorName = choice.Kind == StartupMonitorKind.Named ? choice.Name : null;
        NotifyApplied();
    }

    private string CurrentEnumId(SettingDescriptor setting) =>
        setting.Id switch
        {
            SettingsCatalog.Ids.LaserHoldMode => _settings.Laser.HoldMode.ToString(),
            SettingsCatalog.Ids.ToolbarPlacement => _settings.ToolbarPlacement.ToString(),
            SettingsCatalog.Ids.ToolbarLayout => _settings.CalligraphyAccess.ToString(),
            _ => string.Empty,
        };

    private void SetBoolean(SettingDescriptor setting, bool value)
    {
        if (_suppressChange || setting.Id != SettingsCatalog.Ids.StartFullScreen)
        {
            return;
        }

        _settings.StartFullScreen = value;
        NotifyApplied();
    }

    private void SetRange(SettingDescriptor setting, double value)
    {
        if (_suppressChange)
        {
            return;
        }

        if (setting.Id == SettingsCatalog.Ids.LaserFadeSeconds)
        {
            _settings.Laser.FadeSeconds = value;
        }
        else
        {
            _settings.Laser.HoldSeconds = value;
        }

        _settings.Laser = LaserSettings.Normalize(_settings.Laser);
        NotifyApplied();
    }

    private void SetEnum(SettingDescriptor setting, string id)
    {
        if (_suppressChange)
        {
            return;
        }

        switch (setting.Id)
        {
            case SettingsCatalog.Ids.LaserHoldMode
                when Enum.TryParse<LaserHoldMode>(id, out var holdMode):
                _settings.Laser.HoldMode = holdMode;
                break;
            case SettingsCatalog.Ids.ToolbarPlacement
                when Enum.TryParse<ToolbarPlacement>(id, out var placement):
                _settings.ToolbarPlacement = placement;
                break;
            case SettingsCatalog.Ids.ToolbarLayout
                when Enum.TryParse<CalligraphyAccess>(id, out var access):
                _settings.CalligraphyAccess = access;
                break;
            default:
                return;
        }

        NotifyApplied();
    }

    private void NotifyApplied()
    {
        if (!_suppressChange)
        {
            _applied();
        }
    }

    private static int DistinctCategoryCount(IReadOnlyList<SettingDescriptor> settings)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var setting in settings)
        {
            seen.Add(setting.Category);
        }

        return seen.Count;
    }

    private static string FormatSeconds(double value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        var text = rounded.ToString(rounded == Math.Truncate(rounded) ? "0" : "0.##", CultureInfo.CurrentCulture);
        return $"{text} s";
    }

    private sealed class MonitorChoiceItem
    {
        public StartupMonitorKind Kind { get; init; }

        public string? Name { get; init; }

        public required string Title { get; init; }

        public bool IsSeparator { get; init; }

        public bool IsAvailable { get; init; } = true;
    }
}

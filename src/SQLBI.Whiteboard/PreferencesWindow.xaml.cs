using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SQLBI.Whiteboard.Core.Model;
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
        else if (setting.Editor is
                 SettingEditorKind.OrderedList or
                 SettingEditorKind.LaserWeightChoice or
                 SettingEditorKind.PenButtonChoice or
                 SettingEditorKind.ToolbarPlacementChoice or
                 SettingEditorKind.ToolbarLayoutChoice)
        {
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(editor, 1);
            editor.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(copy);
            body.Children.Add(editor);
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
            SettingEditorKind.OrderedList => CreateOrderedList(setting),
            SettingEditorKind.LaserWeightChoice => CreateLaserWeightChoice(setting),
            SettingEditorKind.PenButtonChoice =>
                CreateSampleChoice(setting, CreatePenButtonSample),
            SettingEditorKind.ToolbarPlacementChoice =>
                CreateSampleChoice(setting, CreateToolbarPlacementSample),
            SettingEditorKind.ToolbarLayoutChoice =>
                CreateSampleChoice(setting, CreateToolbarLayoutSample),
            _ => CreateEnumCombo(setting),
        };

    // Options that are drawn rather than named: each is a sample under its own
    // label, and the row of them behaves as one choice.
    private FrameworkElement CreateSampleChoice(
        SettingDescriptor setting,
        Func<string, FrameworkElement?> sampleFor)
    {
        var host = new UniformGrid
        {
            Rows = 1,
            Columns = setting.Choices.Count,
        };

        var segments = new List<ToggleButton>();
        foreach (var choice in setting.Choices)
        {
            if (sampleFor(choice.Id) is not { } sample)
            {
                continue;
            }

            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sample.HorizontalAlignment = HorizontalAlignment.Center;
            content.Children.Add(sample);
            content.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("SettingsValueLabel"),
                Text = choice.Title,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            var segment = new ToggleButton
            {
                Style = (Style)FindResource("SettingsSampleSegment"),
                Content = content,
                IsChecked = choice.Id == CurrentEnumId(setting),
                Tag = choice.Id,
                ToolTip = choice.Title,
            };
            segment.Click += (_, _) =>
            {
                foreach (var other in segments)
                {
                    other.IsChecked = ReferenceEquals(other, segment);
                }

                SetEnum(setting, choice.Id);
            };
            segments.Add(segment);
            host.Children.Add(segment);
        }

        return host;
    }

    private static readonly Brush SampleInkBrush = Frozen(0xFF374151);
    private static readonly Brush SampleGhostBrush = Frozen(0x59374151);
    private static readonly Brush SampleBoardBrush = Frozen(0xFFFFFFFF);
    private static readonly Brush SampleEdgeBrush = Frozen(0xFFD1D5DB);
    private static readonly Brush SampleAccentBrush = Frozen(0xFF2563EB);

    private static Brush Frozen(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb));
        brush.Freeze();
        return brush;
    }

    // The laser lays a fading trail behind a bright head; the straight line
    // takes the same wandering hand and rules it flat.
    private FrameworkElement? CreatePenButtonSample(string id)
    {
        if (!Enum.TryParse<PenButtonAction>(id, out var action))
        {
            return null;
        }

        var canvas = new Canvas { Width = 84, Height = 34 };
        if (action == PenButtonAction.Laser)
        {
            for (var step = 0; step < 7; step++)
            {
                var fraction = step / 6d;
                var size = 3 + (7 * fraction);
                var trail = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = Frozen((uint)((byte)(40 + (190 * fraction)) << 24) |
                                  ((uint)LaserSettings.TrailRed << 16) |
                                  ((uint)LaserSettings.TrailGreen << 8) |
                                  LaserSettings.TrailBlue),
                };
                Canvas.SetLeft(trail, 8 + (fraction * 60) - (size / 2));
                Canvas.SetTop(trail, 17 - (size / 2) - (Math.Sin(fraction * 3) * 5));
                canvas.Children.Add(trail);
            }

            return canvas;
        }

        canvas.Children.Add(new Polyline
        {
            Points = [
                new Point(6, 24), new Point(20, 12), new Point(34, 22),
                new Point(48, 10), new Point(62, 20), new Point(78, 12),
            ],
            Stroke = SampleGhostBrush,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        });

        var ruled = new Rectangle
        {
            Width = 72,
            Height = 4,
            RadiusX = 2,
            RadiusY = 2,
            Fill = SampleInkBrush,
        };
        Canvas.SetLeft(ruled, 6);
        Canvas.SetTop(ruled, 15);
        canvas.Children.Add(ruled);
        return canvas;
    }

    // A board with the toolbar in it, so the corner is seen rather than read.
    private FrameworkElement? CreateToolbarPlacementSample(string id)
    {
        if (!Enum.TryParse<ToolbarPlacement>(id, out var placement))
        {
            return null;
        }

        var bar = new Border
        {
            Width = placement == ToolbarPlacement.BottomCenter ? 30 : 24,
            Height = 7,
            CornerRadius = new CornerRadius(3.5),
            Background = SampleAccentBrush,
            Margin = new Thickness(5),
            HorizontalAlignment = placement switch
            {
                ToolbarPlacement.TopLeft or ToolbarPlacement.BottomLeft =>
                    HorizontalAlignment.Left,
                ToolbarPlacement.BottomCenter => HorizontalAlignment.Center,
                _ => HorizontalAlignment.Right,
            },
            VerticalAlignment = placement is ToolbarPlacement.TopLeft or ToolbarPlacement.TopRight
                ? VerticalAlignment.Top
                : VerticalAlignment.Bottom,
        };

        return new Border
        {
            Width = 76,
            Height = 46,
            CornerRadius = new CornerRadius(6),
            Background = SampleBoardBrush,
            BorderBrush = SampleEdgeBrush,
            BorderThickness = new Thickness(1),
            Child = bar,
        };
    }

    // A miniature of the ink flyout each layout produces.
    private FrameworkElement? CreateToolbarLayoutSample(string id)
    {
        if (!Enum.TryParse<CalligraphyAccess>(id, out var access))
        {
            return null;
        }

        var rows = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        switch (access)
        {
            case CalligraphyAccess.DualPalette:
                // Two tools' colors and sizes, both on show at once.
                rows.Children.Add(SampleChipRow(4, chevron: false, nibs: false));
                rows.Children.Add(SampleChipRow(4, chevron: false, nibs: false));
                break;
            case CalligraphyAccess.Chevron:
                // One compact bar, the second tool behind a chevron.
                rows.Children.Add(SampleChipRow(4, chevron: true, nibs: false));
                break;
            default:
                // One bar, the nibs trailing the size chips.
                rows.Children.Add(SampleChipRow(3, chevron: false, nibs: true));
                break;
        }

        return new Border
        {
            Width = 76,
            Height = 46,
            CornerRadius = new CornerRadius(6),
            Background = SampleBoardBrush,
            BorderBrush = SampleEdgeBrush,
            BorderThickness = new Thickness(1),
            Child = rows,
        };
    }

    private static StackPanel SampleChipRow(int chips, bool chevron, bool nibs)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2),
        };

        for (var index = 0; index < chips; index++)
        {
            var size = nibs ? 5 + (index * 2) : 8;
            row.Children.Add(new Ellipse
            {
                Width = size,
                Height = size,
                Margin = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = index == 0 ? SampleAccentBrush : SampleGhostBrush,
            });
        }

        if (chevron)
        {
            row.Children.Add(new Polyline
            {
                Points = [new Point(0, 0), new Point(4, 4), new Point(8, 0)],
                Stroke = SampleInkBrush,
                StrokeThickness = 1.6,
                Margin = new Thickness(3, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        if (nibs)
        {
            for (var index = 0; index < 2; index++)
            {
                row.Children.Add(new Rectangle
                {
                    Width = 7,
                    Height = index == 0 ? 7 : 3,
                    RadiusX = 1.5,
                    RadiusY = 1.5,
                    Margin = new Thickness(3, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Fill = SampleInkBrush,
                });
            }
        }

        return row;
    }

    // Each option is drawn as the strokes it produces, at the same width and
    // opacity the trail itself would use, so the choice is made by looking
    // rather than by imagining what a word means.
    private FrameworkElement CreateLaserWeightChoice(SettingDescriptor setting) =>
        CreateSampleChoice(setting, id =>
        {
            const float LightTouch = 0.08f;
            const float FirmTouch = 0.6f;
            if (!Enum.TryParse<LaserTrailWeight>(id, out var weight))
            {
                return null;
            }

            var samples = new StackPanel();
            samples.Children.Add(CreateLaserSample(weight, LightTouch));
            samples.Children.Add(CreateLaserSample(weight, FirmTouch));
            return samples;
        });

    private Border CreateLaserSample(LaserTrailWeight weight, float pressure)
    {
        var minimumWidth = LaserSettings.MinimumTrailWidthFor(weight);
        var height = minimumWidth + ((LaserSettings.MaximumTrailWidth - minimumWidth) * pressure);
        var floor = LaserSettings.MinimumTrailOpacityFor(weight);
        var opacity = floor + ((1 - floor) * pressure);
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)(230 * opacity),
            LaserSettings.TrailRed,
            LaserSettings.TrailGreen,
            LaserSettings.TrailBlue));
        brush.Freeze();
        return new Border
        {
            Height = height,
            Width = 76,
            Margin = new Thickness(0, 4, 0, 4),
            CornerRadius = new CornerRadius(height / 2),
            Background = brush,
        };
    }

    private FrameworkElement CreateOrderedList(SettingDescriptor setting)
    {
        var host = new StackPanel();
        RebuildSnippetFormatOrder(host);
        host.Tag = setting;
        return host;
    }

    private void RebuildSnippetFormatOrder(StackPanel host)
    {
        host.Children.Clear();
        var order = _settings.SnippetFormatOrder;
        for (var index = 0; index < order.Count; index++)
        {
            var languageId = order[index];
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock
            {
                Style = (Style)FindResource("SettingsTitle"),
                Text = TextLanguageRegistry.Resolve(languageId).DisplayName,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var from = index;
            var up = CreateOrderButton(
                "ChevronUpGeometry",
                "Move up",
                enabled: from > 0,
                () => MoveSnippetFormat(from, from - 1, host));
            var down = CreateOrderButton(
                "ChevronDownGeometry",
                "Move down",
                enabled: from < order.Count - 1,
                () => MoveSnippetFormat(from, from + 1, host));
            Grid.SetColumn(up, 1);
            Grid.SetColumn(down, 2);
            row.Children.Add(label);
            row.Children.Add(up);
            row.Children.Add(down);
            host.Children.Add(row);
        }
    }

    private Button CreateOrderButton(string geometryKey, string toolTip, bool enabled, Action move)
    {
        var button = new Button
        {
            Style = (Style)FindResource("SettingsOrderButton"),
            ToolTip = toolTip,
            IsEnabled = enabled,
            Content = new System.Windows.Shapes.Path
            {
                Width = 12,
                Height = 12,
                Stretch = System.Windows.Media.Stretch.Uniform,
                Fill = (System.Windows.Media.Brush)FindResource("ToolbarIconBrush"),
                Data = (System.Windows.Media.Geometry)FindResource(geometryKey),
            },
        };
        button.Click += (_, _) =>
        {
            if (!_suppressChange)
            {
                move();
            }
        };
        return button;
    }

    private void MoveSnippetFormat(int from, int to, StackPanel host)
    {
        if (from < 0 || to < 0 ||
            from >= _settings.SnippetFormatOrder.Count ||
            to >= _settings.SnippetFormatOrder.Count)
        {
            return;
        }

        var item = _settings.SnippetFormatOrder[from];
        _settings.SnippetFormatOrder.RemoveAt(from);
        _settings.SnippetFormatOrder.Insert(to, item);
        _settings.SnippetFormatOrder = [.. TextLanguageIds.NormalizeOrder(_settings.SnippetFormatOrder)];
        RebuildSnippetFormatOrder(host);
        NotifyApplied();
    }

    private ToggleButton CreateSwitch(SettingDescriptor setting)
    {
        var button = new ToggleButton
        {
            Style = (Style)FindResource("SettingsSwitch"),
            IsChecked = setting.Id switch
            {
                SettingsCatalog.Ids.StartFullScreen => _settings.StartFullScreen,
                SettingsCatalog.Ids.CheckForUpdates => _settings.CheckForUpdates,
                _ => false,
            },
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
            SettingsCatalog.Ids.LaserTrailWeight => _settings.Laser.TrailWeight.ToString(),
            SettingsCatalog.Ids.ToolbarPlacement => _settings.ToolbarPlacement.ToString(),
            SettingsCatalog.Ids.ToolbarLayout => _settings.CalligraphyAccess.ToString(),
            SettingsCatalog.Ids.FingerMode => _settings.FingerMode.ToString(),
            SettingsCatalog.Ids.PenButton => _settings.PenButtons.Barrel.ToString(),
            _ => string.Empty,
        };

    private void SetBoolean(SettingDescriptor setting, bool value)
    {
        if (_suppressChange)
        {
            return;
        }

        if (setting.Id == SettingsCatalog.Ids.StartFullScreen)
        {
            _settings.StartFullScreen = value;
        }
        else if (setting.Id == SettingsCatalog.Ids.CheckForUpdates)
        {
            _settings.CheckForUpdates = value;
        }
        else
        {
            return;
        }

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
            case SettingsCatalog.Ids.LaserTrailWeight
                when Enum.TryParse<LaserTrailWeight>(id, out var trailWeight):
                _settings.Laser.TrailWeight = trailWeight;
                break;
            case SettingsCatalog.Ids.ToolbarPlacement
                when Enum.TryParse<ToolbarPlacement>(id, out var placement):
                _settings.ToolbarPlacement = placement;
                break;
            case SettingsCatalog.Ids.ToolbarLayout
                when Enum.TryParse<CalligraphyAccess>(id, out var access):
                _settings.CalligraphyAccess = access;
                break;
            case SettingsCatalog.Ids.FingerMode
                when Enum.TryParse<FingerMode>(id, out var fingerMode):
                _settings.FingerMode = fingerMode;
                break;
            case SettingsCatalog.Ids.PenButton
                when Enum.TryParse<PenButtonAction>(id, out var penButton):
                _settings.PenButtons.Barrel = penButton;
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

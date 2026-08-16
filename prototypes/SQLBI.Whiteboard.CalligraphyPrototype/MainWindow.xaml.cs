using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace SQLBI.Whiteboard.CalligraphyPrototype;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _metricsTimer;
    private bool _uiReady;
    private bool _updatingControls;

    public MainWindow()
    {
        InitializeComponent();
        _uiReady = true;
        ApplySettingsToControls(CalligraphySettings.CurrentApplication, "Current app");
        _metricsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };
        _metricsTimer.Tick += MetricsTimer_Tick;
        _metricsTimer.Start();
        InkSurface.Focus();

        if (Environment.GetCommandLineArgs().Contains("--startup-self-test"))
        {
            Loaded += (_, _) => Dispatcher.BeginInvoke(Close);
        }
    }

    private void CurrentPresetButton_Click(object sender, RoutedEventArgs e) =>
        ApplySettingsToControls(CalligraphySettings.CurrentApplication, "Current app");

    private void StrongerPresetButton_Click(object sender, RoutedEventArgs e) =>
        ApplySettingsToControls(CalligraphySettings.Stronger, "Stronger");

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        InkSurface.Strokes.Clear();
        InkSurface.Focus();
    }

    private void CopySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(JsonSerializer.Serialize(
            ReadSettingsFromControls(),
            new JsonSerializerOptions { WriteIndented = true }));
        PresetText.Text = "Settings copied to clipboard";
        InkSurface.Focus();
    }

    private void TuningSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_uiReady && !_updatingControls)
        {
            ApplySettingsFromControls("Custom");
        }
    }

    private void FitToCurveCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiReady && !_updatingControls)
        {
            ApplySettingsFromControls("Custom");
        }
    }

    private void ApplySettingsToControls(CalligraphySettings settings, string presetName)
    {
        _updatingControls = true;
        NominalSizeSlider.Value = settings.NominalSize;
        NibWidthSlider.Value = settings.NibWidthMultiplier;
        NibHeightSlider.Value = settings.NibHeightMultiplier;
        NibAngleSlider.Value = settings.NibAngle;
        PressureExponentSlider.Value = settings.PressureExponent;
        PressureInfluenceSlider.Value = settings.PressureInfluence;
        MinimumWidthSlider.Value = settings.MinimumWidth;
        SpeedInfluenceSlider.Value = settings.SpeedInfluence;
        SpeedReferenceSlider.Value = settings.SpeedReference;
        SpeedSmoothingSlider.Value = settings.SpeedSmoothing;
        FitToCurveCheckBox.IsChecked = settings.FitToCurve;
        _updatingControls = false;
        ApplySettingsFromControls(presetName);
    }

    private void ApplySettingsFromControls(string presetName)
    {
        CalligraphySettings settings = ReadSettingsFromControls();
        InkSurface.ApplySettings(settings);
        NominalSizeValue.Text = settings.NominalSize.ToString("0.00");
        NibWidthValue.Text = settings.NibWidthMultiplier.ToString("0.00×");
        NibHeightValue.Text = settings.NibHeightMultiplier.ToString("0.00×");
        NibAngleValue.Text = settings.NibAngle.ToString("0.0°");
        PressureExponentValue.Text = settings.PressureExponent.ToString("0.00");
        PressureInfluenceValue.Text = settings.PressureInfluence.ToString("0.00");
        MinimumWidthValue.Text = settings.MinimumWidth.ToString("0.000");
        SpeedInfluenceValue.Text = settings.SpeedInfluence.ToString("0.00");
        SpeedReferenceValue.Text = settings.SpeedReference.ToString("0.00 px/ms");
        SpeedSmoothingValue.Text = settings.SpeedSmoothing.ToString("0.00");
        PresetText.Text = $"Preset: {presetName}";
    }

    private CalligraphySettings ReadSettingsFromControls() => new(
        NominalSizeSlider.Value,
        NibWidthSlider.Value,
        NibHeightSlider.Value,
        NibAngleSlider.Value,
        PressureExponentSlider.Value,
        PressureInfluenceSlider.Value,
        MinimumWidthSlider.Value,
        SpeedInfluenceSlider.Value,
        SpeedReferenceSlider.Value,
        SpeedSmoothingSlider.Value,
        FitToCurveCheckBox.IsChecked == true);

    private void MetricsTimer_Tick(object? sender, EventArgs e)
    {
        var metrics = InkSurface.ReadMetrics();
        MetricsText.Text =
            $"Pressure {metrics.RawPressure:0.000} → {metrics.EffectivePressure:0.000}   " +
            $"Speed {metrics.Speed:0.000}";
    }

    private void InkSurface_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        if (e.StylusDevice.TabletDevice.Type == TabletDeviceType.Touch)
        {
            e.Handled = true;
        }
    }

    private void InkSurface_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice is null)
        {
            e.Handled = true;
        }
    }

    private void InkSurface_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice is null && e.LeftButton == MouseButtonState.Pressed)
        {
            e.Handled = true;
        }
    }

    private void InkSurface_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice is null)
        {
            e.Handled = true;
        }
    }

    private void InkSurface_PreviewTouch(object sender, TouchEventArgs e) =>
        e.Handled = true;

    private void Window_Closed(object? sender, EventArgs e)
    {
        _metricsTimer.Stop();
    }
}

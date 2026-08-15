using System.Windows;
using System.Windows.Controls;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard;

public partial class PreferencesWindow : Window
{
    private readonly Action<ToolbarPlacement> _applyPlacement;
    private readonly Action<CalligraphyAccess> _applyCalligraphyAccess;
    private bool _suppressChange;

    public PreferencesWindow(
        ToolbarPlacement currentPlacement,
        CalligraphyAccess currentCalligraphyAccess,
        Action<ToolbarPlacement> applyPlacement,
        Action<CalligraphyAccess> applyCalligraphyAccess)
    {
        ArgumentNullException.ThrowIfNull(applyPlacement);
        ArgumentNullException.ThrowIfNull(applyCalligraphyAccess);
        _applyPlacement = applyPlacement;
        _applyCalligraphyAccess = applyCalligraphyAccess;
        InitializeComponent();
        SelectCombo(PlacementCombo, currentPlacement.ToString());
        SelectCombo(CalligraphyCombo, currentCalligraphyAccess.ToString());
    }

    private void SelectCombo(ComboBox combo, string tag)
    {
        _suppressChange = true;
        try
        {
            foreach (var item in combo.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is string name &&
                    string.Equals(name, tag, StringComparison.Ordinal))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }

            combo.SelectedIndex = 0;
        }
        finally
        {
            _suppressChange = false;
        }
    }

    private void PlacementCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressChange ||
            PlacementCombo.SelectedItem is not ComboBoxItem { Tag: string name } ||
            !Enum.TryParse<ToolbarPlacement>(name, out var placement))
        {
            return;
        }

        _applyPlacement(placement);
    }

    private void CalligraphyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressChange ||
            CalligraphyCombo.SelectedItem is not ComboBoxItem { Tag: string name } ||
            !Enum.TryParse<CalligraphyAccess>(name, out var access))
        {
            return;
        }

        _applyCalligraphyAccess(access);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

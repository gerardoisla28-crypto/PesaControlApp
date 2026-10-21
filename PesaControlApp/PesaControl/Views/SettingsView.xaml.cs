using System.Windows;
using System.Windows.Controls;
using PesaControl.UI.ViewModels;

namespace PesaControl.UI.Views;

public partial class SettingsView : UserControl
{
    private int _currentTab;
    private bool _revertingTab;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => _currentTab = SettingsTabs.SelectedIndex;
    }

    // Si la pestaña que se deja tiene cambios sin guardar, se vuelve a ella y se pregunta antes de cambiar.
    private async void SettingsTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Las listas y combos de adentro también disparan este evento; solo interesa el de las pestañas.
        if (e.OriginalSource != SettingsTabs || _revertingTab) return;

        var target = SettingsTabs.SelectedIndex;
        if (target == _currentTab) return;

        if (DataContext is SettingsViewModel vm && !vm.SuppressTabGuard && vm.IsTabWithUnsavedChanges(_currentTab))
        {
            var previous = _currentTab;
            SetTab(previous);
            if (!await vm.CanLeaveTabAsync(previous)) return;
            SetTab(target);
        }

        _currentTab = target;
    }

    private void SetTab(int index)
    {
        _revertingTab = true;
        SettingsTabs.SelectedIndex = index;
        _revertingTab = false;
    }
}

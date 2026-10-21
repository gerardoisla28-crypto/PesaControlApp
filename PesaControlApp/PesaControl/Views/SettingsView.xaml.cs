using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
    private void SettingsTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Las listas y combos de adentro también disparan este evento; solo interesa el de las pestañas.
        if (e.OriginalSource != SettingsTabs || _revertingTab) return;

        var target = SettingsTabs.SelectedIndex;
        if (target == _currentTab) return;

        if (DataContext is SettingsViewModel vm && !vm.SuppressTabGuard && vm.IsTabWithUnsavedChanges(_currentTab))
        {
            var previous = _currentTab;
            // Cambiar la selección y abrir un diálogo modal dentro del propio evento del clic deja la
            // pestaña anterior sin responder a los clics siguientes: se deja terminar el clic y se pregunta después.
            Dispatcher.BeginInvoke(DispatcherPriority.Send, async () => await ConfirmTabChangeAsync(vm, previous, target));
            return;
        }

        _currentTab = target;
    }

    private async Task ConfirmTabChangeAsync(SettingsViewModel vm, int previous, int target)
    {
        SetTab(previous);
        if (!await vm.CanLeaveTabAsync(previous)) return;

        SetTab(target);
        _currentTab = target;
    }

    private void SetTab(int index)
    {
        _revertingTab = true;
        SettingsTabs.SelectedIndex = index;
        _revertingTab = false;
    }
}

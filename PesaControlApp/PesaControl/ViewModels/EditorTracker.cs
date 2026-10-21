namespace PesaControl.UI.ViewModels;

// Detecta si un editor abierto (formulario con "Guardar" y "Cancelar") tiene cambios respecto a como se abrió.
public sealed class EditorTracker
{
    private readonly Func<string> _snapshot;
    private string? _initial;

    public EditorTracker(Func<string> snapshot) => _snapshot = snapshot;

    // Se llama al abrir el editor, después de cargar sus campos.
    public void Begin() => _initial = _snapshot();

    public void End() => _initial = null;

    public bool IsDirty => _initial is not null && _snapshot() != _initial;

    public static string Snap(params object?[] values) => string.Join('\u001f', values.Select(v => v?.ToString() ?? ""));
}

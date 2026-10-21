namespace PesaControl.UI.ViewModels;

// Pantallas con cambios que el usuario puede dejar sin guardar al navegar a otra parte de la app.
public interface IUnsavedChanges
{
    bool HasUnsavedChanges { get; }

    // Qué hay sin guardar, para el aviso (por ejemplo "Puesto y Red").
    string UnsavedChangesDescription { get; }

    // Devuelve false si no pudo guardar (por ejemplo, por un dato inválido); en ese caso no se debe salir.
    Task<bool> SaveChangesAsync();

    void DiscardChanges();
}

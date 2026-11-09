using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PesaControl.Core.Models;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;

namespace PesaControl.UI.ViewModels;

public partial class CatalogsViewModel : ObservableObject, IUnsavedChanges
{
    private readonly DatabaseService _db;

    public CatalogsViewModel(DatabaseService db)
    {
        _db = db;
        _productEditor = new EditorTracker(() => EditorTracker.Snap(EditProductName, EditProductIsActive));
        _companyEditor = new EditorTracker(() => EditorTracker.Snap(EditCompanyName, EditCompanyRut, EditCompanyAddress, EditCompanyPhone, EditCompanyIsActive));
        _driverEditor = new EditorTracker(() => EditorTracker.Snap(EditDriverName, EditDriverPhone, EditDriverCompany?.Id, EditDriverIsActive));
    }

    // ========== CAMBIOS SIN GUARDAR ==========

    private readonly EditorTracker _productEditor;
    private readonly EditorTracker _companyEditor;
    private readonly EditorTracker _driverEditor;

    partial void OnIsProductEditorOpenChanged(bool value) { if (!value) _productEditor.End(); }
    partial void OnIsCompanyEditorOpenChanged(bool value) { if (!value) _companyEditor.End(); }
    partial void OnIsDriverEditorOpenChanged(bool value) { if (!value) _driverEditor.End(); }

    private bool ProductDirty => IsProductEditorOpen && _productEditor.IsDirty;
    private bool CompanyDirty => IsCompanyEditorOpen && _companyEditor.IsDirty;
    private bool DriverDirty => IsDriverEditorOpen && _driverEditor.IsDirty;

    public bool HasUnsavedChanges => ProductDirty || CompanyDirty || DriverDirty;

    public string UnsavedChangesDescription => string.Join(", ", new[]
    {
        ProductDirty ? "Producto" : null,
        CompanyDirty ? "Empresa" : null,
        DriverDirty ? "Conductor" : null
    }.Where(x => x is not null));

    // Si el guardado no cierra el editor (por ejemplo, falta un dato), no se debe salir de la pantalla.
    public async Task<bool> SaveChangesAsync()
    {
        if (ProductDirty) { await SaveProductAsync(); if (IsProductEditorOpen) return false; }
        if (CompanyDirty) { await SaveCompanyAsync(); if (IsCompanyEditorOpen) return false; }
        if (DriverDirty) { await SaveDriverAsync(); if (IsDriverEditorOpen) return false; }
        return true;
    }

    public void DiscardChanges()
    {
        CancelProductEdit();
        CancelCompanyEdit();
        CancelDriverEdit();
    }

    // ========== PRODUCTS ==========
    public ObservableCollection<Product> Products { get; } = [];
    [ObservableProperty] private Product? _selectedProduct;
    [ObservableProperty] private bool _isProductEditorOpen;
    [ObservableProperty] private string _editProductName = "";
    [ObservableProperty] private bool _editProductIsActive = true;

    [RelayCommand]
    private async Task LoadProductsAsync()
    {
        var ctx = _db.CreateContext();
        var repo = new Repository<Product>(ctx);
        var items = await repo.GetAllAsync();
        App.Current.Dispatcher.Invoke(() =>
        {
            Products.Clear();
            foreach (var p in items.OrderBy(x => x.Name))
                Products.Add(p);
        });
    }

    [RelayCommand]
    private void NewProduct()
    {
        SelectedProduct = null;
        EditProductName = "";
        EditProductIsActive = true;
        _productEditor.Begin();
        IsProductEditorOpen = true;
    }

    [RelayCommand]
    private void EditProduct()
    {
        if (SelectedProduct is null) return;
        EditProductName = SelectedProduct.Name;
        EditProductIsActive = SelectedProduct.IsActive;
        _productEditor.Begin();
        IsProductEditorOpen = true;
    }

    [RelayCommand]
    private async Task SaveProductAsync()
    {
        if (string.IsNullOrWhiteSpace(EditProductName)) return;
        try
        {
            var ctx = _db.CreateContext();
            var repo = new Repository<Product>(ctx);
            if (SelectedProduct is not null)
            {
                SelectedProduct.Name = EditProductName.Trim();
                SelectedProduct.IsActive = EditProductIsActive;
                await repo.UpdateAsync(SelectedProduct);
            }
            else
            {
                var p = new Product { Name = EditProductName.Trim(), IsActive = EditProductIsActive };
                await repo.AddAsync(p);
                Products.Add(p);
            }
            IsProductEditorOpen = false;
            SelectedProduct = null;
            await LoadProductsAsync();
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    private void CancelProductEdit() => IsProductEditorOpen = false;

    [RelayCommand]
    private async Task DeleteProductAsync()
    {
        if (SelectedProduct is null) return;
        if (!ConfirmDelete("producto", SelectedProduct.Name)) return;
        try
        {
            var ctx = _db.CreateContext();
            var repo = new Repository<Product>(ctx);
            var p = await repo.GetByIdAsync(SelectedProduct.Id);
            if (p is not null) await repo.DeleteAsync(p);
            Products.Remove(SelectedProduct);
            SelectedProduct = null;
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
    }

    // ========== COMPANIES ==========
    public ObservableCollection<Company> Companies { get; } = [];
    [ObservableProperty] private Company? _selectedCompany;
    [ObservableProperty] private bool _isCompanyEditorOpen;
    [ObservableProperty] private string _editCompanyName = "";
    [ObservableProperty] private string _editCompanyRut = "";
    [ObservableProperty] private string _editCompanyAddress = "";
    [ObservableProperty] private string _editCompanyPhone = "";
    [ObservableProperty] private bool _editCompanyIsActive = true;

    [RelayCommand]
    private async Task LoadCompaniesAsync()
    {
        var ctx = _db.CreateContext();
        var repo = new Repository<Company>(ctx);
        var items = await repo.GetAllAsync();
        App.Current.Dispatcher.Invoke(() =>
        {
            Companies.Clear();
            foreach (var c in items.OrderBy(x => x.Name))
                Companies.Add(c);
        });
    }

    [RelayCommand]
    private void NewCompany()
    {
        SelectedCompany = null;
        EditCompanyName = "";
        EditCompanyRut = "";
        EditCompanyAddress = "";
        EditCompanyPhone = "";
        EditCompanyIsActive = true;
        _companyEditor.Begin();
        IsCompanyEditorOpen = true;
    }

    [RelayCommand]
    private void EditCompany()
    {
        if (SelectedCompany is null) return;
        EditCompanyName = SelectedCompany.Name;
        EditCompanyRut = SelectedCompany.RUT;
        EditCompanyAddress = SelectedCompany.Address;
        EditCompanyPhone = SelectedCompany.Phone;
        EditCompanyIsActive = SelectedCompany.IsActive;
        _companyEditor.Begin();
        IsCompanyEditorOpen = true;
    }

    [RelayCommand]
    private async Task SaveCompanyAsync()
    {
        if (string.IsNullOrWhiteSpace(EditCompanyName)) return;
        try
        {
            var ctx = _db.CreateContext();
            var repo = new Repository<Company>(ctx);
            if (SelectedCompany is not null)
            {
                SelectedCompany.Name = EditCompanyName.Trim();
                SelectedCompany.RUT = EditCompanyRut.Trim();
                SelectedCompany.Address = EditCompanyAddress.Trim();
                SelectedCompany.Phone = EditCompanyPhone.Trim();
                SelectedCompany.IsActive = EditCompanyIsActive;
                await repo.UpdateAsync(SelectedCompany);
            }
            else
            {
                var c = new Company
                {
                    Name = EditCompanyName.Trim(),
                    RUT = EditCompanyRut.Trim(),
                    Address = EditCompanyAddress.Trim(),
                    Phone = EditCompanyPhone.Trim(),
                    IsActive = EditCompanyIsActive
                };
                await repo.AddAsync(c);
                Companies.Add(c);
            }
            IsCompanyEditorOpen = false;
            SelectedCompany = null;
            await LoadCompaniesAsync();
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    private void CancelCompanyEdit() => IsCompanyEditorOpen = false;

    [RelayCommand]
    private async Task DeleteCompanyAsync()
    {
        if (SelectedCompany is null) return;
        if (!ConfirmDelete("empresa", SelectedCompany.Name)) return;
        try
        {
            var ctx = _db.CreateContext();
            var repo = new Repository<Company>(ctx);
            var c = await repo.GetByIdAsync(SelectedCompany.Id);
            if (c is not null) await repo.DeleteAsync(c);
            Companies.Remove(SelectedCompany);
            SelectedCompany = null;
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
    }

    // ========== DRIVERS ==========
    public ObservableCollection<Driver> Drivers { get; } = [];
    [ObservableProperty] private Driver? _selectedDriver;
    [ObservableProperty] private bool _isDriverEditorOpen;
    [ObservableProperty] private string _editDriverName = "";
    [ObservableProperty] private string _editDriverPhone = "";
    [ObservableProperty] private Company? _editDriverCompany;
    [ObservableProperty] private bool _editDriverIsActive = true;

    [RelayCommand]
    private async Task LoadDriversAsync()
    {
        var ctx = _db.CreateContext();
        var repo = new Repository<Driver>(ctx);
        var items = await repo.GetAllAsync();
        var companies = await new Repository<Company>(ctx).GetAllAsync();
        var companyDict = companies.ToDictionary(c => c.Id);
        App.Current.Dispatcher.Invoke(() =>
        {
            Drivers.Clear();
            foreach (var d in items.OrderBy(x => x.FullName))
            {
                companyDict.TryGetValue(d.CompanyId, out var company);
                d.Company = company;
                Drivers.Add(d);
            }
        });
    }

    [RelayCommand]
    private void NewDriver()
    {
        SelectedDriver = null;
        EditDriverName = "";
        EditDriverPhone = "";
        EditDriverCompany = Companies.FirstOrDefault();
        EditDriverIsActive = true;
        _driverEditor.Begin();
        IsDriverEditorOpen = true;
    }

    [RelayCommand]
    private void EditDriver()
    {
        if (SelectedDriver is null) return;
        EditDriverName = SelectedDriver.FullName;
        EditDriverPhone = SelectedDriver.Phone ?? "";
        EditDriverCompany = Companies.FirstOrDefault(c => c.Id == SelectedDriver.CompanyId);
        EditDriverIsActive = SelectedDriver.IsActive;
        _driverEditor.Begin();
        IsDriverEditorOpen = true;
    }

    [RelayCommand]
    private async Task SaveDriverAsync()
    {
        if (string.IsNullOrWhiteSpace(EditDriverName) || EditDriverCompany is null) return;
        try
        {
            var ctx = _db.CreateContext();
            var repo = new Repository<Driver>(ctx);
            if (SelectedDriver is not null)
            {
                SelectedDriver.FullName = EditDriverName.Trim();
                SelectedDriver.Phone = EditDriverPhone.Trim();
                SelectedDriver.CompanyId = EditDriverCompany.Id;
                SelectedDriver.IsActive = EditDriverIsActive;
                await repo.UpdateAsync(SelectedDriver);
            }
            else
            {
                var d = new Driver
                {
                    FullName = EditDriverName.Trim(),
                    Phone = EditDriverPhone.Trim(),
                    CompanyId = EditDriverCompany.Id,
                    IsActive = EditDriverIsActive
                };
                await repo.AddAsync(d);
                Drivers.Add(d);
            }
            IsDriverEditorOpen = false;
            SelectedDriver = null;
            await LoadDriversAsync();
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    private void CancelDriverEdit() => IsDriverEditorOpen = false;

    [RelayCommand]
    private async Task DeleteDriverAsync()
    {
        if (SelectedDriver is null) return;
        if (!ConfirmDelete("conductor", SelectedDriver.FullName)) return;
        try
        {
            var ctx = _db.CreateContext();
            var repo = new Repository<Driver>(ctx);
            var d = await repo.GetByIdAsync(SelectedDriver.Id);
            if (d is not null) await repo.DeleteAsync(d);
            Drivers.Remove(SelectedDriver);
            SelectedDriver = null;
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
    }

    // Eliminar no tiene vuelta atrás: se pide confirmación antes.
    private static bool ConfirmDelete(string kind, string name) =>
        System.Windows.MessageBox.Show($"¿Eliminar {kind} \"{name}\"?\nEsta acción no se puede deshacer.", "Confirmar eliminación",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;

    // ========== STATUS ==========
    [ObservableProperty] private string _statusMessage = "";
}

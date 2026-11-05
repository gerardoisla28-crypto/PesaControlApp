namespace PesaControl.Core.Models;

public enum WeighingStatus
{
    WeighingIn,
    WeighingOut,
    Completed,
    Cancelled
}

public enum SyncStatus
{
    // Creado o modificado en este puesto y aún no confirmado por el servidor central.
    Pending,
    Synced
}

public class WeighingRecord : System.ComponentModel.INotifyPropertyChanged
{
    public int Id { get; set; }
    public string TicketNumber { get; set; } = "";
    public string StationCode { get; set; } = "";
    public DateTime WeighInTime { get; set; }
    public DateTime? WeighOutTime { get; set; }
    public decimal GrossWeight { get; set; }
    public decimal TareWeight { get; set; }
    public decimal NetWeight { get; set; }
    // Decimales del visor con el que se tomaron los pesos (0 si el visor no muestra decimales).
    public int WeightDecimals { get; set; }
    public string LicensePlate { get; set; } = "";
    public string PlateCountry { get; set; } = "CL";
    public string OperatorName { get; set; } = "";
    public string DriverName { get; set; } = "";
    public int? ProductId { get; set; }
    public int? CompanyId { get; set; }
    public WeighingStatus Status { get; set; } = WeighingStatus.WeighingIn;
    public string? PhotoPath { get; set; }
    public string? PhotoPathExit { get; set; }
    public string? EntryScaleName { get; set; }
    public string? ExitScaleName { get; set; }
    public bool IsExitWeightManual { get; set; }
    public string Notes { get; set; } = "";

    // Última modificación en este puesto (hora local). La asigna el almacén local en cada guardado.
    public DateTime ModifiedAt { get; set; } = DateTime.Now;
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;
    // Cuándo el servidor central confirmó este pesaje; nulo mientras esté pendiente.
    public DateTime? SyncedAt { get; set; }

    // Estado de la pantalla de Registros (casilla de exportación): no se guarda ni viaja en los respaldos.
    private bool _isSelectedForExport;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsSelectedForExport
    {
        get => _isSelectedForExport;
        set
        {
            if (_isSelectedForExport == value) return;
            _isSelectedForExport = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelectedForExport)));
        }
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    [System.Text.Json.Serialization.JsonIgnore]
    public string GrossWeightText => WeightFormat.Format(GrossWeight, WeightDecimals);

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    [System.Text.Json.Serialization.JsonIgnore]
    public string TareWeightText => WeightFormat.Format(TareWeight, WeightDecimals);

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    [System.Text.Json.Serialization.JsonIgnore]
    public string NetWeightText => WeightFormat.Format(NetWeight, WeightDecimals);

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public Product? Product { get; set; }
    public Company? Company { get; set; }
}

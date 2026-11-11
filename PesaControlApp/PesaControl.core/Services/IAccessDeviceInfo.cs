namespace PesaControl.Core.Services;

/// <summary>
/// Informa qué salidas tiene realmente instaladas un equipo de control de acceso
/// (barrera, semáforo o ambas). Se llena al conectar, tras preguntarle al equipo.
/// </summary>
public interface IAccessDeviceInfo
{
    bool HasBarrier { get; }
    bool HasTrafficLight { get; }
}

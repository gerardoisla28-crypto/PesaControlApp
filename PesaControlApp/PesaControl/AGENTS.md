# PesaControl

App WPF de control industrial (.NET 10) para estaciones de pesaje de camiones: lectura de báscula, cámaras/DVR, ANPR de placas, barrera, semáforo y tickets. Solución de 5 proyectos en directorios hermanos, más un sketch Arduino.

## Estructura

```
PesaControl/              → PesaControl.UI (WPF WinExe, net10.0-windows) + PesaControl.slnx
PesaControl.core/         → PesaControl.Core (class lib, net10.0): modelos, protocolos, dispositivos, seguridad
PesaControl.data/         → PesaControl.Data (class lib, net10.0): EF Core/PostgreSQL (Npgsql), repositorios, exportación
PesaControl.Provisioner/  → consola para crear el SuperAdmin inicial (referencia Data)
SerialTest/               → consola de prueba del puerto serie
arduino/arduino_access_control/ → sketch .ino (barrera con servo + semáforo con 2 relés, ASCII a 9600 baud)
```

Cadena de dependencias: `UI → Core, Data → Core`; `Provisioner → Data`.

## Stack

- WPF + CommunityToolkit.Mvvm + MaterialDesignThemes
- Entity Framework Core + PostgreSQL (Npgsql) (`DatabaseService`, `AppDbContext`)
- System.IO.Ports (serial RS232/RS485/USB), NModbus (Modbus RTU/TCP)
- LiveCharts2 (gráficos), QuestPDF (PDF), ClosedXML (Excel)
- ANPR: PlateRecognizer (cloud, `PlateRecognizerAnprService`)
- Cámaras: LibVLCSharp / OpenCvSharp (RTSP/USB) y DVR Dahua (`DahuaDvrService`, `DahuaHttpClient`)
- Argon2id (Konscious) para hash de contraseñas (`Core/Security`)

El archivo de solución es `.slnx` (formato XML de VS2022+, no el `.sln` clásico). Debe compilarse desde `PesaControl/` o usando la ruta al `.slnx`.

## Módulos principales

- **Pesaje**: `ScaleManager`, `WeighingService` y protocolos `IWeighingProtocol` (Sensocar A12, Sensoc, Modbus RTU/TCP, serial, autodetección).
- **Acceso**: `IBarrierService` / `ITrafficLightService` con implementaciones Modbus y Arduino (`ArduinoAccessService` en UI).
- **Tickets**: `ITicketPrinter` con `EscPosPrinter` y `WindowsPrinter`.
- **Datos**: migraciones EF (`InitialCreate`), exportación/importación (`ExportImportService`), Excel y PDF.
- **Usuarios**: roles en `UserRoles` (SuperAdmin, Admin, Operator, Viewer); `AuthService` en UI.

## Comandos

```powershell
dotnet build ./PesaControl/PesaControl.slnx
dotnet run --project ./PesaControl/PesaControl.UI.csproj
dotnet run --project ./PesaControl.Provisioner   # crear SuperAdmin (pide cadena de conexión, usuario y contraseña)
```

No hay proyectos de pruebas automatizadas.

## Gotchas

- `AuthService` se instancia manualmente (`new AuthService()`). Migrar a inyección de dependencias en fases posteriores.
- Las contraseñas ya usan Argon2id; `DatabaseService.MigrateUserPasswordsAsync` convierte contraseñas legadas al iniciar.
- La cadena de conexión sale de la variable de entorno `PESACONTROL_DB` o de `dbsettings.json` (ver `dbsettings.example.json`). `DatabaseService.InitializeAsync` aplica migraciones EF (`MigrateAsync`); `dotnet dotnet-ef` está como herramienta local.
- `PlateRecord.Source` menciona PaddleOCR, pero no hay implementación local de PaddleOCR en el código; solo PlateRecognizer y entrada manual.

# PesaControl

App WPF de control industrial (.NET 10) para estaciones de pesaje de camiones: lectura de báscula, cámaras/DVR, ANPR de placas, barrera, semáforo y tickets. Solución de 5 proyectos en directorios hermanos, más un sketch Arduino.

## Estructura

```
PesaControl/              → PesaControl.UI (WPF WinExe, net10.0-windows) + PesaControl.slnx
PesaControl.core/         → PesaControl.Core (class lib, net10.0): modelos, protocolos, dispositivos, seguridad
PesaControl.data/         → PesaControl.Data (class lib, net10.0): EF Core: SQLite (almacén local del puesto) y PostgreSQL (servidor central), repositorios, exportación
PesaControl.Provisioner/  → consola para crear el SuperAdmin inicial (referencia Data)
SerialTest/               → consola de prueba del puerto serie
arduino/arduino_access_control/ → sketch .ino (barrera con servo + semáforo con 2 relés, ASCII a 9600 baud)
```

Cadena de dependencias: `UI → Core, Data → Core`; `Provisioner → Data`.

## Stack

- WPF + CommunityToolkit.Mvvm + MaterialDesignThemes
- Entity Framework Core: SQLite como almacén local de cada puesto y PostgreSQL (Npgsql) como servidor central (`DatabaseService`, `AppDbContext`, `LocalDbContext`, `CentralDbContext`)
- System.IO.Ports (serial RS232/RS485/USB), NModbus (Modbus RTU/TCP)
- LiveCharts2 (gráficos), QuestPDF (PDF), ClosedXML (Excel)
- ANPR: PlateRecognizer (cloud, `PlateRecognizerAnprService`)
- Cámaras: LibVLCSharp / OpenCvSharp (RTSP/USB) y DVR Dahua (`DahuaDvrService`, `DahuaHttpClient`)
- Argon2id (Konscious) para hash de contraseñas (`Core/Security`)

El archivo de solución es `.slnx` (formato XML de VS2022+, no el `.sln` clásico). Debe compilarse desde `PesaControl/` o usando la ruta al `.slnx`.

## Módulos principales

- **Intercambio entre puestos**: `WeighingExchangeService` exporta los pesajes pendientes (`ExportPending`) a un zip con `exchange.json`, `manifest.json` (suma de verificación) y fotos, e importa fusionando sin borrar (empareja empresas por RUT y luego por nombre; ante datos distintos conserva el local e informa el conflicto). Pantalla: Registros (casillas, filtro Exportación, exportar seleccionados o pendientes e importar; solo SuperAdmin, Admin y Operator).
- **Tickets numerados por puesto**: `LocalWeighingDataService.CreateAsync` asigna `CÓDIGO-AAAA-MM-NNNN` en la misma transacción que guarda el pesaje; el código de puesto (`station_code`, 1 a 4 caracteres) se pide al primer arranque y no viaja en los respaldos.
- **Pesaje**: `ScaleManager`, `WeighingService` y protocolos `IWeighingProtocol` (Sensocar A12, Sensoc, Modbus RTU/TCP, serial, autodetección).
- **Acceso**: `IBarrierService` / `ITrafficLightService` con implementaciones Modbus y Arduino (`ArduinoAccessService` en UI).
- **Tickets**: `ITicketPrinter` con `EscPosPrinter` y `WindowsPrinter`.
- **Datos**: migraciones EF por proveedor en `Migrations/Sqlite` y `Migrations/Postgres` (`InitialCreate`), exportación/importación (`ExportImportService`), Excel y PDF.
- **Usuarios**: roles en `UserRoles` (SuperAdmin, Admin, Operator, Viewer); `AuthService` en UI.

## Comandos

```powershell
dotnet build ./PesaControl/PesaControl.slnx
dotnet run --project ./PesaControl/PesaControl.UI.csproj
dotnet run --project ./PesaControl.Provisioner   # crear SuperAdmin (pide ruta de la base local, usuario y contraseña)
```

No hay proyectos de pruebas automatizadas.

## Gotchas

- `AuthService` se instancia manualmente (`new AuthService()`). Migrar a inyección de dependencias en fases posteriores.
- Las contraseñas usan Argon2id (`Core/Security`), junto con las políticas de contraseña, usuario y nombre.
- `AppDbContext` es la base abstracta con el modelo compartido. La app solo usa `LocalDbContext` (SQLite, archivo en `%LocalAppData%/PesaControl/pesacontrol.db`, o la ruta de `PESACONTROL_DB_PATH`). `CentralDbContext` (PostgreSQL) es el lado servidor y aún no lo usa la app: se conectará con la sincronización (ver `PLAN_MEJORAS.md`).
- Migraciones: `dotnet dotnet-ef migrations add Nombre --context LocalDbContext --project PesaControl.data --output-dir Migrations/Sqlite` (y `--context CentralDbContext --output-dir Migrations/Postgres` para PostgreSQL). Ambos contextos deben generar migraciones cuando cambie el modelo.
- `PlateRecord.Source` menciona PaddleOCR, pero no hay implementación local de PaddleOCR en el código; solo PlateRecognizer y entrada manual.

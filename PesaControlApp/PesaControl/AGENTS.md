# PesaControl

App WPF de control industrial (.NET 10). Solución de 3 proyectos distribuidos en directorios hermanos.

## Estructura

```
PesaControl/             → PesaControl.UI (WPF WinExe, net10.0-windows)
PesaControl.core/        → PesaControl.Core (class lib, net10.0)
PesaControl.data/        → PesaControl.Data (class lib, net10.0)
```

Cadena de dependencias: `UI → Core, Data → Core`.

## stack

- WPF + CommunityToolkit.Mvvm + MaterialDesignThemes
- Entity Framework Core + SQLite (Microsoft.EntityFrameworkCore.Sqlite)
- System.IO.Ports (serial RS232/RS485/USB), EasyModbus (Modbus RTU/TCP)
- LiveCharts2 (gráficos), QuestPDF (PDF), ClosedXML (Excel)
- PaddleOCR (ANPR local), PlateRecognizer (ANPR cloud)
- LibVLC / OpenCvSharp (cámaras RTSP/USB)

El archivo de solución es `.slnx` (formato XML de VS2022+, no el `.sln` clásico). Debe compilarse desde `PesaControl/` o usando la ruta al `.slnx`.

## Comandos

```powershell
dotnet build ./PesaControl/PesaControl.slnx
dotnet run --project ./PesaControl/PesaControl.UI.csproj
```

El proyecto ya incluye `.editorconfig`, `.gitignore`, `Directory.Build.props`.

## Gotchas

- `AuthService` se instancia manualmente (`new AuthService()`). Migrar a inyección de dependencias en fases posteriores.
- Falta migrar `User.Password` de texto plano a hash (PBKDF2/BCrypt) — pendiente.

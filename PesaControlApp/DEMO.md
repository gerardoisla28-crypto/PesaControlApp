# Modo demostración

Para grabar videos o mostrar el producto sin hardware. Usa una base de datos aparte y no toca la del puesto.

```powershell
.\run-demo.ps1          # abrir la demo
.\run-demo.ps1 -Reset   # recrearla con fechas al día (los últimos 30 días)
```

Qué prepara (`PesaControl.data/Data/DemoDataSeeder.cs`):

- Datos ficticios chilenos: 6 empresas con RUT válido, choferes, 9 productos y unos 110 pesajes de los últimos 30 días, con patentes `BBCL-45`, 3 tickets anulados y 2 camiones dentro de la planta esperando la salida.
- Código de puesto `STGO` y tickets `STGO-AAAA-MM-NNNN`.
- Báscula simulada (`SimulatorProtocol`): el peso sube, se estabiliza unos 20 segundos, baja y se repite con otro camión. También se puede elegir "Simulador" en Configuración › Básculas.
- Cámaras desactivadas, porque no hay hardware.
- Todo lo anterior lo crea la demostración; la base de una instalación real arranca vacía, sin empresas, conductores, productos ni básculas de ejemplo.
- Los operadores de los pesajes son los usuarios con rol Operador (aquí `operador` y `operadora`).

Usuarios de la base de demostración (no sirven en una instalación real):

| Usuario    | Rol      | Contraseña            |
|------------|----------|-----------------------|
| `admin`    | Admin    | `AdminDemo2026-Chile` |
| `operador` | Operador | `OperadorDemo2026`    |
| `operadora` | Operador | `OperadoraDemo2026`   |

El sembrado solo corre si `PESACONTROL_DEMO=1` **y** `PESACONTROL_DB_PATH` apunta a una base aparte; si no, se niega a ejecutarse.

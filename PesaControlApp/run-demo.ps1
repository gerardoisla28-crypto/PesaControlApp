# Abre Control de Peso en modo demostración: base aparte, báscula simulada y datos ficticios chilenos.
# No toca la base real del puesto (%LocalAppData%\PesaControl).
#   .\run-demo.ps1          abre la demo (la primera vez crea los datos de los últimos 30 días)
#   .\run-demo.ps1 -Reset   borra la base de demostración y la crea de nuevo con fechas al día
param([switch]$Reset)

$demoDir = Join-Path $env:LOCALAPPDATA 'PesaControlDemo'
if ($Reset) { Remove-Item $demoDir -Recurse -Force -ErrorAction SilentlyContinue }

$env:PESACONTROL_DB_PATH = Join-Path $demoDir 'pesacontrol-demo.db'
$env:PESACONTROL_PHOTOS_PATH = Join-Path $demoDir 'Photos'
$env:PESACONTROL_DEMO = '1'

dotnet run --project (Join-Path $PSScriptRoot 'PesaControl\PesaControl.UI.csproj')

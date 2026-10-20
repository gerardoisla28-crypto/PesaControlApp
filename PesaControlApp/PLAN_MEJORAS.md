# Plan de mejoras de PesaControl (versión comercial)

Objetivo: dejar PesaControl terminado y estable para empezar a venderlo, tomando como referencia lo que ya tiene Weight-Trak.

> Basado en la estructura de archivos y búsquedas puntuales (`EnsureCreated`, cadena de conexión SQLite, `catch` genéricos). No es una auditoría completa: validar cada punto antes de ejecutarlo.

## A. Cambios que pueden romper el proyecto actual

Hacer respaldo de la base de datos y trabajar en una rama de git antes de cada uno. Ordenados de mayor a menor riesgo.

### 1. Pasar de `EnsureCreated` a migraciones EF puras
- Archivos: `PesaControl.data/Data/DatabaseService.cs:25`, `PesaControl.data/Services/ExportImportService.cs:146`.
- Riesgo: una base creada con `EnsureCreated` no tiene `__EFMigrationsHistory`. Activar `Migrate()` sin más intentará recrear tablas existentes y fallará.
- Hoy hay una sola migración (`AddDvrSupport`) y ajustes manuales (`MigrateLegacyScaleConfigAsync`).
- Plan:
  - Crear una migración baseline.
  - Detectar bases existentes e insertar la baseline en el historial a mano.
  - Mover los ajustes manuales a migraciones.
- Es el cambio más importante: sin él no se pueden actualizar clientes con datos.

### 2. Cifrar la base de datos (SQLCipher)
- Riesgo: la clave tiene que venir de algún lado; una base sin cifrar no se abre con clave, hay que migrar los datos a un archivo nuevo cifrado. Si se pierde la clave, se pierden los datos.
- Requiere `SQLitePCLRaw.bundle_e_sqlcipher` en lugar del provider actual de EF Core.
- Decidir si la clave se deriva del HWID (queda atada a la licencia).

### 3. Sistema de licencias con HWID
- Riesgo: si el bloqueo falla (cambio de disco o placa), un cliente pagado queda fuera.
- Necesita:
  - Generador de claves aparte (herramienta propia, nunca incluida en el instalador).
  - Periodo de gracia.
  - Proceso de reactivación.
- Weight-Trak lo declara en su README, pero no tiene una implementación completa que copiar.

### 4. Reestructurar en capas (Domain / Application / Infrastructure)
- Riesgo alto: ~12.900 líneas, toca todo. No hacerlo antes de vender.
- Alternativa: aplicar solo inyección de dependencias y casos de uso nuevos para los flujos críticos (login, pesaje, cierre de ticket), dejando el resto como está.

### 5. Hash de contraseñas
- Mantener Argon2id (mejor que el BCrypt de Weight-Trak). **No cambiar.**

### 6. Esquema de roles y usuarios
- Riesgo medio: hay que migrar los usuarios existentes. `AuthService` se crea con `new AuthService()`.
- Hacerlo junto con la inyección de dependencias.

## B. Cambios seguros

### Imprescindibles para vender
- [ ] **Pruebas automáticas** (hoy hay cero). Prioridad: `WeighingService`, protocolos de báscula (con tramas grabadas), `Argon2idPasswordHasher`, repositorios con SQLite en memoria, `ExportImportService`. Usar los fakes de Weight-Trak como modelo.
- [ ] **Auditoría**: registrar quién hizo qué (login, anulación o edición de pesajes, cambios de configuración y de usuarios).
- [ ] **Protección del último SuperAdmin**: no permitir eliminarlo ni degradarlo (`GuardaUltimoSuperAdmin` en Weight-Trak).
- [ ] **Pantalla de primer arranque** dentro de la app, que reemplace al proyecto `Provisioner`.
- [ ] **Instalador** (MSIX, WiX o Inno Setup):
  - Datos en `%ProgramData%` o `%AppData%`, no junto al ejecutable.
  - Runtime incluido (self-contained).
  - Dependencias nativas de OpenCV y LibVLC empaquetadas.
- [ ] **Respaldos automáticos y verificados**: revisar `BackupViewModel`, probar la restauración y añadir rotación.
- [ ] **Registro de errores a archivo** (Serilog o similar). Hay ~28 `catch` que tragan errores, lo que impide dar soporte.

### Seguridad
- [ ] Revisar dónde se guarda la clave de PlateRecognizer (no en texto plano ni en el repositorio; usar DPAPI).
- [ ] Bloqueo tras intentos fallidos de login, con aviso al operador de contactar a Administración.
- [ ] Confirmación de contraseña antes de acciones destructivas.

### Funcionalidad a tomar de Weight-Trak (según el mercado)
- [ ] Reglas de peso por eje y validación MOP (solo si se vende en Chile o para control vial).
- [ ] Maestros más completos: transportistas, clientes, lugares, vehículos (revisar qué cubre `CatalogsViewModel`).
- [ ] Flujos de recepción, despacho y servicio, con tipos de ticket.
- [ ] Reportes por folio y fechas; añadir exportación a Word (Excel y PDF ya existen).
- [ ] Control de contraseña reutilizable.

### Calidad
- [ ] Quitar código ambiguo (p. ej. mención a PaddleOCR en `PlateRecord.Source` sin implementación).
- [ ] Dejar `SerialTest` fuera del instalador y documentado.
- [ ] CI mínimo con `dotnet build` y `dotnet test`.
- [ ] README y manual de instalación y soporte.

## Orden de trabajo sugerido

1. Respaldo y baseline de migraciones (A1).
2. Registro de errores, auditoría y protección del último SuperAdmin.
3. Primer arranque dentro de la app e instalador.
4. Pruebas sobre la lógica de pesaje.
5. Cifrado de la base (A2) y licencias (A3), en ese orden: la clave de cifrado puede depender del HWID.
6. Inyección de dependencias gradual (A4/A6).

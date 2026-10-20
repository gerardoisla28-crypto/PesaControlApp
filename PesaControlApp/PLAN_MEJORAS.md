# Plan de mejoras de PesaControl (versión comercial)

Objetivo: dejar PesaControl terminado y estable para empezar a venderlo, tomando como referencia lo que ya tiene Weight-Trak.

> Basado en la estructura de archivos y búsquedas puntuales (`EnsureCreated`, cadena de conexión SQLite, `catch` genéricos). No es una auditoría completa: validar cada punto antes de ejecutarlo.

## Decisiones tomadas

- **Multi-puesto desde el inicio**: varios puestos de pesaje comparten los mismos registros.
- **Motor de base de datos: PostgreSQL** (reemplaza a SQLite). Sin límite práctico de tamaño y sin costo de licencia.
- **Fotos de cámaras: archivos en una carpeta compartida del servidor**, no dentro de la base. La base guarda la ruta relativa.
- **El servidor puede ser un PC normal** del cliente (siempre encendido, IP fija). Ver [REQUISITOS_CLIENTE.md](REQUISITOS_CLIENTE.md).
- **Dos modos de instalación**: "servidor + puesto" y "solo puesto".

## Decisiones pendientes

- **¿Guardar las fotos en un servidor en la nube?** Por ahora se usa la carpeta compartida local. Revisar más adelante como opción adicional. A considerar: dependencia de internet en los puestos, costo mensual de almacenamiento, seguridad del servicio expuesto y responsabilidad sobre los datos de todos los clientes.

## Multi-puesto (nuevo, implica cambios en el código)

Estado actual: las fotos se guardan en `%LocalAppData%\PesaControl\Photos` con ruta absoluta (`WeighingViewModel.cs:562`), por lo que otro puesto no las ve.

- [ ] Cambiar `PhotoPath` y `PhotoPathExit` a rutas **relativas** (p. ej. `2026/10/T-1234-entry.jpg`) con carpeta raíz configurable.
- [ ] Organizar las fotos por año y mes.
- [ ] Si la red falla, guardar la foto en local y subirla después.
- [ ] Quitar el `catch { }` vacío al guardar fotos: hoy un fallo pasa desapercibido.
- [ ] Separar la **configuración por puesto** (báscula, puerto serie, cámaras, impresora) de la configuración compartida.
- [ ] Numeración de tickets sin duplicados entre puestos.
- [ ] Control de concurrencia: dos puestos editando o cerrando el mismo ticket.
- [ ] Definir qué hace un puesto sin conexión al servidor (bloquear o permitir pesar sin conexión).
- [ ] Registrar en la auditoría desde qué puesto se hizo cada acción.
- [ ] Respaldos en el servidor, de la base y de las fotos por separado.
- [ ] Política de retención de fotos.
- [ ] Pantalla de primer arranque con los modos "servidor + puesto" y "solo puesto" (dirección del servidor y carpeta de fotos).

## A. Cambios que pueden romper el proyecto actual

Hacer respaldo de la base de datos y trabajar en una rama de git antes de cada uno. Ordenados de mayor a menor riesgo.

### 1. Migrar a PostgreSQL con migraciones EF puras
- Archivos: `PesaControl.data/Data/DatabaseService.cs:25`, `PesaControl.data/Services/ExportImportService.cs:146`.
- Hoy: SQLite con `EnsureCreated`, una sola migración (`AddDvrSupport`) y ajustes manuales (`MigrateLegacyScaleConfigAsync`, SQL crudo tipo `ALTER TABLE ... TEXT`).
- No hay clientes con datos, así que **no hace falta detectar bases existentes ni insertar baseline a mano**. Basta con:
  - Cambiar el proveedor a Npgsql (PostgreSQL).
  - Borrar las migraciones actuales y generar una migración inicial limpia que incluya el esquema completo, absorbiendo `AddDvrSupport` y los ajustes manuales.
  - Reemplazar `EnsureCreated` por `Migrate()`.
  - Revisar SQL crudo y tipos específicos de SQLite (`TEXT`, `BLOB`).
- Revisar `ExportImportService` (importa/exporta con rutas de fotos y base local).
- Es el cambio más importante: sin migraciones no se pueden actualizar clientes con datos.
- Hacerlo en una rama de git; las bases SQLite de desarrollo se descartan.

### 2. Seguridad de la base de datos (reemplaza a SQLCipher)
- SQLCipher solo aplica a SQLite, por lo que se descarta.
- Usuario de base propio con contraseña fuerte (no `postgres`), generada por el instalador.
- Conexión solo desde la red local; no exponer el puerto a internet.
- Cadena de conexión protegida (DPAPI), no en texto plano.
- Conexión TLS si hay varios puestos en red.
- Respaldos cifrados o en ubicación con acceso restringido.

### 3. Sistema de licencias con HWID
- Riesgo: si el bloqueo falla (cambio de disco o placa), un cliente pagado queda fuera.
- Con multi-puesto hay que decidir si se licencia por puesto, por servidor o por número de puestos.
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

### Opcional
- [ ] **Importación de catálogos desde Excel/CSV con plantilla**, para clientes que vienen de otro software de pesaje y quieren cargar sus maestros (transportistas, clientes, lugares, vehículos, productos).
  - Ofrecer una plantilla descargable por catálogo, con columnas fijas y ejemplos.
  - Validar antes de importar: columnas requeridas, duplicados, formatos (patentes, RUT u otros identificadores) y mostrar una vista previa con errores por fila.
  - Importación transaccional: o entra todo o no entra nada; opción de omitir o actualizar duplicados.
  - Registrar la importación en la auditoría.
  - Alcance: solo catálogos. No leer bases de otros programas ni importar historial de pesajes.
  - Reutilizar lo que ya exista en `ExportImportService` y las librerías de Excel/CSV del proyecto.

### Calidad
- [ ] Quitar código ambiguo (p. ej. mención a PaddleOCR en `PlateRecord.Source` sin implementación).
- [ ] Dejar `SerialTest` fuera del instalador y documentado.
- [ ] CI mínimo con `dotnet build` y `dotnet test`.
- [ ] README y manual de instalación y soporte.

## Orden de trabajo sugerido

1. Migración a PostgreSQL y migración inicial limpia (A1).
2. Multi-puesto: fotos con ruta relativa y carpeta compartida, configuración por puesto, numeración y concurrencia.
3. Registro de errores, auditoría (con el puesto de origen) y protección del último SuperAdmin.
4. Primer arranque dentro de la app e instalador con los dos modos (servidor + puesto, solo puesto).
5. Pruebas sobre la lógica de pesaje (con PostgreSQL de prueba o contenedor, no SQLite en memoria).
6. Seguridad de la base (A2) y licencias (A3).
7. Inyección de dependencias gradual (A4/A6).

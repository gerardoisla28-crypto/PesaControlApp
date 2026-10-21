# Plan de mejoras de PesaControl (versión comercial)

Objetivo: dejar PesaControl terminado y estable para empezar a venderlo, tomando como referencia lo que ya tiene Weight-Trak.

> Basado en la estructura de archivos y búsquedas puntuales (`EnsureCreated`, cadena de conexión SQLite, `catch` genéricos). No es una auditoría completa: validar cada punto antes de ejecutarlo.

## Avance (actualizado el 2026-10-20)

Todo lo hecho está en la rama `feature/postgresql` (aún sin fusionar a `dev`). Probado contra PostgreSQL 18 local: el login entra al panel.

### Hecho
- [x] **A1: migración a PostgreSQL** con migración inicial limpia (`InitialCreate`), `MigrateAsync` en lugar de `EnsureCreated`, y sin el SQL crudo de SQLite. `dotnet-ef` queda como herramienta local.
- [x] **Cadena de conexión** desde la variable de entorno `PESACONTROL_DB` o `dbsettings.json` (ignorado por git; plantilla en `dbsettings.example.json`).
- [x] **Importación de respaldos transaccional**: ya no borra un archivo `.db`; vacía las tablas dentro de una transacción y realinea las secuencias de IDs.
- [x] **Fechas**: guardadas como `timestamp without time zone` (hora local). Un convertidor pasa a local las fechas en UTC al guardar (`LocalDateTimeConverter`).
- [x] **Políticas de credenciales** (`PesaControl.core/Security`):
  - Contraseña: 14 a 128 caracteres para SuperAdmin y Admin; 8 a 128 para Operator y Viewer. Sin reglas de composición (criterio NIST).
  - Usuario: 3 a 50 caracteres ASCII (letras, números, punto, guion y guion bajo), sin espacios. El login distingue mayúsculas y minúsculas.
  - Nombre completo: espacios normalizados y máximo 100 caracteres.
- [x] **Pantalla "Crear Admin"**: usuario libre, nombre y apellido obligatorios, contraseña con confirmación y ojito, errores bajo cada campo, campos obligatorios marcados con `*`.
- [x] **Gestión de usuarios**: aplica las políticas y pide confirmar la contraseña; los campos de contraseña se limpian al reabrir el editor.
- [x] **Arranque**: la base se inicializa en `App.OnStartup`; si no hay Admin se muestra solo "Crear Admin" y luego el login, con un mensaje de confirmación.
- [x] **Login**: errores bajo cada campo y un único mensaje para credenciales incorrectas, Enter para ingresar, botones minimizar y cerrar, cierre con Esc y arrastre de la ventana.
- [x] **Bloqueo al entrar al panel corregido**: `DashboardWindow` esperaba consultas con `.GetAwaiter().GetResult()` en el hilo de la interfaz, lo que con PostgreSQL provoca un bloqueo mutuo. Ahora se carga con `DashboardWindow.CreateAsync`. El comando "Ver detalle" de registros tenía el mismo problema.
- [x] **Documentación**: `REQUISITOS_CLIENTE.md` y `AGENTS.md` actualizados.

### Aprendizajes y riesgos que quedaron
- **Esperas síncronas sobre código asíncrono**: con SQLite pasaban desapercibidas; con PostgreSQL congelan la interfaz. Quedan dos `.GetResult()` en `SensocarA12Protocol` y `SensocProtocol` (puerto serie al cerrar, no base de datos); revisar si hay otros al añadir módulos.
- **Fechas**: usar `DateTime.UtcNow` o `DateTime.Now` es indistinto para la base gracias al convertidor, pero conviene unificar el criterio al implementar la auditoría.
- **Faltan pruebas automáticas contra PostgreSQL**: tanto el error de las fechas como el del bloqueo habrían aparecido en una prueba de integración básica (crear usuario, iniciar sesión, abrir el panel).
- **Pendientes menores detectados**:
  - Al renombrar un usuario no se comprueba que el nuevo nombre no esté en uso por otro (la base lo impide, pero con un error técnico).
  - Hay `catch` vacíos en el flujo de arranque y de cámaras.
  - El login muestra "No se pudo conectar con la base de datos" para cualquier excepción; conviene registrar la causa real cuando exista el registro de errores.

## Decisiones tomadas

- **Multi-puesto desde el inicio**: varios puestos de pesaje comparten los mismos registros.
- **Motor de base de datos: PostgreSQL** (reemplaza a SQLite). Sin límite práctico de tamaño y sin costo de licencia.
- **Fotos de cámaras: archivos en una carpeta compartida del servidor**, no dentro de la base. La base guarda la ruta relativa.
- **El servidor puede ser un PC normal** del cliente (siempre encendido, IP fija). Ver [REQUISITOS_CLIENTE.md](REQUISITOS_CLIENTE.md).
- **Dos modos de instalación**: "servidor + puesto" y "solo puesto".
- **Fechas en hora local sin zona horaria**: todos los puestos de una instalación comparten zona horaria.
- **Usuario sensible a mayúsculas y minúsculas** en el login, a propósito.
- **Contraseñas**: se prioriza el largo sobre la complejidad; el mínimo es menor para Operator y Viewer, que las escriben con frecuencia en el puesto.
- **Cada puesto debe poder pesar con y sin conexión** (diseño local-first, ver la sección "Operación con y sin conexión"). Almacén local: **SQLite embebido**; servidor central: **PostgreSQL**.
- **Numeración de tickets por serie de puesto** (`A-2026-10-0001`, `B-2026-10-0001`): sin huecos dentro de cada puesto y sin depender de la red.
- **Camión que entra por un puesto y sale por otro sin conexión: se permite el registro manual** de la salida, con los datos del ticket impreso.
- **Sin límite de tiempo sin conexión**: un puesto puede trabajar indefinidamente sin servidor (incluso un cliente con un solo puesto sin red). Los registros se pueden **exportar a un archivo, llevarlos en un pendrive y cargarlos en otro equipo**.

## Decisiones pendientes

- **¿Guardar las fotos en un servidor en la nube?** Por ahora se usa la carpeta compartida local. Revisar más adelante como opción adicional. A considerar: dependencia de internet en los puestos, costo mensual de almacenamiento, seguridad del servicio expuesto y responsabilidad sobre los datos de todos los clientes.

## Operación con y sin conexión (local-first) — nuevo, es el cambio de arquitectura más grande

Cada puesto guarda y numera en un **almacén local** y sincroniza con el servidor cuando hay conexión. Sin servidor, el puesto sigue funcionando de forma autónoma y los datos viajan en archivos.

### Decisión tomada: almacén local SQLite y servidor central PostgreSQL (2026-10-21)
- Cada puesto usa **SQLite embebido** como almacén local (sin instalación ni administración) y funciona solo, sin servidor.
- **PostgreSQL es el servidor central**, usado cuando hay conexión para consolidar y compartir datos entre puestos.
- Implica mantener **dos proveedores de EF Core** (SQLite local y Npgsql central) y dos juegos de migraciones (`Migrations/Sqlite` y `Migrations/Postgres`), con las entidades compartidas.
- La migración a PostgreSQL ya hecha sigue siendo válida como lado servidor y como esquema. Lo que se rehace es qué base usa directamente cada puesto (hoy habla con PostgreSQL).
- Configuración y datos del puesto (código de puesto, báscula, cámaras, impresora, contador de tickets) viven **solo en el almacén local**, no en la base compartida.

### Diseño
- **Numeración:** cada puesto tiene un **código** (en su configuración) y una serie propia. El contador vive en el almacén local y se incrementa **en la misma transacción** que guarda el pesaje, por lo que no deja huecos. Anular un ticket (`Cancelled`) conserva su número; los pesajes nunca se borran.
- **Clave natural global:** el número con el código de puesto es único en todo el sistema, así que importar o sincronizar dos veces el mismo pesaje es **idempotente** (se omite o se fusiona, no se duplica).
- **Propiedad por campos:** los datos de entrada pertenecen al puesto que creó el ticket y los de salida al puesto que registró la salida. Al fusionar dos mitades del mismo ticket se toma cada grupo de campos de su dueño.
- **Salida en otro puesto sin conexión:** pantalla de **registro manual de salida** donde se escribe el número del ticket impreso, la placa y el peso de entrada. Al sincronizar, el servidor une las dos mitades por el número del ticket.
- **Login sin conexión:** copia local de los usuarios (con su hash Argon2id). Los cambios de usuarios y catálogos requieren conexión con el servidor.
- **Fotos:** se guardan siempre en local y se suben al sincronizar (o viajan dentro del archivo de intercambio).
- **Relojes:** guardar la hora del puesto y la de recepción en el servidor.

### Tareas
- [x] Código de puesto: 1 a 4 caracteres (letras y números, en mayúsculas), se pide en el primer arranque justo después de crear el Admin (también si una base existente no lo tiene) y el Admin puede cambiarlo en Configuración › Puesto. Vive solo en el almacén local y **no viaja en los respaldos**. *Hecho en la rama `feature/station-series`.*
- [x] (parte 1) Almacén local SQLite en cada puesto, con `LocalDbContext` y `CentralDbContext` sobre un modelo compartido y migraciones por proveedor. *Hecho en la rama `feature/local-store`.*
- [x] Contador local por serie de puesto, asignado en `CreateAsync` dentro de la misma transacción que guarda el pesaje (formato `STGO-2026-10-0001`, reinicio mensual). Probado: 100 pesajes concurrentes sin huecos y un guardado fallido no consume número. *Hecho.*
- [ ] Agregar a los pesajes: versión o fecha de modificación y estado de sincronización. *(Ya tienen el código de puesto, `StationCode`.)*
- [ ] Servicio de sincronización en segundo plano (subir pendientes, bajar catálogos y tickets abiertos), con reintentos.
- [ ] **Exportación incremental a archivo** (pesajes pendientes desde la última exportación, con sus fotos) e **importación que fusiona sin borrar** (no reutilizar tal cual la restauración actual, que vacía las tablas).
  - *Tramo 1 hecho en la rama `feature/file-exchange`:* marca `ExportPending` en cada pesaje (se activa al crear o modificar y se desactiva al exportar), servicio `WeighingExchangeService` y archivo zip (`exchange.json` con los pesajes y los datos de su empresa y producto en lugar de los Id locales, `manifest.json` con la suma de verificación y `photos/`). Probado: exportación de pendientes y de todo, nueva exportación solo con lo modificado y restauración completa sin alterar el estado.
  - *Decisiones:* los catálogos se emparejan por RUT (sin puntos ni guiones) y luego por nombre sin distinguir mayúsculas; sin cifrado por ahora; **sin fechas de modificación**: si el mismo ticket llega con datos distintos en el mismo grupo (entrada o salida), se conserva la versión local y se informa el conflicto en el resumen.
  - *Tramo 2 hecho:* importación que fusiona sin borrar (`WeighingExchangeService.ImportAsync`). Verifica la suma de verificación antes de tocar la base y es todo o nada; empareja empresas por RUT y luego por nombre, y productos por nombre; une la entrada y la salida de un mismo ticket; ante datos distintos conserva el local y lo informa; una anulación prevalece; importar dos veces no duplica; los pesajes insertados o actualizados quedan pendientes para seguir su camino y los sin cambios no. Probado con dos puestos simulados (STGO y RM), con archivo alterado, archivo ajeno, y reinstalación del propio puesto, cuyo contador de tickets continúa después del mayor número.
  - *Tramo 3 hecho:* pestaña **Intercambio** en Configuración (aparte de Respaldo, cuya importación sí reemplaza todos los datos) con **Exportar pendientes** (muestra cuántos hay), **Exportar todo**, **Importar archivo**, barra de progreso, resumen y lista de conflictos (ticket, dato, valor de este equipo y valor del archivo). Verificada con la vista real: textos, conteo de pendientes, lista de conflictos, mensajes de error en rojo y sin errores de enlace de datos. **Sin probar a mano:** los cuadros de Guardar y Abrir archivo y una exportación e importación completas con la app abierta.
  - *Elegir qué exportar (hecho):* la pestaña muestra los pendientes en una lista con casillas (todos vienen marcados) y filtros por fecha (desde y hasta) y por tipo (entradas, salidas, salidas manuales y anulados). **Exportar seleccionados** lleva solo lo marcado **y a la vista**; el resto sigue pendiente y conserva su casilla. **Exportar todo** incluye todos los pesajes del equipo. Verificado con la vista real y el ViewModel: filtros combinados, exportar 1 de 4, que lo marcado pero oculto por un filtro no se exporta, sin selección y exportar todo.
- [ ] Registro manual de salida en otro puesto.
- [ ] Copia local de usuarios para el login sin conexión.
- [ ] Indicador visible del estado de conexión y de los registros pendientes de sincronizar.
- [ ] Revisar la pestaña "Red" de Configuración (modos Local, Servidor y Cliente con puerto y URL): es anterior a este diseño y probablemente se reemplace por la configuración de sincronización con el servidor central.
- [ ] Ampliar `ExportImportService` y su `ExportPackage` (hoy exporta todo y la importación reemplaza todo).

## Multi-puesto (nuevo, implica cambios en el código)

Estado actual: las fotos se guardan en `%LocalAppData%\PesaControl\Photos` con ruta absoluta (`WeighingViewModel.cs:562`), por lo que otro puesto no las ve.

- [ ] Cambiar `PhotoPath` y `PhotoPathExit` a rutas **relativas** (p. ej. `2026/10/T-1234-entry.jpg`) con carpeta raíz configurable.
- [ ] Organizar las fotos por año y mes.
- [ ] Si la red falla, guardar la foto en local y subirla después.
- [ ] Quitar el `catch { }` vacío al guardar fotos: hoy un fallo pasa desapercibido.
- [ ] Separar la **configuración por puesto** (báscula, puerto serie, cámaras, impresora) de la configuración compartida.
- [x] Numeración de tickets sin duplicados entre puestos: resuelta con la serie por puesto (ver la sección local-first). *Antes, interino: contador atómico por mes (`TicketCounters`, `INSERT ... ON CONFLICT`); probado con 4 puestos simulados y 100 peticiones simultáneas. Si se pierde un número (p. ej. falla el guardado después de pedirlo) queda un hueco en la secuencia.*
- [ ] Control de concurrencia: dos puestos editando o cerrando el mismo ticket.
- [x] Qué hace un puesto sin conexión: **definido**, puede pesar con y sin conexión (ver la sección local-first).
- [ ] Registrar en la auditoría desde qué puesto se hizo cada acción.
- [ ] Respaldos en el servidor, de la base y de las fotos por separado.
- [ ] Política de retención de fotos.
- [ ] Pantalla de primer arranque con los modos "servidor + puesto" y "solo puesto" (dirección del servidor y carpeta de fotos).

## A. Cambios que pueden romper el proyecto actual

Hacer respaldo de la base de datos y trabajar en una rama de git antes de cada uno. Ordenados de mayor a menor riesgo.

### 1. Migrar a PostgreSQL con migraciones EF puras — **HECHO** (rama `feature/postgresql`, 2026-10-20)
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

### 6. Esquema de roles y usuarios — **parcial**
- Hecho: políticas de contraseña, usuario y nombre por rol; creación del Admin dentro de la app. El SuperAdmin sigue creándose con `Provisioner`.
- Pendiente: protección del último SuperAdmin e inyección de dependencias.
- Riesgo medio: hay que migrar los usuarios existentes. `AuthService` se crea con `new AuthService()`.
- Hacerlo junto con la inyección de dependencias.

## B. Cambios seguros

### Imprescindibles para vender
- [ ] **Pruebas automáticas** (hoy hay cero). Prioridad: `WeighingService`, protocolos de báscula (con tramas grabadas), `Argon2idPasswordHasher`, repositorios contra un PostgreSQL de prueba (no SQLite en memoria: se comporta distinto con fechas y concurrencia), `ExportImportService`. Incluir pruebas de integración de los flujos básicos (crear usuario, iniciar sesión, abrir el panel). Usar los fakes de Weight-Trak como modelo.
- [ ] **Auditoría**: registrar quién hizo qué (login, anulación o edición de pesajes, cambios de configuración y de usuarios).
- [ ] **Protección del último SuperAdmin**: no permitir eliminarlo ni degradarlo (`GuardaUltimoSuperAdmin` en Weight-Trak).
- [ ] **Pantalla de primer arranque** dentro de la app, que reemplace al proyecto `Provisioner`. *Avance: ya crea el Admin dentro de la app; falta el SuperAdmin y los modos servidor/puesto.*
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
- [ ] Control de contraseña reutilizable. *Avance: ya existe la confirmación con ojito en "Crear Admin" y en usuarios, pero está duplicada en XAML; extraerla a un control compartido.*

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
- [ ] **Verificación manual pendiente del aviso de cambios sin guardar** en Usuarios, Catálogos y Control (editor de dispositivos). Está implementado (`IUnsavedChanges` y `EditorTracker`) y probado con la lógica de Configuración, pero no con estas pantallas en la app real. Comprobar:
  - Abrir un formulario de usuario, escribir algo y pulsar otra sección del menú: debe preguntar.
  - Abrir un formulario de catálogo (producto, empresa, operador o conductor), escribir algo y cerrar la ventana: debe preguntar.
  - Abrir y cancelar un formulario **sin cambios**: no debe preguntar.
  - Control: lo mismo con el editor de dispositivos, barrera y semáforo.
  - "Guardar y continuar" con un formulario inválido (falta un dato obligatorio): debe quedarse en la pantalla.
  - Al cerrar la app con el aviso, comprobar en el Administrador de tareas que el proceso termina; si queda abierto, revisar el cierre de hilos de cámara y puerto serie.

## Orden de trabajo sugerido

1. ~~Migración a PostgreSQL y migración inicial limpia (A1).~~ Hecho.
2. **Siguiente:** operación con y sin conexión: (a) ~~código de puesto~~ y ~~almacén local con serie por puesto~~ (hechos), (b) resto de la configuración por puesto, (c) exportación e importación por archivo, (d) sincronización con el servidor, (e) registro manual de salida, (f) fotos compartidas y concurrencia.
3. Registro de errores, auditoría (con el puesto de origen) y protección del último SuperAdmin.
4. Primer arranque dentro de la app e instalador con los dos modos (servidor + puesto, solo puesto).
5. Pruebas sobre la lógica de pesaje (con PostgreSQL de prueba o contenedor, no SQLite en memoria).
6. Seguridad de la base (A2) y licencias (A3).
7. Inyección de dependencias gradual (A4/A6).

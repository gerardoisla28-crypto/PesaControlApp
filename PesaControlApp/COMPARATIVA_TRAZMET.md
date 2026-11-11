# Comparativa con Trazmet Scale Master y mejoras para PesaControl

Fecha: 2026-11-05. Fuentes: manual de usuario (v5.2/5.3) y brochure (v5.0.0) de Trazmet Scale Master, dos capturas de pantalla de ese software (Pesaje y Configuración › Indicador de peso) y el código actual de PesaControl (rama `feature/file-exchange`).

> Trazmet se evaluó solo por su documentación y capturas, no se instaló. Del lado de PesaControl, cada "falta" se comprobó buscando en el código; si una función no aparece ahí, se marca como ausente. Validar cada punto antes de ejecutarlo.
> No copiar de Trazmet textos, fotografías de camiones, nombres ni marca (Exactimet® está registrada). Las ideas y flujos sí sirven; los límites legales hay que tomarlos del texto del D.S. MOP 158/1980, no de su tabla (el brochure y el manual no coinciden del todo entre sí).

---

## 1. Resumen

**Trazmet es un producto de un solo puesto, centrado en Chile y en una idea: controlar el peso por eje y por conjunto de ejes** (lo que fiscaliza Vialidad), con una operación de tres pasos y un comprobante que sirve de prueba (calibración, sobrepeso declarado, folio, anulaciones con motivo).

**PesaControl ya es más fuerte en hardware y arquitectura:** varias básculas y protocolos (Sensocar, Sensoc, Modbus RTU/TCP, serie, autodetección), cámaras/DVR, lectura de placas, barrera y semáforo, tickets numerados por puesto, almacén local con sincronización prevista, intercambio de pesajes entre puestos por archivo, contraseñas Argon2id con política más exigente que la de Trazmet (14 caracteres para administradores frente a 8 con letras y números) y gráficos en Reportes. Trazmet no tiene nada de eso: es portátil, guarda en una carpeta local y no habla de varios puestos.

**Lo que le falta a PesaControl frente a Trazmet cae en tres grupos:**

1. **Control por eje / sobrepeso** (la razón de ser de Trazmet; en PesaControl no existe ni el concepto).
2. **Confianza del comprobante y del sistema:** calibración, anulación con motivo, auditoría, bloqueo por intentos, roles a medida, licencia, soporte.
3. **Claridad de la interfaz:** hoy el pesaje es un formulario con botones; Trazmet es un asistente guiado de tres pasos, con estado siempre visible y atajos de teclado.

---

## 2. Cuadro comparativo

| Área | Trazmet | PesaControl hoy | Brecha |
|---|---|---|---|
| Control por eje y conjunto (D.S. MOP 158) | Sí, núcleo del producto; 66 configuraciones de rodado | No hay ejes ni sobrepeso en el código | **Grande** |
| Flujo de pesaje | 3 pasos con indicador, 1.ª y 2.ª pesada, "En planta" con contador | Formulario único con "Marcar entrada/salida", lista "Pesajes del Día" | Media (UX) |
| Tipos de pesaje (Despacho, Recepción, Residuos…) | 5 tipos configurables, con regla propia de pesadas y ejes | No hay tipos; los campos de peso se llaman Entrada/Salida | Media |
| Tara guardada por vehículo | Sí ("Tomar tara", "Tara manual") | No hay maestro de vehículos ni tara guardada | Media |
| Maestros | Vehículos, choferes, transportistas, clientes, productos, lugares de entrega, grupos de eje, tipos de eje, tipos de pesaje, básculas | Productos, empresas, operadores, conductores | Media |
| Autocompletar por patente | Sí: ficha del camión, revisión técnica, peso máximo, chofer, transportista | La patente es solo texto (más lectura de placa por cámara) | Media |
| Peso manual | Con motivo obligatorio, marcado en el comprobante | Existe `IsExitWeightManual`; no se verificó el motivo | Pequeña, a revisar |
| Anular | Con motivo; el folio queda anulado, nunca se borra | `CancelWeighingAsync` cambia el estado sin pedir motivo ni usuario | **Media-alta** |
| Corregir un pesaje | Permiso aparte, queda con motivo, usuario y fecha | No se encontró; sin auditoría | Media-alta |
| Comprobante | Vista previa antes de imprimir; tara/bruto/neto; estado de calibración; declara el sobrepeso | Impresión directa (ESC/POS y Windows) | Media |
| Calibración y mantención | Estado y vencimiento en la cabecera; historial | No existe | Media (según el mercado) |
| Roles | 3 perfiles + roles a medida con permisos y niveles | 4 roles fijos | Media |
| Bloqueo por intentos / cambio obligatorio de clave | 5 intentos → 15 min; clave provisoria que se debe cambiar | Pendiente en tu plan; no hay código | Media |
| Auditoría | Bitácora de quién hizo qué | Pendiente en tu plan; no hay código | **Media-alta** |
| Licencia | Por instalación, con activación en línea o por teléfono y días de gracia | Pendiente (A3 de tu plan) | Pendiente para vender |
| Soporte | Pantalla de Soporte, mensaje de WhatsApp con ficha técnica, canal remoto cifrado | No hay pantalla de soporte | Media |
| Estado del indicador | Cabecera con semáforo: verde real, amarillo simulador, rojo desconectado | Texto "Conectado/Desconectado" dentro del pesaje | Pequeña |
| Informes | Totales por período agrupados por cliente, producto, transportista, chofer o tipo | Reportes con filtros, tarjetas y 4 gráficos; sin agrupar por chofer ni transportista | Pequeña |
| Importación del sistema anterior | Desde WeighMaster (Firebird) | Plan: importar catálogos desde Excel/CSV (opcional) | Distinta estrategia |
| Cámaras, DVR, barrera, semáforo | No | Sí | **Ventaja tuya** |
| Varias básculas y protocolos | Una activa a la vez | Varias, con protocolos | **Ventaja tuya** |
| Multi-puesto, local-first, intercambio por archivo | No | Sí (en construcción) | **Ventaja tuya** |
| Idioma y marca de la interfaz | Todo en español, marca coherente | Mezcla: "Industrial Weighing System", "Desarrollado por ARIS Tech · v1.0.0" en login y barra lateral | Pequeña, visible |

---

## 3. Listado de mejoras priorizado

Prioridad: **P1** = hacerlo antes de mostrarlo a clientes; **P2** = siguiente versión; **P3** = según el mercado.
Esfuerzo: S (días), M (1–2 semanas), L (más de 2 semanas).

### A. Interfaz y facilidad de uso (lo que más pidió, y lo más barato de ganar)

| # | Mejora | Qué copiar del enfoque de Trazmet | Prio | Esf. |
|---|---|---|---|---|
| UI-1 | **Rehacer la pantalla de Pesaje como asistente de 3 pasos** (Identificar vehículo → Tomar peso → Confirmar e imprimir), con un indicador arriba que muestre en qué paso va. | Un solo camino; el paso activo resaltado. | P1 | M |
| UI-2 | **Barra de acciones fija abajo** con las acciones del pesaje: Cancelar, Foto, Peso manual, Obtener peso, Procesar e imprimir. | Acciones siempre en el mismo lugar. Evitar su defecto: en las capturas esa barra tapa contenido (campos de tara, terminal); reservar espacio al desplazar. | P1 | S |
| UI-3 | **Atajos de teclado**: F5 obtener peso, F9 procesar, Esc cancelar, y mostrar la tecla dentro del botón. Hoy solo existe Esc en el login. | Operar todo sin mouse. | P1 | S |
| UI-4 | **Estado del peso en palabras**: bajo el número grande, "Esperando que el peso se estabilice" (ámbar) → "Peso estable, puede procesar" (verde). Bloquear "Procesar" hasta que esté estable. | Mensaje de estabilidad claro. | P1 | S |
| UI-5 | **Cabecera fija con chips de estado**: nombre de la báscula, estado del indicador (verde real / ámbar simulador / rojo desconectado), versión y usuario con su rol. | Semáforo explícito. Además: el modo simulador debe verse siempre en la cabecera, no solo en Configuración. | P1 | S |
| UI-6 | **Ficha del vehículo al escribir la patente**: tarjeta verde con datos (tipo, revisión técnica, peso máximo, chofer, transportista). Depende de MAE-1. | Menos escritura, menos errores. | P1 | M |
| UI-7 | **Panel "En planta"** como módulo propio: tarjetas de camiones esperando la segunda pesada (folio, cliente, hora de entrada, hace cuánto), con acciones "Registrar segunda pesada", "Cambiar patente" y "Sacar de planta (con motivo)", y un contador rojo en el menú. | Hoy es una lista "Pesajes del Día" sin tiempos ni acciones. | P1 | M |
| UI-8 | **Menú lateral con ícono y etiqueta fijos** para cada módulo (Pesaje, En planta, Historial, Informes, Datos, Configuración, Soporte, Ayuda). Renombrar: Registros → Historial, Catálogos → Datos. | Menos módulos "de segunda" (hoy Control, Cámaras y Reportes compiten con el pesaje). | P2 | S |
| UI-9 | **Vista previa del comprobante** antes de imprimir ("Confirmar e imprimir"), para no gastar papel. | Paso 3 del asistente. | P2 | M |
| UI-10 | **Configuración por tarjetas** que llevan a páginas con "‹ Configuración" para volver, en vez de pestañas apretadas. Cada página con su botón "Aplicar configuración" y aviso de cambios sin guardar (ya lo tienes en otras vistas). | Más ordenado a medida que crezca (puesto, sincronización, comprobante, calibración, licencia). | P2 | M |
| UI-11 | **Pantalla de indicador con "lectura en vivo"**: peso interpretado, estado y **terminal con la trama cruda**, más un asistente de lectura para elegir el formato. Aprovecha tus protocolos y la autodetección. | Reduce llamadas de soporte en la puesta en marcha. | P2 | M |
| UI-12 | **Aviso visible de modo demo/simulador**: banner en Configuración y marca "DEMOSTRACIÓN" en los comprobantes emitidos con peso simulado, para que no se confundan con un pesaje real. | Tu `SimulatorProtocol` y la demo ya existen; falta señalizarlos. | P1 | S |
| UI-13 | **Unificar idioma y marca**: quitar "Industrial Weighing System" y "ARIS Tech · v1.0.0" de login y barra lateral; usar una sola versión y un solo nombre. | Marca coherente. | P1 | S |
| UI-14 | **Ayuda y Soporte**: pantalla con ficha técnica del equipo (versión, báscula, indicador), botón que arma un mensaje de WhatsApp/correo ya escrito, y ayuda breve por módulo. | Menos tiempo explicando por teléfono. | P2 | M |
| UI-15 | **Formularios con errores bajo cada campo y campos obligatorios con \***: ya lo hiciste en login y usuarios; extender a Catálogos y Pesaje (hoy el pesaje avisa solo en una línea de estado). | Consistencia. | P2 | S |
| UI-16 | **Mensajes de error con causa y qué hacer** ("no se pudo conectar: revise puerto, cable y velocidad"). Ya hay algunos; quitar los `catch` vacíos que callan el error. | Soporte más rápido. | P2 | S |

### B. Control por eje y sobrepeso (el diferenciador de Trazmet)

Decisión previa: tu plan lo marca como "solo si se vende en Chile". Tus datos de demo ya son chilenos, así que conviene decidirlo ahora. **Depende del tipo de báscula**: con una romana completa solo se puede validar el peso total y el peso máximo del vehículo; el control real por conjunto exige báscula por conjuntos o por ejes (o pesaje dinámico).

| # | Mejora | Prio | Esf. |
|---|---|---|---|
| EJE-1 | **Primer paso mínimo, útil con cualquier báscula:** peso máximo legal por vehículo y alerta de sobrepeso total al pesar, con el exceso en kg. | P1 | S |
| EJE-2 | Tabla de **grupos de eje** (simple, doble, tándem, trídem…) con límite y tolerancia editables, cargada desde la norma. | P2 | M |
| EJE-3 | **Configuración de rodado** por vehículo (catálogo de disposiciones) y dibujo del camión con la sigla de cada eje y su máximo, generado por el sistema desde esos datos. Dibujo esquemático propio, no fotografías de Trazmet. | P2 | L |
| EJE-4 | **Captura por conjunto/eje** según el tipo de romana (completa, por conjuntos, por ejes; estático o dinámico), resaltando el grupo que está en la plataforma. | P3 | L |
| EJE-5 | **Emitir el comprobante aunque esté fuera de norma**, indicando qué grupo y por cuánto, y registrando el hecho (no maquillarlo). | P2 | S |
| EJE-6 | Activar el control por tipo de pesaje y por pasada (en despacho entra vacío y sale cargado; en recepción al revés). | P2 | S |

### C. Datos maestros y pesaje

| # | Mejora | Prio | Esf. |
|---|---|---|---|
| MAE-1 | **Maestro de vehículos**: patente, patente del acoplado, tipo, tara, peso máximo, revisión técnica, transportista y chofer habitual. Es la base de UI-6 y de EJE-1. | P1 | M |
| MAE-2 | **Maestros faltantes**: transportistas y lugares de entrega (tu plan ya los menciona). | P2 | M |
| MAE-3 | **Tara guardada por vehículo**: "Tomar tara" (pesar vacío) y "Tara manual"; usarla en una pesada única. | P2 | M |
| MAE-4 | **Tipos de pesaje configurables** (Despacho, Recepción, Residuos, Comercial, Pruebas): pesada única o ciclo de dos pesadas, y si controla ejes. Cambia el significado de los campos para que se hable de *tara / bruto / neto* y no de *entrada / salida*. Hoy la lista muestra "Peso Entrada" = bruto y "Peso Salida" = tara, lo que confunde en una recepción. | P2 | L |
| MAE-5 | **Validación de RUT** (dígito verificador) y de patente chilena en maestros y pesaje. Verificar si ya existe; la búsqueda no la encontró. | P2 | S |
| MAE-6 | **Desactivar en vez de eliminar** (casilla Activo ya existe), y que los registros inactivos no se sugieran al pesar. | P2 | S |
| MAE-7 | **Importar catálogos desde Excel/CSV con plantilla** (ya en tu plan). Es tu respuesta al "importar WeighMaster" de Trazmet, sin leer bases ajenas. | P3 | M |

### D. Confianza, trazabilidad y seguridad

| # | Mejora | Prio | Esf. |
|---|---|---|---|
| CON-1 | **Anular con motivo obligatorio**, quién y cuándo. El folio se conserva anulado (ya no se borra). Hoy `CancelWeighingAsync` no pide motivo. | **P1** | S |
| CON-2 | **Corregir un pesaje** con permiso propio, dejando motivo, usuario y fecha. | P1 | M |
| CON-3 | **Auditoría** (ya en tu plan): login, anulaciones, correcciones, cambios de configuración y usuarios, con el puesto de origen. Es la base de CON-1 y CON-2. | P1 | M |
| CON-4 | **Peso manual con motivo** (indicador sin comunicación, averiado, corte de energía, romana auxiliar, otro) y marca visible en el comprobante. | P1 | S |
| CON-5 | **Bloqueo por intentos fallidos** (p. ej. 5 intentos → 15 min por usuario) y aviso de contactar al administrador. | P2 | S |
| CON-6 | **Clave provisoria**: la que fija el administrador obliga a crear una propia al primer ingreso. Cerrar sesión tras un tiempo sin actividad. | P2 | M |
| CON-7 | **Protección del último SuperAdmin** (ya en tu plan). | P1 | S |
| CON-8 | **Roles a medida con permisos** (pesar, peso manual, corregir, anular, maestros, mantención, configuración, usuarios) y regla de jerarquía. Hoy hay 4 roles fijos; con CON-2 y CON-1 aparecen permisos que conviene separar. | P2 | L |
| CON-9 | **Calibración y mantención**: estado y vencimiento en la cabecera (con días restantes), historial, y el estado impreso en el comprobante. Una calibración vencida avisa, pero no impide pesar. Solo si tu cliente es metrológico. | P3 | M |
| CON-10 | **Licencia** (A3 de tu plan): activación con y sin internet, días de gracia y bloqueo solo de pesajes nuevos, dejando el historial siempre consultable. | P2 | L |

### E. Informes y comprobante

| # | Mejora | Prio | Esf. |
|---|---|---|---|
| INF-1 | **Totales por período agrupados** por cliente, producto, transportista, chofer o tipo de pesaje, con exportar e imprimir. | P2 | M |
| INF-2 | **Comprobante más probatorio**: folio, fecha, patente, chofer, transportista, cliente, guía, tara/bruto/neto, operador, marca de peso manual y de modo demo, y estado de calibración. Opción de mostrar o no casillas de firma. | P2 | M |
| INF-3 | **Impresión carta o rollo térmico 58/80 mm** a elección en Configuración (hoy tienes ESC/POS y Windows; unificar la elección). | P3 | S |
| INF-4 | Reportes por folio y fechas, y exportación a Word (ya en tu plan). | P3 | M |

### F. Cosas que Trazmet hace y **no** conviene copiar

- **Soporte remoto con control de la pantalla del operador** (anexo B): es mucha superficie de seguridad (cifrado, firmas, relé). Para empezar bastan UI-14 (ficha técnica + WhatsApp) y una herramienta de acceso remoto estándar que el cliente encienda.
- **Servidor web local en 127.0.0.1 con validación de Host y token**: es su decisión de arquitectura; la tuya (app WPF + SQLite/PostgreSQL) no lo necesita.
- **Una sola báscula activa**: tu soporte de varias básculas es mejor.
- **Pestañas de Configuración con el botón "Aplicar" tapando contenido** (se ve en la segunda captura): copiar la idea, no el defecto.

---

## 4. Orden de trabajo sugerido

1. **Primera ronda, la que cambia la primera impresión (todo P1 de UI, S/M):** UI-13 (marca e idioma), UI-5 y UI-12 (cabecera y aviso de demo), UI-3 y UI-4 (atajos y estado del peso), UI-1 y UI-2 (asistente de 3 pasos con barra de acciones).
2. **Trazabilidad mínima para vender (P1):** CON-3 auditoría → CON-1 anular con motivo → CON-4 peso manual con motivo → CON-7 último SuperAdmin → CON-2 correcciones.
3. **Maestro de vehículos (MAE-1)** junto con UI-6 (ficha al escribir la patente) y EJE-1 (peso máximo y alerta de sobrepeso). Con esto ya tienes una versión "light" del diferenciador de Trazmet con cualquier báscula.
4. **UI-7 "En planta"** y vista previa del comprobante (UI-9).
5. **Decidir si se vende en Chile con control por eje completo** (EJE-2 a EJE-6). Es la inversión más grande; conviene validarla con un cliente antes.
6. Resto de P2/P3 según lo que pidan los primeros clientes.

Nota de alcance: todo esto se suma a lo que ya está en `PLAN_MEJORAS.md` (sincronización, instalador, pruebas, registro de errores). Los puntos CON-3, CON-7, CON-10, MAE-2 y MAE-7 ya estaban allí; aquí quedan ordenados frente a la competencia.

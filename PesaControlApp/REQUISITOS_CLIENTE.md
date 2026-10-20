# Requisitos del cliente para instalar PesaControl

Lo que el cliente debe tener antes de la instalación. PesaControl pone el software (instalador, base de datos y respaldos); el cliente pone el equipo, la red y la energía.

> Valores mínimos de referencia, a validar con pruebas reales antes de entregarlos a un cliente.

## 1. Modos de instalación

| Modo | Para qué | Qué instala |
|---|---|---|
| **Servidor + puesto** | Cliente con una sola báscula, o el PC principal en una instalación con varios puestos | PostgreSQL, carpeta de fotos y la app de pesaje |
| **Solo puesto** | Cada báscula adicional | La app de pesaje, conectada al servidor |

Un cliente con una báscula usa solo el modo "servidor + puesto". Si más adelante compra otra báscula, agrega un "solo puesto" que apunta a ese PC.

## 2. Equipo servidor (PC que aloja la base de datos y las fotos)

- Windows 10/11 Pro de 64 bits (o Windows Server).
- 8 GB de RAM como mínimo.
- Procesador de 4 núcleos o más.
- Disco SSD, con espacio libre para las fotos (calcular según pesajes diarios y años de retención).
- **Encendido permanente** mientras haya operación. Si se apaga, ningún puesto puede pesar.
- **IP fija** en la red local (o reserva en el router).
- UPS recomendado, para evitar corrupción de datos por cortes de energía.
- Sin cierres automáticos de sesión ni suspensión del equipo.
- Puede ser el mismo PC de un puesto de pesaje.

## 3. Equipo de cada puesto de pesaje

- Windows 10/11 de 64 bits.
- 8 GB de RAM como mínimo (16 GB si usa varias cámaras o DVR).
- Puerto serie físico, adaptador USB-serie o conexión de red hacia la báscula, según el indicador.
- Pantalla de resolución suficiente para la app (a definir).
- Impresora de tickets (térmica ESC/POS o impresora Windows), si aplica.

## 4. Red

- Todos los puestos y el servidor en la misma red local (LAN) cableada. Se desaconseja Wi-Fi para los puestos de pesaje.
- El servidor accesible por IP fija desde cada puesto.
- Puertos abiertos en el firewall del servidor: PostgreSQL (5432 por defecto) y compartición de archivos (SMB) para las fotos.
- Internet solo si se usa reconocimiento de placas con PlateRecognizer (servicio en la nube), actualizaciones o soporte remoto.

## 5. Dispositivos de campo

- **Báscula e indicador**: modelo y protocolo conocidos (Sensocar A12, Sensoc, Modbus RTU/TCP u otro soportado). Parámetros de comunicación (puerto, velocidad) a confirmar en la puesta en marcha.
- **Cámaras**: USB, RTSP o DVR Dahua. Credenciales y direcciones IP a cargo del cliente.
- **Barrera y semáforo** (opcional): módulo Modbus o placa Arduino compatible.

## 6. Respaldos

- Disco o carpeta de respaldo **distinta** de la que contiene la base de datos (disco externo, otro PC o unidad de red).
- Cada cliente define cuántos respaldos conservar. Los respaldos de la base y de las fotos se hacen en el servidor.

## 7. Licencia y accesos

- El cliente informa cuántos puestos va a usar, para dimensionar la licencia.
- Una persona del cliente con permisos de administrador de Windows durante la instalación.
- Usuario administrador inicial de PesaControl definido en el primer arranque.

## 8. Qué entrega PesaControl

- Instalador con ambos modos y conexión configurable en el primer arranque.
- PostgreSQL y carpeta de fotos preparados por el instalador.
- Respaldos automáticos con rotación.
- Manual de instalación y soporte.

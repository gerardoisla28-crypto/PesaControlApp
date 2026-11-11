/*
 * PesaControl - Arduino Access Control
 * Controla barrera (servo) y semáforo (2 relés) via serial.
 * Protocolo ASCII a 9600 baud:
 *   OPEN / CLOSE / STATUS  → barrera
 *   RED / GRN / OFF / YEL / COLOR → semáforo
 *   CAPS → el equipo dice qué tiene instalado: CAPS;BARRIER=1;LIGHT=1
 *         (PesaControl lo usa para comprobar que hay un equipo y mostrar solo
 *          las salidas que existen)
 *
 * Conexiones:
 *   D3 → IN1 Relay → Luz ROJA (220V)
 *   D4 → IN2 Relay → Luz VERDE (220V)
 *   D5 → Servo motor (señal naranja)
 *   Relays activos en LOW (LOW = enciende, HIGH = apaga)
 */

#include <Servo.h>

const int PIN_RED   = 3;    // D3 → relay rojo
const int PIN_GREEN = 4;    // D4 → relay verde
const int PIN_SERVO = 5;    // D5 → servo (naranja)

// ── Qué salidas tiene instaladas ESTE equipo ──
// Poner en false la que no esté conectada. Así PesaControl solo muestra la activa.
const bool HAS_BARRIER = true;   // servo de la barrera
const bool HAS_LIGHT   = true;   // semáforo (2 relés)

const int ANGLE_OPEN  = 180;
const int ANGLE_CLOSE = 0;

Servo barrierServo;
String currentBarrierStatus = "CLOSE";
String currentColor = "OFF";

void setup() {
    if (HAS_LIGHT) {
        pinMode(PIN_RED, OUTPUT);
        pinMode(PIN_GREEN, OUTPUT);

        // Apagar relays (HIGH = OFF para relays activos en LOW)
        digitalWrite(PIN_RED, HIGH);
        digitalWrite(PIN_GREEN, HIGH);
    }

    if (HAS_BARRIER) {
        barrierServo.attach(PIN_SERVO);
        barrierServo.write(ANGLE_CLOSE);
    }

    Serial.begin(9600);
}

void loop() {
    if (Serial.available() == 0) return;

String cmd = "";
while (Serial.available()) {
    char c = Serial.read();
    if (c == '\n' || c == '\r') break;
    cmd += c;
}
    cmd.toUpperCase();

    // ── Identificación: qué salidas hay instaladas ──
    if (cmd == "CAPS") {
        Serial.print("CAPS;BARRIER=");
        Serial.print(HAS_BARRIER ? 1 : 0);
        Serial.print(";LIGHT=");
        Serial.println(HAS_LIGHT ? 1 : 0);
    }
    // ── Barrera ──
    else if (HAS_BARRIER && cmd == "OPEN") {
        barrierServo.write(ANGLE_OPEN);
        currentBarrierStatus = "OPEN";
    }
    else if (HAS_BARRIER && cmd == "CLOSE") {
        barrierServo.write(ANGLE_CLOSE);
        currentBarrierStatus = "CLOSE";
    }
    else if (HAS_BARRIER && cmd == "STATUS") {
        Serial.println(currentBarrierStatus);
    }
    // ── Semáforo ──
    else if (HAS_LIGHT && cmd == "RED") {
        digitalWrite(PIN_GREEN, HIGH);   // verde OFF
        digitalWrite(PIN_RED, LOW);      // rojo ON (LOW activa relay)
        currentColor = "RED";
    }
    else if (HAS_LIGHT && cmd == "GRN") {
        digitalWrite(PIN_RED, HIGH);     // rojo OFF
        digitalWrite(PIN_GREEN, LOW);    // verde ON (LOW activa relay)
        currentColor = "GRN";
    }
    else if (HAS_LIGHT && (cmd == "OFF" || cmd == "YEL")) {
        digitalWrite(PIN_RED, HIGH);     // rojo OFF
        digitalWrite(PIN_GREEN, HIGH);   // verde OFF
        currentColor = "OFF";
    }
    else if (HAS_LIGHT && cmd == "COLOR") {
        Serial.println(currentColor);
    }
}

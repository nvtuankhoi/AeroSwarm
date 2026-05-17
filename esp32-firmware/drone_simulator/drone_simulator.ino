/*
 * AeroSwarm — ESP32-C3 MAVLink v2 Drone Simulator
 *
 * Flash this sketch onto each ESP32. Change DRONE_ID (1–5),
 * WIFI_SSID, WIFI_PASS, and SERVER_IP before flashing.
 *
 * Sends 3 MAVLink v2 messages to the AeroSwarm backend:
 *   MSG 0   HEARTBEAT          — every 1 000 ms
 *   MSG 33  GLOBAL_POSITION_INT — every   200 ms
 *   MSG 147 BATTERY_STATUS      — every 5 000 ms
 */

#include <WiFi.h>
#include <WiFiUdp.h>

// ─── CONFIGURE BEFORE FLASHING ───────────────────────────────────────
#define DRONE_ID    1                   // 1 – 5  (must be unique per board)
#define WIFI_SSID   "YourSSID"
#define WIFI_PASS   "YourPassword"
#define SERVER_IP   "192.168.1.100"     // IP of the PC running AeroSwarm.Api
#define SERVER_PORT 14550
// ─────────────────────────────────────────────────────────────────────

WiFiUDP udp;

// Base GPS positions near Ho Chi Minh City (one per drone slot)
const double BASE_LAT[6] = { 0.0, 10.7769, 10.7780, 10.7790, 10.7760, 10.7750 };
const double BASE_LON[6] = { 0.0, 106.7009, 106.7020, 106.7030, 106.6999, 106.6990 };

// Simulation state
static float  orbitAngle     = (DRONE_ID - 1) * 72.0f;  // stagger starting angles
static float  altitude       = 10.0f + DRONE_ID * 2.0f;
static float  batteryVoltage = 12.6f;
static int    batteryPercent = 100;
static bool   isArmed        = true;
static uint8_t mavSeq        = 0;

// ── CRC-16/MCRF4XX (MAVLink standard) ────────────────────────────────
static uint16_t crc16(const uint8_t* buf, uint16_t len) {
    uint16_t crc = 0xFFFF;
    while (len--) {
        uint8_t tmp = *buf++ ^ (uint8_t)(crc & 0xFF);
        tmp ^= (tmp << 4);
        crc = (crc >> 8) ^ ((uint16_t)tmp << 8) ^ ((uint16_t)tmp << 3) ^ ((uint16_t)tmp >> 4);
    }
    return crc;
}

static uint16_t crcAccum(uint16_t crc, uint8_t b) {
    uint8_t tmp = b ^ (uint8_t)(crc & 0xFF);
    tmp ^= (tmp << 4);
    return (crc >> 8) ^ ((uint16_t)tmp << 8) ^ ((uint16_t)tmp << 3) ^ ((uint16_t)tmp >> 4);
}

// Build a complete MAVLink v2 frame into `out[]`.
// Returns total byte count (header 10 + payload + CRC 2).
static int buildFrame(uint8_t* out, uint32_t msgId,
                      const uint8_t* payload, uint8_t payloadLen,
                      uint8_t crcExtra) {
    out[0] = 0xFD;          // MAVLink v2 magic
    out[1] = payloadLen;
    out[2] = 0;             // incompat_flags
    out[3] = 0;             // compat_flags
    out[4] = mavSeq++;
    out[5] = DRONE_ID;      // system id (1–5 → drone id on backend)
    out[6] = 1;             // component id (autopilot)
    out[7] = (uint8_t)(msgId & 0xFF);
    out[8] = (uint8_t)((msgId >> 8) & 0xFF);
    out[9] = (uint8_t)((msgId >> 16) & 0xFF);
    memcpy(out + 10, payload, payloadLen);

    // CRC covers bytes[1..9] + payload + crcExtra seed
    uint16_t crc = crc16(out + 1, 9 + payloadLen);
    crc = crcAccum(crc, crcExtra);

    out[10 + payloadLen]     = (uint8_t)(crc & 0xFF);
    out[10 + payloadLen + 1] = (uint8_t)(crc >> 8);
    return 12 + payloadLen;
}

static void sendPacket(uint8_t* buf, int len) {
    udp.beginPacket(SERVER_IP, SERVER_PORT);
    udp.write(buf, len);
    udp.endPacket();
}

// ── Message senders ───────────────────────────────────────────────────

// MSG 0 — HEARTBEAT  (9-byte payload, CRC_EXTRA = 50)
static void sendHeartbeat() {
    uint8_t payload[9] = {};
    uint32_t customMode = 4;  // GUIDED
    memcpy(payload + 0, &customMode, 4);
    payload[4] = 2;   // MAV_TYPE_QUADROTOR
    payload[5] = 3;   // MAV_AUTOPILOT_ARDUPILOTMEGA
    payload[6] = isArmed ? 0x81 : 0x01;  // base_mode; bit7 = armed
    payload[7] = 0;   // system status
    payload[8] = 3;   // MAVLink version

    uint8_t frame[32];
    int len = buildFrame(frame, 0, payload, 9, 50);
    sendPacket(frame, len);
    Serial.printf("[Drone #%d] HEARTBEAT — %s\n", DRONE_ID, isArmed ? "ARMED" : "DISARMED");
}

// MSG 33 — GLOBAL_POSITION_INT  (28-byte payload, CRC_EXTRA = 104)
//
// Wire layout (MAVLink v2 spec):
//  [0- 3] time_boot_ms  uint32  ms
//  [4- 7] lat           int32   degE7
//  [8-11] lon           int32   degE7
//  [12-15] alt          int32   mm  (MSL)
//  [16-19] relative_alt int32   mm
//  [20-21] vx           int16   cm/s
//  [22-23] vy           int16   cm/s
//  [24-25] vz           int16   cm/s
//  [26-27] hdg          uint16  cdeg  0-35999
static void sendGlobalPositionInt(double lat, double lon, float alt, float spd, float hdgDeg) {
    uint8_t payload[28] = {};

    uint32_t timeBootMs = millis();
    int32_t  latE7      = (int32_t)(lat  * 1e7);
    int32_t  lonE7      = (int32_t)(lon  * 1e7);
    int32_t  altMm      = (int32_t)(alt  * 1000.0f);
    int32_t  relAltMm   = (int32_t)(alt  * 1000.0f);
    int16_t  vx         = (int16_t)(spd * 100.0f * cos(hdgDeg * DEG_TO_RAD));
    int16_t  vy         = (int16_t)(spd * 100.0f * sin(hdgDeg * DEG_TO_RAD));
    int16_t  vz         = 0;
    uint16_t hdg        = (uint16_t)(hdgDeg * 100.0f);

    memcpy(payload + 0,  &timeBootMs, 4);
    memcpy(payload + 4,  &latE7,      4);
    memcpy(payload + 8,  &lonE7,      4);
    memcpy(payload + 12, &altMm,      4);
    memcpy(payload + 16, &relAltMm,   4);
    memcpy(payload + 20, &vx,         2);
    memcpy(payload + 22, &vy,         2);
    memcpy(payload + 24, &vz,         2);
    memcpy(payload + 26, &hdg,        2);

    uint8_t frame[50];
    int len = buildFrame(frame, 33, payload, 28, 104);
    sendPacket(frame, len);
}

// MSG 147 — BATTERY_STATUS  (36-byte payload, CRC_EXTRA = 154)
//
// Wire layout (relevant fields only):
//  [10-11] voltage_battery  uint16  mV
//  [33]    battery_remaining int8   percent  (-1 = unknown)
static void sendBatteryStatus() {
    uint8_t payload[36] = {};
    uint16_t voltMv = (uint16_t)(batteryVoltage * 1000.0f);
    memcpy(payload + 10, &voltMv, 2);
    payload[33] = (uint8_t)batteryPercent;

    uint8_t frame[60];
    int len = buildFrame(frame, 147, payload, 36, 154);
    sendPacket(frame, len);
    Serial.printf("[Drone #%d] BATTERY %.2fV  %d%%\n", DRONE_ID, batteryVoltage, batteryPercent);
}

// ── Arduino lifecycle ─────────────────────────────────────────────────

void setup() {
    Serial.begin(115200);
    delay(500);
    Serial.printf("\n=== AeroSwarm Drone Simulator #%d ===\n", DRONE_ID);

    WiFi.begin(WIFI_SSID, WIFI_PASS);
    Serial.print("Connecting to WiFi");
    while (WiFi.status() != WL_CONNECTED) {
        delay(500);
        Serial.print(".");
    }
    Serial.printf("\nConnected! IP: %s\n", WiFi.localIP().toString().c_str());
    Serial.printf("Sending MAVLink to %s:%d\n", SERVER_IP, SERVER_PORT);

    udp.begin(14550);
}

static unsigned long lastHeartbeat = 0;
static unsigned long lastPosition  = 0;
static unsigned long lastBattery   = 0;

void loop() {
    unsigned long now = millis();

    // HEARTBEAT — 1 Hz
    if (now - lastHeartbeat >= 1000) {
        lastHeartbeat = now;
        sendHeartbeat();
    }

    // GLOBAL_POSITION_INT — 5 Hz
    if (now - lastPosition >= 200) {
        lastPosition = now;

        // Orbit in a small circle (~50 m radius), 2.5 deg/s
        orbitAngle += 0.5f;
        if (orbitAngle >= 360.0f) orbitAngle -= 360.0f;

        float heading = orbitAngle;
        double lat = BASE_LAT[DRONE_ID] + 0.00045 * sin(orbitAngle * DEG_TO_RAD);
        double lon = BASE_LON[DRONE_ID] + 0.00045 * cos(orbitAngle * DEG_TO_RAD);

        // Gentle altitude oscillation ±3 m
        altitude = (10.0f + DRONE_ID * 2.0f) + 3.0f * sin(orbitAngle * 2.0f * DEG_TO_RAD);

        sendGlobalPositionInt(lat, lon, altitude, 2.5f, heading);
    }

    // BATTERY_STATUS — every 5 s (drain slowly)
    if (now - lastBattery >= 5000) {
        lastBattery = now;
        batteryVoltage -= 0.003f;
        if (batteryVoltage < 10.5f) batteryVoltage = 12.6f;  // reset for sim
        batteryPercent = constrain(
            (int)((batteryVoltage - 10.5f) / (12.6f - 10.5f) * 100.0f), 0, 100);
        sendBatteryStatus();
    }
}

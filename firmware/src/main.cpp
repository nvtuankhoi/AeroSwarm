// AeroSwarm — ESP32-C3 Mock Drone Firmware
//
// Architecture:
//   - WiFi via WiFiManager captive portal (first boot AP "AeroSwarm-Setup-N")
//   - UDP MAVLink v2 RX + TX on port 14550
//   - FSM: BOOT → IDLE → ARMED → TAKEOFF → FLYING → RTL → LANDING → ERROR
//   - Virtual GPS drifts toward target at 5 m/s horizontal / 1 m/s vertical
//   - Battery: 1S Li-Po, 100k/100k divider on GPIO3, EWMA + LUT %
//   - Failsafe: GCS heartbeat watchdog (3s) + low-batt latching + WiFi reconnect
//   - Peripherals (DEMO_DRONE only): RGB LED, buzzer, 4-motor common PWM
//   - OTA via ArduinoOTA (password "aeroswarm") over WiFi
//   - Geofence 200m from home
//   - Mission upload protocol with NVS persistence

#include <Arduino.h>
#include <WiFi.h>
#include <WiFiUdp.h>
#include <ArduinoOTA.h>
#include <Preferences.h>
#include <WiFiManager.h>
#include <esp_wifi.h>
#include <math.h>

#include "config.h"
#include "mavlink.h"
#include "peripherals.h"

// Optional: local override of captive portal with hardcoded creds for testing.
// File is gitignored. Define WIFI_SSID + WIFI_PASS to skip WiFiManager.
#if __has_include("secrets_local.h")
#include "secrets_local.h"
#endif

using peripherals::FsmState;
using peripherals::BattWarn;
using peripherals::BuzzerPattern;

// ── Globals ───────────────────────────────────────────────────────────
WiFiUDP   g_udp;
Preferences g_pref;
WiFiManager g_wm;

uint8_t   g_sysId = DEFAULT_SYSID;
uint16_t  g_udpPort = BASE_UDP_PORT;
FsmState  g_state = FsmState::BOOT;
static uint8_t g_currentMotorThrottle = 0;
static uint8_t g_targetMotorThrottle = 0;
static uint32_t g_flightStartMs = 0;

// Virtual GPS state
double g_lat = 0.0, g_lon = 0.0;
float  g_alt = 0.0f;
float  g_targetAlt = 0.0f;
double g_targetLat = 0.0, g_targetLon = 0.0;
bool   g_hasTarget = false;
float  g_vx = 0, g_vy = 0, g_vz = 0;
float  g_heading = 0.0f;

// SITL sync state (when paired with a SITL "brain" drone)
static bool   g_syncActive = false;
static bool   g_syncArmed  = false;
static float  g_syncAlt    = 0.0f;
static FsmState g_syncState = FsmState::IDLE;
static uint32_t g_lastSyncMs = 0;

// Home (set via MAV_CMD_DO_SET_HOME)
double g_homeLat = DEFAULT_HOME_LAT;
double g_homeLon = DEFAULT_HOME_LON;
float  g_homeAlt = DEFAULT_HOME_ALT;
bool   g_homeSet = false;

// Battery
float g_battV = 4.20f;
int8_t g_battPct = 100;
BattWarn g_battWarn = BattWarn::NORMAL;
uint32_t g_battCritSince = 0;

// GCS heartbeat watchdog
uint32_t g_lastGcsHb = 0;
bool g_gcsSeen = false;
IPAddress g_gcsIp;     // discovered from first GCS heartbeat

// Periodic timers
uint32_t g_lastFsmTick = 0;
uint32_t g_lastGpsTick = 0;
uint32_t g_lastHbTx    = 0;
uint32_t g_lastPosTx   = 0;
uint32_t g_lastBattTx  = 0;
uint32_t g_lastSysTx   = 0;

// Mission (NVS-persisted)
struct WP { double lat; double lon; float alt; uint16_t cmd; };
constexpr int MAX_WP = 16;
WP   g_mission[MAX_WP];
int  g_missionCount = 0;
int  g_missionSeq   = 0;
int  g_missionUploadPending = 0;
uint16_t g_missionExpectedSeq = 0;
uint8_t  g_missionUploadSrcSys = 0;

// ── Forward decls ─────────────────────────────────────────────────────
static void connectWifi();
static void setupOta();
static void onHeartbeat(const mavlink::Decoded& m, IPAddress src);
static void onCommandLong(const mavlink::Decoded& m, IPAddress src);
static void onSetMode(const mavlink::Decoded& m);
static void onMissionCount(const mavlink::Decoded& m, IPAddress src);
static void onMissionItemInt(const mavlink::Decoded& m, IPAddress src);
static void onMissionClearAll();
static void onNamedValueFloat(const mavlink::Decoded& m);
static void onGoto(const mavlink::Decoded& m);
static void changeState(FsmState s, const char* reason);
static void txHeartbeat();
static void txGlobalPosition();
static void txBatteryStatus();
static void txStatusText(uint8_t severity, const char* text);
static void txCommandAck(uint16_t cmd, uint8_t result);
static void txMissionAck(uint8_t targetSys, uint8_t result);
static void txMissionRequestInt(uint8_t targetSys, uint16_t seq);
static void sendUdp(const uint8_t* data, int len, IPAddress dst = IPAddress(0,0,0,0));
static void fsmTick();
static void gpsTick(float dt);
static void batteryTick();
static float battVoltsToPercent(float v);
static double haversine(double lat1, double lon1, double lat2, double lon2);
static void loadMissionFromNvs();
static void saveMissionToNvs();
static void clearMissionNvs();

// ── Setup ─────────────────────────────────────────────────────────────
void setup() {
    Serial.begin(115200);
    delay(200);
    Serial.printf("\n=== AeroSwarm Drone Firmware (SysID %d) ===\n", DEFAULT_SYSID);

    peripherals::init();

    // SysID: build flag wins over NVS to prevent stale values when
    // re-flashing a different env (e.g. demo_sysid1 → demo_sysid3).
    g_pref.begin(NVS_NS_DRONE, false);
    uint8_t nvsSysId = g_pref.getUChar("sysid", DEFAULT_SYSID);
    if (nvsSysId != DEFAULT_SYSID) {
        Serial.printf("[NVS] overriding stored sysid %d -> build sysid %d\n", nvsSysId, DEFAULT_SYSID);
        g_sysId = DEFAULT_SYSID;
        g_pref.putUChar("sysid", g_sysId);
    } else {
        g_sysId = nvsSysId;
    }
    Serial.printf("[NVS] sysid=%d\n", g_sysId);
    g_pref.end();

    loadMissionFromNvs();

    // Initialize virtual GPS near HCMC with per-drone offset so swarm
    // drones don't stack at (0,0). 0.0001 deg ≈ 11 m apart.
    g_lat = DEFAULT_HOME_LAT + (g_sysId - 1) * 0.0001;
    g_lon = DEFAULT_HOME_LON + (g_sysId - 1) * 0.0001;
    g_homeLat = g_lat;
    g_homeLon = g_lon;
    g_homeAlt = DEFAULT_HOME_ALT;
    g_homeSet = true;

    connectWifi();
    setupOta();

    g_udpPort = BASE_UDP_PORT + (g_sysId - 1) * 10;
    g_udp.begin(g_udpPort);
    Serial.printf("[UDP] listening on %d (sysid=%d)\n", g_udpPort, g_sysId);

    changeState(FsmState::IDLE, "boot complete");
    peripherals::buzzerPlay(BuzzerPattern::BOOT_DONE);
}

// ── Loop ──────────────────────────────────────────────────────────────
void loop() {
    ArduinoOTA.handle();

    // RX
    int sz = g_udp.parsePacket();
    if (sz > 0) {
        static uint8_t buf[300];
        int n = g_udp.read(buf, sizeof(buf));
        IPAddress src = g_udp.remoteIP();
        mavlink::Decoded msg;
        if (mavlink::decode(buf, n, msg)) {
            switch (msg.msgId) {
                case mavlink::MSG_HEARTBEAT:        onHeartbeat(msg, src); break;
                case mavlink::MSG_COMMAND_LONG:     onCommandLong(msg, src); break;
                case mavlink::MSG_SET_MODE:         onSetMode(msg); break;
                case mavlink::MSG_MISSION_COUNT:    onMissionCount(msg, src); break;
                case mavlink::MSG_MISSION_ITEM_INT: onMissionItemInt(msg, src); break;
                case mavlink::MSG_MISSION_CLEAR_ALL: onMissionClearAll(); break;
                case mavlink::MSG_NAMED_VALUE_FLOAT: onNamedValueFloat(msg); break;
                case mavlink::MSG_SET_POSITION_TARGET_GLOBAL_INT: onGoto(msg); break;
            }
        }
    }

    uint32_t now = millis();

    // FSM tick
    if (now - g_lastFsmTick >= FSM_TICK_MS) {
        g_lastFsmTick = now;
        fsmTick();
    }

    // Virtual GPS tick
    if (now - g_lastGpsTick >= GPS_TICK_MS) {
        float dt = (now - g_lastGpsTick) / 1000.0f;
        g_lastGpsTick = now;
        gpsTick(dt);
    }

    // Battery monitor REMOVED — no divider hardware, no telemetry, no failsafe
    // (user opted to skip battery percentage entirely)

    // Telemetry TX
    if (now - g_lastHbTx >= HEARTBEAT_TX_MS) { g_lastHbTx = now; txHeartbeat(); }
    if (now - g_lastPosTx >= POSITION_TX_MS) { g_lastPosTx = now; txGlobalPosition(); }

    // WiFi debug log every 5s
    static uint32_t s_lastWifiLog = 0;
    if (now - s_lastWifiLog >= 5000) {
        s_lastWifiLog = now;
        Serial.printf("[WIFI] status=%d rssi=%d ip=%s\n",
                      WiFi.status(), WiFi.RSSI(),
                      WiFi.localIP().toString().c_str());
    }

    // WiFi reconnect
    if (WiFi.status() != WL_CONNECTED) {
        static uint32_t lastReconnectAttempt = 0;
        static uint8_t reconnectCount = 0;
        if (now - lastReconnectAttempt >= WIFI_RECONNECT_MS) {
            lastReconnectAttempt = now;
            reconnectCount++;
            Serial.printf("[WIFI] disconnected, reconnect attempt %d\n", reconnectCount);
            WiFi.reconnect();
            if (reconnectCount >= WIFI_MAX_RETRIES && g_state == FsmState::FLYING) {
                changeState(FsmState::RTL, "wifi lost");
            }
        }
    }

    // GCS heartbeat watchdog
    if (g_gcsSeen && (now - g_lastGcsHb) > HEARTBEAT_TIMEOUT_MS &&
        (g_state == FsmState::FLYING || g_state == FsmState::TAKEOFF || g_state == FsmState::ARMED)) {
        changeState(FsmState::RTL, "GCS heartbeat lost");
        txStatusText(2, "Failsafe: GCS HB lost, RTL");
    }

    peripherals::tick();
    peripherals::onboardLedTick(g_state, WiFi.status() == WL_CONNECTED);
}

// ── WiFi + OTA ────────────────────────────────────────────────────────
static void connectWifi() {
#if defined(WIFI_SSID) && defined(WIFI_PASS)
    // Hardcoded test mode (secrets_local.h present) — skip captive portal.
    Serial.printf("[WIFI] connecting to '%s' (hardcoded)\n", WIFI_SSID);
    WiFi.mode(WIFI_STA);
    // VN region (scan ch 1-13)
    wifi_country_t country = { .cc = "VN", .schan = 1, .nchan = 13,
                                .max_tx_power = 78, .policy = WIFI_COUNTRY_POLICY_MANUAL };
    esp_wifi_set_country(&country);
    // ESP32-C3 Super Mini PCB antenna fix: lower TX power + disable modem sleep.
    WiFi.setSleep(false);
    WiFi.setTxPower(WIFI_POWER_8_5dBm);
    // Warmup RF with a scan (improves first-connect reliability on C3 Super Mini)
    Serial.println("[WIFI] RF warmup scan...");
    int n = WiFi.scanNetworks(false, true);
    Serial.printf("[WIFI] scan saw %d networks\n", n);
    WiFi.scanDelete();
    delay(200);
    WiFi.begin(WIFI_SSID, WIFI_PASS);
    uint32_t start = millis();
    while (WiFi.status() != WL_CONNECTED && (millis() - start) < 20000) {
        delay(300);
        Serial.print(".");
    }
    Serial.println();
    if (WiFi.status() == WL_CONNECTED) {
        Serial.printf("[WIFI] connected IP=%s RSSI=%d\n",
                      WiFi.localIP().toString().c_str(), WiFi.RSSI());
    } else {
        Serial.println("[WIFI] connect failed (check SSID/pass), continuing offline");
    }
#else
    char apName[32];
    snprintf(apName, sizeof(apName), "AeroSwarm-Setup-%d", g_sysId);
    g_wm.setConfigPortalTimeout(180);
    bool ok = g_wm.autoConnect(apName);
    if (ok) {
        Serial.printf("[WIFI] connected IP=%s RSSI=%d\n",
                      WiFi.localIP().toString().c_str(), WiFi.RSSI());
    } else {
        Serial.println("[WIFI] portal timed out, continuing offline");
    }
#endif
}

static void setupOta() {
    char hostname[32];
    snprintf(hostname, sizeof(hostname), "aeroswarm-drone-%d", g_sysId);
    ArduinoOTA.setHostname(hostname);
    ArduinoOTA.setPassword(OTA_PASSWORD);
    ArduinoOTA.onStart([]{ Serial.println("[OTA] start"); });
    ArduinoOTA.onEnd([]{ Serial.println("[OTA] done"); });
    ArduinoOTA.onError([](ota_error_t e){ Serial.printf("[OTA] error %u\n", e); });
    ArduinoOTA.begin();
    Serial.printf("[OTA] %s ready (pwd %s)\n", hostname, OTA_PASSWORD);
}

// ── MAVLink handlers ──────────────────────────────────────────────────
static void onHeartbeat(const mavlink::Decoded& m, IPAddress src) {
    // Heartbeats from GCS sysId 255 reset our watchdog and pin GCS IP
    if (m.sysId == 255) {
        g_lastGcsHb = millis();
        if (!g_gcsSeen) {
            g_gcsSeen = true;
            g_gcsIp = src;
            Serial.printf("[MAV] GCS heartbeat from %s\n", src.toString().c_str());
            txStatusText(6, "GCS link established");
        }
    }
}

static void onCommandLong(const mavlink::Decoded& m, IPAddress src) {
    mavlink::CommandLongParams c;
    if (!mavlink::parseCommandLong(m.payload, m.payloadLen, c)) return;
    if (c.targetSys != 0 && c.targetSys != g_sysId) return;

    if (!g_gcsSeen) { g_gcsIp = src; g_gcsSeen = true; g_lastGcsHb = millis(); }

    Serial.printf("[MAV] COMMAND_LONG cmd=%d sys=%d p1=%.1f\n", c.command, c.targetSys, c.p[0]);

    switch (c.command) {
        case mavlink::CMD_COMPONENT_ARM_DISARM:
            if (c.p[0] > 0.5f) {
                if (g_state == FsmState::IDLE) {
                    changeState(FsmState::ARMED, "arm cmd");
                    txCommandAck(c.command, 0);
                    peripherals::buzzerPlay(BuzzerPattern::ARM);
                } else txCommandAck(c.command, 4); // failed
            } else {
                if (g_state == FsmState::ARMED || g_state == FsmState::IDLE) {
                    changeState(FsmState::IDLE, "disarm cmd");
                    txCommandAck(c.command, 0);
                    peripherals::buzzerPlay(BuzzerPattern::DISARM);
                } else txCommandAck(c.command, 4);
            }
            break;

        case mavlink::CMD_NAV_TAKEOFF:
            if (g_state == FsmState::ARMED || g_state == FsmState::IDLE) {
                g_targetAlt = (c.p[6] > 0.5f) ? c.p[6] : 10.0f;
                changeState(FsmState::TAKEOFF, "takeoff cmd");
                txCommandAck(c.command, 0);
                peripherals::buzzerPlay(BuzzerPattern::TAKEOFF);
            } else txCommandAck(c.command, 4);
            break;

        case mavlink::CMD_NAV_RETURN_TO_LAUNCH:
            if (!g_homeSet) {
                g_homeLat = g_lat; g_homeLon = g_lon; g_homeAlt = g_alt;
                g_homeSet = true;
                txStatusText(6, "Home auto-set for RTL");
            }
            changeState(FsmState::RTL, "rtl cmd");
            txCommandAck(c.command, 0);
            peripherals::buzzerPlay(BuzzerPattern::RTL_START);
            break;

        case mavlink::CMD_NAV_LAND:
            changeState(FsmState::LANDING, "land cmd");
            txCommandAck(c.command, 0);
            break;

        case mavlink::CMD_DO_SET_HOME:
            if (c.p[0] < 0.5f) {
                g_homeLat = c.p[4];
                g_homeLon = c.p[5];
                g_homeAlt = c.p[6];
                g_homeSet = true;
                // Do NOT override current GPS position — SET_HOME should only
                // define the RTL return point, not teleport the drone.
                Serial.printf("[MAV] HOME set to (%.6f, %.6f)\n", g_homeLat, g_homeLon);
                txStatusText(6, "Home set");
            } else {
                g_homeLat = g_lat; g_homeLon = g_lon; g_homeAlt = g_alt;
                g_homeSet = true;
            }
            txCommandAck(c.command, 0);
            break;

        default:
            txCommandAck(c.command, 3); // UNSUPPORTED
            break;
    }
}

static void onSetMode(const mavlink::Decoded& m) {
    mavlink::SetModePayload sm;
    if (!mavlink::parseSetMode(m.payload, m.payloadLen, sm)) return;
    if (sm.targetSys != g_sysId) return;
    Serial.printf("[MAV] SET_MODE custom=%u\n", sm.customMode);
    switch (sm.customMode) {
        case 6:
            if (!g_homeSet) {
                g_homeLat = g_lat; g_homeLon = g_lon; g_homeAlt = g_alt;
                g_homeSet = true;
                txStatusText(6, "Home auto-set for RTL");
            }
            changeState(FsmState::RTL, "set_mode rtl");
            peripherals::buzzerPlay(BuzzerPattern::RTL_START);
            break;
        case 9: changeState(FsmState::LANDING, "set_mode land"); break;
        case 4:
            // GUIDED mode is accepted but does NOT auto-transition state.
            // The drone must reach FLYING via TAKEOFF first.
            break;
        default: break;
    }
}

static void onMissionCount(const mavlink::Decoded& m, IPAddress src) {
    mavlink::MissionCountPayload mc;
    if (!mavlink::parseMissionCount(m.payload, m.payloadLen, mc)) return;
    if (mc.targetSys != g_sysId) return;
    g_missionUploadPending = mc.count > MAX_WP ? MAX_WP : mc.count;
    g_missionExpectedSeq = 0;
    g_missionUploadSrcSys = m.sysId;
    Serial.printf("[MAV] MISSION_COUNT=%d, requesting items\n", g_missionUploadPending);
    txMissionRequestInt(m.sysId, 0);
}

static void onMissionItemInt(const mavlink::Decoded& m, IPAddress src) {
    mavlink::MissionItemInt wp;
    if (!mavlink::parseMissionItemInt(m.payload, m.payloadLen, wp)) return;
    if (wp.targetSys != g_sysId) return;

    if (wp.seq < MAX_WP) {
        g_mission[wp.seq].lat = wp.latE7 / 1e7;
        g_mission[wp.seq].lon = wp.lonE7 / 1e7;
        g_mission[wp.seq].alt = wp.alt;
        g_mission[wp.seq].cmd = wp.command;
    }
    Serial.printf("[MAV] MISSION_ITEM_INT seq=%d (%.6f, %.6f) alt=%.1f\n",
                  wp.seq, g_mission[wp.seq].lat, g_mission[wp.seq].lon, wp.alt);
    g_missionExpectedSeq = wp.seq + 1;
    if (g_missionExpectedSeq < g_missionUploadPending) {
        txMissionRequestInt(m.sysId, g_missionExpectedSeq);
    } else {
        g_missionCount = g_missionUploadPending;
        g_missionSeq = 0;
        saveMissionToNvs();
        txMissionAck(m.sysId, 0); // MAV_MISSION_ACCEPTED
        txStatusText(6, "Mission uploaded");
        Serial.printf("[MAV] mission accepted, %d waypoints\n", g_missionCount);
    }
}

static void onMissionClearAll() {
    g_missionCount = 0;
    g_missionSeq = 0;
    clearMissionNvs();
    txStatusText(6, "Mission cleared");
}

static void onNamedValueFloat(const mavlink::Decoded& m) {
    mavlink::NamedValueFloatPayload p;
    if (!mavlink::parseNamedValueFloat(m.payload, m.payloadLen, p)) return;

    char name[11];
    memcpy(name, p.name, 10);
    name[10] = '\0';

    if (strcmp(name, "AS_ARM") == 0) {
        g_syncArmed = (p.value > 0.5f);
    } else if (strcmp(name, "AS_MOD") == 0) {
        g_syncState = (FsmState)(int)p.value;
    } else if (strcmp(name, "AS_ALT") == 0) {
        g_syncAlt = p.value;
    } else if (strcmp(name, "AS_LAT") == 0) {
        g_lat = p.value;
    } else if (strcmp(name, "AS_LON") == 0) {
        g_lon = p.value;
    } else {
        return; // unknown name
    }

    g_lastSyncMs = millis();
    g_syncActive = true;

    // Apply synced state immediately to FSM + peripherals
    if (strcmp(name, "AS_MOD") == 0) {
        FsmState target = g_syncState;
        if (target != g_state) {
            // Validate target is within valid range
            if (target >= FsmState::BOOT && target <= FsmState::ERROR_STATE) {
                changeState(target, "sitl sync");
            }
        }
    }

    if (strcmp(name, "AS_ALT") == 0) {
        g_targetAlt = g_syncAlt;
        g_alt = g_syncAlt; // mirror altitude for telemetry TX
    }

    if (strcmp(name, "AS_ARM") == 0) {
        // If disarmed remotely, force IDLE
        if (!g_syncArmed && g_state != FsmState::IDLE) {
            changeState(FsmState::IDLE, "sitl disarm");
        }
    }
}

static void onGoto(const mavlink::Decoded& m) {
    mavlink::SetPositionTargetGlobalIntPayload p;
    if (!mavlink::parseSetPositionTargetGlobalInt(m.payload, m.payloadLen, p)) return;
    if (p.targetSys != 0 && p.targetSys != g_sysId) return;
    // Reject GOTO if not already flying (must TAKEOFF first)
    if (g_state != FsmState::FLYING) {
        txStatusText(4, "Goto rejected: not flying");
        return;
    }

    g_targetLat = p.latE7 / 1e7;
    g_targetLon = p.lonE7 / 1e7;
    g_targetAlt = p.alt;
    g_hasTarget = true;
    Serial.printf("[MAV] GOTO (%.6f, %.6f) alt=%.1f  frame=%u mask=%u\n",
                  g_targetLat, g_targetLon, g_targetAlt, p.coordinateFrame, p.typeMask);
}

// ── State machine ─────────────────────────────────────────────────────
static bool isFlightState(FsmState s) {
    return s == FsmState::TAKEOFF || s == FsmState::FLYING ||
           s == FsmState::RTL     || s == FsmState::LANDING;
}

static void changeState(FsmState s, const char* reason) {
    if (s == g_state) return;
    // Only reset motor throttle when transitioning between ground and flight.
    // Staying within flight states (TAKEOFF→FLYING→RTL→LANDING) keeps ramp
    // to avoid brown-out from current spikes.
    bool wasFlying = isFlightState(g_state);
    bool nowFlying = isFlightState(s);
    if (wasFlying != nowFlying) {
        g_currentMotorThrottle = 0;
    }
    const char* names[] = {"BOOT","IDLE","ARMED","TAKEOFF","FLYING","RTL","LANDING","ERROR"};
    Serial.printf("[FSM] %s → %s (%s)\n", names[(int)g_state], names[(int)s], reason);
    g_state = s;
    peripherals::setFsmState(s, g_battWarn);

    // Drone 3 runs higher motor throttle (~30%) because its USB port
    // can supply more current. Other drones stay at ~4% to avoid brownout.
    uint8_t mt = 10;   // 10/255 ≈ 4%

    switch (s) {
        case FsmState::TAKEOFF:
            g_targetLat = g_lat; g_targetLon = g_lon;  // climb in place
            g_hasTarget = true;
            g_targetMotorThrottle = mt;
            break;
        case FsmState::FLYING:
            g_targetMotorThrottle = mt;   // cruise
            break;
        case FsmState::RTL:
            g_targetLat = g_homeLat; g_targetLon = g_homeLon;
            g_targetAlt = g_homeAlt + 10.0f;
            g_hasTarget = g_homeSet;
            g_targetMotorThrottle = mt;
            break;
        case FsmState::LANDING:
            g_targetAlt = 0.0f;
            g_targetMotorThrottle = 8;  // ~3%
            break;
        case FsmState::IDLE:
            g_targetMotorThrottle = 0;
            g_hasTarget = false;
            g_alt = 0.0f;
            g_targetAlt = 0.0f;
            break;
        case FsmState::ARMED:
            g_targetMotorThrottle = 0;    // props off when armed
            break;
        case FsmState::ERROR_STATE:
            g_targetMotorThrottle = 0;
            break;
        default: break;
    }
}

static void fsmTick() {
    // If sync lost for 5s, resume local FSM autonomy
    if (g_syncActive && (millis() - g_lastSyncMs) > 5000) {
        g_syncActive = false;
        txStatusText(4, "SITL sync lost, local FSM");
    }

    switch (g_state) {
        case FsmState::TAKEOFF:
            if (g_syncActive) break;
            if (fabs(g_alt - g_targetAlt) < 0.5f) {
                changeState(FsmState::FLYING, "reached takeoff alt");
                // Start mission if we have one
                if (g_missionCount > 0) {
                    g_missionSeq = 0;
                    g_targetLat = g_mission[0].lat;
                    g_targetLon = g_mission[0].lon;
                    g_targetAlt = g_mission[0].alt;
                    g_hasTarget = true;
                }
            }
            break;

        case FsmState::FLYING:
            if (g_syncActive) break;
            // Advance through mission waypoints
            if (g_missionCount > 0 && g_missionSeq < g_missionCount) {
                double d = haversine(g_lat, g_lon, g_targetLat, g_targetLon);
                if (d < 1.0) {
                    g_missionSeq++;
                    if (g_missionSeq < g_missionCount) {
                        g_targetLat = g_mission[g_missionSeq].lat;
                        g_targetLon = g_mission[g_missionSeq].lon;
                        g_targetAlt = g_mission[g_missionSeq].alt;
                        Serial.printf("[MISSION] advancing to wp %d\n", g_missionSeq);
                    } else {
                        txStatusText(6, "Mission complete, RTL");
                        changeState(FsmState::RTL, "mission complete");
                    }
                }
            }
            // Geofence
            if (g_homeSet) {
                double dist = haversine(g_lat, g_lon, g_homeLat, g_homeLon);
                if (dist > GEOFENCE_RADIUS_M) {
                    txStatusText(3, "Geofence breach, RTL");
                    changeState(FsmState::RTL, "geofence");
                }
            }
            break;

        case FsmState::RTL:
            if (g_syncActive) break;
            {
                double d = haversine(g_lat, g_lon, g_homeLat, g_homeLon);
                if (d < 2.0) changeState(FsmState::LANDING, "reached home");
            }
            break;

        case FsmState::LANDING:
            if (g_syncActive) break;
            if (g_alt < 0.2f) {
                g_alt = 0.0f;
                changeState(FsmState::IDLE, "landed");
                peripherals::buzzerPlay(BuzzerPattern::LAND_DONE);
            }
            break;

        default: break;
    }

    // Kickstart: jump to 15 immediately if starting from 0 to overcome static friction
    // (was 25, lowered to reduce Li-Po + regulator brown-out on motor spin-up)
    if (g_currentMotorThrottle == 0 && g_targetMotorThrottle > 0) {
        g_currentMotorThrottle = 15;
    }

    // Global soft-start / soft-stop for motor throttle (all states)
    if (g_currentMotorThrottle < g_targetMotorThrottle) {
        g_currentMotorThrottle++;
    } else if (g_currentMotorThrottle > g_targetMotorThrottle) {
        g_currentMotorThrottle--;
    }
    // Serial.printf("[MOTOR] state=%d curr=%d target=%d\n", (int)g_state, g_currentMotorThrottle, g_targetMotorThrottle);
    peripherals::motorsSet(g_currentMotorThrottle);
}

// ── Virtual GPS ───────────────────────────────────────────────────────
static void gpsTick(float dt) {
    if (g_state != FsmState::TAKEOFF && g_state != FsmState::FLYING &&
        g_state != FsmState::RTL     && g_state != FsmState::LANDING) {
        g_vx = g_vy = g_vz = 0;
        return;
    }

    // Vertical
    float dAlt = g_targetAlt - g_alt;
    float vzMax = V_CLIMB_MPS;
    if (fabs(dAlt) > 0.05f) {
        float step = (dAlt > 0 ? vzMax : -vzMax) * dt;
        if (fabs(step) > fabs(dAlt)) g_alt = g_targetAlt;
        else g_alt += step;
        g_vz = (dAlt > 0) ? vzMax : -vzMax;
    } else g_vz = 0;

    // Horizontal — only after reached cruise altitude (or when in TAKEOFF target is current)
    if (!g_hasTarget) { g_vx = g_vy = 0; return; }

    double dN = (g_targetLat - g_lat) * (M_PI / 180.0) * 6378137.0;
    double dE = (g_targetLon - g_lon) * (M_PI / 180.0) * 6378137.0 * cos(g_lat * M_PI / 180.0);
    double dist = sqrt(dN * dN + dE * dE);
    if (dist > 0.1) {
        double step = V_GROUND_MPS * dt;
        if (step >= dist) {
            g_lat = g_targetLat;
            g_lon = g_targetLon;
            g_vx = g_vy = 0;
        } else {
            double ux = dE / dist, uy = dN / dist;
            double moveN = uy * step;
            double moveE = ux * step;
            g_lat += (moveN / 6378137.0) * (180.0 / M_PI);
            g_lon += (moveE / (6378137.0 * cos(g_lat * M_PI / 180.0))) * (180.0 / M_PI);
            g_vx = (float)(ux * V_GROUND_MPS);
            g_vy = (float)(uy * V_GROUND_MPS);
            g_heading = (float)(atan2(dE, dN) * 180.0 / M_PI);
            if (g_heading < 0) g_heading += 360.0f;
        }
    } else { g_vx = g_vy = 0; }
}

// ── Battery ───────────────────────────────────────────────────────────
static void batteryTick() {
    // Average 8 reads
    uint32_t sumMv = 0;
    for (int i = 0; i < 8; i++) {
        sumMv += analogReadMilliVolts(BATT_ADC_PIN);
    }
    float adcMv = sumMv / 8.0f;
    float battMv = adcMv * BATT_DIV_RATIO;
    float newV = battMv / 1000.0f;

    // EWMA smoothing
    g_battV = g_battV * 0.85f + newV * 0.15f;
    g_battPct = (int8_t)battVoltsToPercent(g_battV);

    BattWarn newWarn = BattWarn::NORMAL;
    uint32_t now = millis();
    if (g_battV < BATT_CRITICAL_V) {
        if (g_battCritSince == 0) g_battCritSince = now;
        if (now - g_battCritSince > BATT_CRIT_HOLD_MS) newWarn = BattWarn::CRITICAL;
        else newWarn = BattWarn::WARN;
    } else if (g_battV < BATT_WARN_V) {
        newWarn = BattWarn::WARN;
        g_battCritSince = 0;
    } else {
        g_battCritSince = 0;
    }

    if (newWarn != g_battWarn) {
        g_battWarn = newWarn;
        peripherals::setFsmState(g_state, g_battWarn);
        if (g_battWarn == BattWarn::WARN) {
            peripherals::buzzerPlay(BuzzerPattern::LOW_BATT_LOOP);
            txStatusText(4, "Battery low");
        } else if (g_battWarn == BattWarn::CRITICAL) {
            peripherals::buzzerPlay(BuzzerPattern::CRITICAL_LOOP);
            txStatusText(2, "Battery CRITICAL");
        } else if (g_battWarn == BattWarn::NORMAL) {
            peripherals::buzzerPlay(BuzzerPattern::NONE);
        }
    }
}

static float battVoltsToPercent(float v) {
    if (v >= 4.20f) return 100;
    if (v <= 3.30f) return 0;
    // Piecewise-linear LUT
    struct { float v; float p; } lut[] = {
        {4.20f, 100}, {4.10f, 90}, {4.00f, 80}, {3.90f, 65},
        {3.80f, 50}, {3.70f, 30}, {3.60f, 15}, {3.50f, 5}, {3.30f, 0}
    };
    for (int i = 0; i < 8; i++) {
        if (v >= lut[i+1].v) {
            float frac = (v - lut[i+1].v) / (lut[i].v - lut[i+1].v);
            return lut[i+1].p + frac * (lut[i].p - lut[i+1].p);
        }
    }
    return 0;
}

// ── Telemetry TX ──────────────────────────────────────────────────────
static void txHeartbeat() {
    uint8_t buf[24];
    uint8_t baseMode = 0;
    uint32_t customMode = 0;
    if (g_state == FsmState::ARMED || g_state == FsmState::TAKEOFF ||
        g_state == FsmState::FLYING || g_state == FsmState::RTL ||
        g_state == FsmState::LANDING) {
        baseMode |= 0x80; // armed
    }
    switch (g_state) {
        case FsmState::FLYING:  customMode = 4; break;  // GUIDED
        case FsmState::RTL:     customMode = 6; break;  // RTL
        case FsmState::LANDING: customMode = 9; break;  // LAND
        case FsmState::ARMED:   customMode = 0; break;  // STABILIZE (was 4/GUIDED)
        default:                customMode = 0; break;  // STABILIZE
    }
    int n = mavlink::encHeartbeat(buf, g_sysId,
                                  2,    // MAV_TYPE_QUADROTOR
                                  3,    // MAV_AUTOPILOT_ARDUPILOTMEGA (for QGC compat)
                                  baseMode, customMode,
                                  (g_state == FsmState::ERROR_STATE) ? 5 /*CRITICAL*/ : 4 /*ACTIVE*/);
    sendUdp(buf, n);
}

static void txGlobalPosition() {
    if (g_lat == 0.0 && g_lon == 0.0) return; // no fix yet
    uint8_t buf[64];
    int n = mavlink::encGlobalPositionInt(buf, g_sysId, g_lat, g_lon, g_alt,
                                          g_vx, g_vy, g_vz, g_heading);
    sendUdp(buf, n);
}

static void txBatteryStatus() {
    uint8_t buf[64];
    int n = mavlink::encBatteryStatus(buf, g_sysId, g_battV, g_battPct);
    sendUdp(buf, n);
}

static void txStatusText(uint8_t severity, const char* text) {
    uint8_t buf[80];
    int n = mavlink::encStatusText(buf, g_sysId, severity, text);
    sendUdp(buf, n);
}

static void txCommandAck(uint16_t cmd, uint8_t result) {
    uint8_t buf[24];
    int n = mavlink::encCommandAck(buf, g_sysId, cmd, result);
    sendUdp(buf, n);
}

static void txMissionAck(uint8_t targetSys, uint8_t result) {
    uint8_t buf[24];
    int n = mavlink::encMissionAck(buf, g_sysId, targetSys, result);
    sendUdp(buf, n);
}

static void txMissionRequestInt(uint8_t targetSys, uint16_t seq) {
    uint8_t buf[24];
    int n = mavlink::encMissionRequestInt(buf, g_sysId, targetSys, seq);
    sendUdp(buf, n);
}

static void sendUdp(const uint8_t* data, int len, IPAddress dst) {
    if (dst == IPAddress(0, 0, 0, 0)) {
        if (g_gcsSeen) dst = g_gcsIp;
        else dst = WiFi.broadcastIP();
    }
    g_udp.beginPacket(dst, g_udpPort);
    g_udp.write(data, len);
    g_udp.endPacket();
}

// ── Geo helpers ───────────────────────────────────────────────────────
static double haversine(double lat1, double lon1, double lat2, double lon2) {
    double R = 6378137.0;
    double dLat = (lat2 - lat1) * M_PI / 180.0;
    double dLon = (lon2 - lon1) * M_PI / 180.0;
    double a = sin(dLat / 2) * sin(dLat / 2) +
               cos(lat1 * M_PI / 180.0) * cos(lat2 * M_PI / 180.0) *
               sin(dLon / 2) * sin(dLon / 2);
    return R * 2 * atan2(sqrt(a), sqrt(1 - a));
}

// ── NVS Mission ───────────────────────────────────────────────────────
static void loadMissionFromNvs() {
    Preferences pref;
    if (!pref.begin(NVS_NS_MISSION, true)) return;
    int count = pref.getInt("count", 0);
    if (count > 0 && count <= MAX_WP) {
        for (int i = 0; i < count; i++) {
            char key[8];
            snprintf(key, sizeof(key), "wp%d", i);
            size_t sz = pref.getBytesLength(key);
            if (sz == sizeof(WP)) {
                pref.getBytes(key, &g_mission[i], sizeof(WP));
            }
        }
        g_missionCount = count;
        Serial.printf("[NVS] restored mission with %d waypoints\n", count);
    }
    pref.end();
}

static void saveMissionToNvs() {
    Preferences pref;
    if (!pref.begin(NVS_NS_MISSION, false)) return;
    pref.clear();
    pref.putInt("count", g_missionCount);
    for (int i = 0; i < g_missionCount; i++) {
        char key[8];
        snprintf(key, sizeof(key), "wp%d", i);
        pref.putBytes(key, &g_mission[i], sizeof(WP));
    }
    pref.end();
}

static void clearMissionNvs() {
    Preferences pref;
    if (!pref.begin(NVS_NS_MISSION, false)) return;
    pref.clear();
    pref.end();
}

// AeroSwarm — WiFi Debug Sketch (ESP32-C3 Super Mini)
//
// Purpose: standalone WiFi scan + connect with full event logging.
// No MAVLink / no FSM / no peripherals. Just figures out why WiFi won't connect.
//
// EDIT TARGET_SSID + TARGET_PASS below, flash, watch serial @115200.

#include <Arduino.h>
#include <WiFi.h>
#include <esp_wifi.h>

// ── EDIT ME ───────────────────────────────────────────────────────────
#define TARGET_SSID "iQOO Z10 Turbo Pro"
#define TARGET_PASS "1234567890a"
static const uint8_t TARGET_BSSID[6] = { 0x5A, 0xD2, 0x47, 0x48, 0x3F, 0x24 };
static const int TARGET_CHANNEL = 8;
// ──────────────────────────────────────────────────────────────────────

static const char* eventName(WiFiEvent_t e) {
    switch (e) {
        case ARDUINO_EVENT_WIFI_READY: return "WIFI_READY";
        case ARDUINO_EVENT_WIFI_SCAN_DONE: return "SCAN_DONE";
        case ARDUINO_EVENT_WIFI_STA_START: return "STA_START";
        case ARDUINO_EVENT_WIFI_STA_STOP: return "STA_STOP";
        case ARDUINO_EVENT_WIFI_STA_CONNECTED: return "STA_CONNECTED";
        case ARDUINO_EVENT_WIFI_STA_DISCONNECTED: return "STA_DISCONNECTED";
        case ARDUINO_EVENT_WIFI_STA_AUTHMODE_CHANGE: return "AUTHMODE_CHANGE";
        case ARDUINO_EVENT_WIFI_STA_GOT_IP: return "GOT_IP";
        case ARDUINO_EVENT_WIFI_STA_LOST_IP: return "LOST_IP";
        default: return "OTHER";
    }
}

static const char* reasonName(uint8_t r) {
    switch (r) {
        case 1: return "UNSPECIFIED";
        case 2: return "AUTH_EXPIRE (wrong password / handshake fail)";
        case 3: return "AUTH_LEAVE";
        case 4: return "ASSOC_EXPIRE";
        case 6: return "NOT_AUTHED";
        case 7: return "NOT_ASSOCED";
        case 8: return "ASSOC_LEAVE";
        case 15: return "4WAY_HANDSHAKE_TIMEOUT";
        case 16: return "GROUP_KEY_UPDATE_TIMEOUT";
        case 17: return "IE_IN_4WAY_DIFFERS";
        case 200: return "BEACON_TIMEOUT";
        case 201: return "NO_AP_FOUND (SSID not visible / wrong name / out of range)";
        case 202: return "AUTH_FAIL (wrong password)";
        case 203: return "ASSOC_FAIL";
        case 204: return "HANDSHAKE_TIMEOUT";
        case 205: return "CONNECTION_FAIL";
        default: return "OTHER";
    }
}

static const char* encName(wifi_auth_mode_t m) {
    switch (m) {
        case WIFI_AUTH_OPEN: return "OPEN";
        case WIFI_AUTH_WEP: return "WEP";
        case WIFI_AUTH_WPA_PSK: return "WPA_PSK";
        case WIFI_AUTH_WPA2_PSK: return "WPA2_PSK";
        case WIFI_AUTH_WPA_WPA2_PSK: return "WPA_WPA2_PSK";
        case WIFI_AUTH_ENTERPRISE: return "ENTERPRISE";
        case WIFI_AUTH_WPA3_PSK: return "WPA3_PSK";
        case WIFI_AUTH_WPA2_WPA3_PSK: return "WPA2_WPA3_PSK";
        case WIFI_AUTH_WAPI_PSK: return "WAPI_PSK";
        default: return "?";
    }
}

static void onEvt(WiFiEvent_t event, WiFiEventInfo_t info) {
    Serial.printf("[EVT] %s (%d) ", eventName(event), (int)event);
    if (event == ARDUINO_EVENT_WIFI_STA_DISCONNECTED) {
        uint8_t r = info.wifi_sta_disconnected.reason;
        Serial.printf("reason=%d (%s) ssid='%.*s'\n",
                      r, reasonName(r),
                      info.wifi_sta_disconnected.ssid_len,
                      info.wifi_sta_disconnected.ssid);
    } else if (event == ARDUINO_EVENT_WIFI_STA_CONNECTED) {
        Serial.printf("ssid='%.*s' ch=%d authmode=%s\n",
                      info.wifi_sta_connected.ssid_len,
                      info.wifi_sta_connected.ssid,
                      info.wifi_sta_connected.channel,
                      encName(info.wifi_sta_connected.authmode));
    } else if (event == ARDUINO_EVENT_WIFI_STA_GOT_IP) {
        Serial.printf("ip=%s gw=%s mask=%s\n",
                      IPAddress(info.got_ip.ip_info.ip.addr).toString().c_str(),
                      IPAddress(info.got_ip.ip_info.gw.addr).toString().c_str(),
                      IPAddress(info.got_ip.ip_info.netmask.addr).toString().c_str());
    } else {
        Serial.println();
    }
}

static void doScan() {
    Serial.println("\n[SCAN] starting (incl. hidden, 13 channels)...");
    int n = WiFi.scanNetworks(false, true);
    Serial.printf("[SCAN] found %d networks\n", n);
    for (int i = 0; i < n; i++) {
        String ssid = WiFi.SSID(i);
        Serial.printf("  %2d: '%s' BSSID=%s RSSI=%d ch=%d enc=%s hidden=%s len=%d\n",
                      i,
                      ssid.c_str(),
                      WiFi.BSSIDstr(i).c_str(),
                      WiFi.RSSI(i),
                      WiFi.channel(i),
                      encName(WiFi.encryptionType(i)),
                      (ssid.length() == 0 ? "Y" : "N"),
                      (int)ssid.length());
    }
    WiFi.scanDelete();
}

void setup() {
    Serial.begin(115200);
    delay(2000);
    Serial.println("\n\n=== AeroSwarm WiFi Debug ===");
    Serial.printf("[MAC] STA = %s\n", WiFi.macAddress().c_str());

    WiFi.mode(WIFI_STA);
    WiFi.setMinSecurity(WIFI_AUTH_WEP);
    WiFi.setSleep(false);
    WiFi.setTxPower(WIFI_POWER_8_5dBm);  // moderate — try if antenna PCB is poor
    WiFi.onEvent(onEvt);
    Serial.printf("[CFG] sleep=off, tx_pwr=8.5dBm\n");

    // VN region — scan ch 1-13
    wifi_country_t cc = { .cc = "VN", .schan = 1, .nchan = 13,
                          .max_tx_power = 78, .policy = WIFI_COUNTRY_POLICY_MANUAL };
    esp_wifi_set_country(&cc);

    doScan();

    Serial.printf("\n[CONNECT] target SSID='%s' (%d bytes), pass len=%d\n",
                  TARGET_SSID, (int)strlen(TARGET_SSID), (int)strlen(TARGET_PASS));
    Serial.print("[CONNECT] ssid bytes: ");
    for (size_t i = 0; i < strlen(TARGET_SSID); i++)
        Serial.printf("%02X ", (uint8_t)TARGET_SSID[i]);
    Serial.println();

    // Clear any cached creds in NVS (some Arduino-ESP32 versions reuse stale creds)
    Serial.println("[CONNECT] clearing NVS-cached creds + persistent off...");
    WiFi.persistent(false);
    WiFi.disconnect(true, true);  // wifioff + eraseAP
    delay(500);
    Serial.println("[CONNECT] calling WiFi.begin() (high-level)...");
    WiFi.begin(TARGET_SSID, TARGET_PASS);
}

static uint32_t lastStatus = 0;
static uint32_t lastRescan = 0;

void loop() {
    uint32_t now = millis();
    if (now - lastStatus > 2000) {
        lastStatus = now;
        wl_status_t s = WiFi.status();
        Serial.printf("[STATUS t=%lus] code=%d ", now / 1000, (int)s);
        switch (s) {
            case WL_CONNECTED:
                Serial.printf("CONNECTED IP=%s RSSI=%d ch=%d BSSID=%s\n",
                              WiFi.localIP().toString().c_str(),
                              WiFi.RSSI(), WiFi.channel(),
                              WiFi.BSSIDstr().c_str());
                break;
            case WL_DISCONNECTED:   Serial.println("DISCONNECTED"); break;
            case WL_NO_SSID_AVAIL:  Serial.println("NO_SSID_AVAIL"); break;
            case WL_CONNECT_FAILED: Serial.println("CONNECT_FAILED"); break;
            case WL_IDLE_STATUS:    Serial.println("IDLE"); break;
            default:                Serial.printf("?%d\n", (int)s); break;
        }
    }

    // Re-scan every 30s if still disconnected
    if (WiFi.status() != WL_CONNECTED && now - lastRescan > 30000) {
        lastRescan = now;
        doScan();
        Serial.println("[CONNECT] retry WiFi.begin()...");
        WiFi.disconnect();
        delay(200);
        WiFi.begin(TARGET_SSID, TARGET_PASS);
    }
}

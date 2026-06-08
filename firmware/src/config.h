#pragma once
#include <Arduino.h>

// ── Identity ──────────────────────────────────────────────────────────
#ifndef DEFAULT_SYSID
#define DEFAULT_SYSID 1
#endif

// ── Network ───────────────────────────────────────────────────────────
constexpr uint16_t GCS_UDP_PORT = 14550;   // RX + TX same port
constexpr uint16_t LOCAL_UDP_PORT = 14550;

// ── Pinout (ESP32-C3 Super Mini) ──────────────────────────────────────
constexpr uint8_t BATT_ADC_PIN     = 3;    // ADC1_CH3
constexpr uint8_t ONBOARD_LED_PIN  = 8;    // built-in, active-low
constexpr uint8_t BOOT_BUTTON_PIN  = 9;

#ifdef DEMO_DRONE
  constexpr uint8_t RGB_R_PIN   = 4;
  constexpr uint8_t RGB_G_PIN   = 5;
  constexpr uint8_t RGB_B_PIN   = 6;
  constexpr uint8_t RGB_R_CHAN  = 0;
  constexpr uint8_t RGB_G_CHAN  = 1;
  constexpr uint8_t RGB_B_CHAN  = 2;
  constexpr uint8_t BUZZER_PIN  = 7;
  constexpr uint8_t MOTOR_PIN   = 2;    // was 10 (SPICS0, unsafe); GPIO2 is free I/O
  constexpr uint8_t MOTOR_CHAN  = 3;
  constexpr uint32_t MOTOR_PWM_FREQ = 1000;   // 1 kHz for TIP120 (cooler than 20 kHz)
  constexpr uint8_t MOTOR_PWM_RES   = 8;     // 0-255
#endif

// ── Battery (1S Li-Po, 100k/100k discrete divider) ────────────────────
constexpr float BATT_DIV_RATIO    = 2.0f;     // 100k/100k → divide by 2
constexpr float BATT_FULL_V       = 4.20f;
constexpr float BATT_EMPTY_V      = 3.30f;    // critical threshold
constexpr float BATT_WARN_V       = 3.55f;
constexpr float BATT_CRITICAL_V   = 3.30f;
constexpr uint32_t BATT_CRIT_HOLD_MS = 2000;

// ── Failsafe thresholds ───────────────────────────────────────────────
constexpr uint32_t HEARTBEAT_TIMEOUT_MS = 3000;
constexpr uint32_t WIFI_RECONNECT_MS    = 2000;
constexpr uint8_t  WIFI_MAX_RETRIES     = 5;
constexpr float    GEOFENCE_RADIUS_M    = 200.0f;

// ── Virtual GPS dynamics ──────────────────────────────────────────────
constexpr float V_GROUND_MPS = 5.0f;     // horizontal speed
constexpr float V_CLIMB_MPS  = 1.0f;     // vertical speed
constexpr uint32_t GPS_TICK_MS = 100;    // 10 Hz

// ── Telemetry rates ───────────────────────────────────────────────────
constexpr uint32_t HEARTBEAT_TX_MS  = 1000;
constexpr uint32_t POSITION_TX_MS   = 100;   // 10 Hz
constexpr uint32_t BATTERY_TX_MS    = 1000;
constexpr uint32_t SYS_STATUS_TX_MS = 1000;

// ── FSM tick rate ─────────────────────────────────────────────────────
constexpr uint32_t FSM_TICK_MS = 50;

// ── OTA ───────────────────────────────────────────────────────────────
constexpr const char* OTA_PASSWORD = "aeroswarm";

// ── Default home (will be overridden via MAV_CMD_DO_SET_HOME) ─────────
// Initialize to (0,0) so a missing set_home is visible. The first
// GLOBAL_POSITION_INT after takeoff will use the home as start point.
constexpr double DEFAULT_HOME_LAT = 0.0;
constexpr double DEFAULT_HOME_LON = 0.0;
constexpr float  DEFAULT_HOME_ALT = 0.0f;

// ── NVS namespaces ────────────────────────────────────────────────────
constexpr const char* NVS_NS_DRONE   = "drone";
constexpr const char* NVS_NS_MISSION = "mission";

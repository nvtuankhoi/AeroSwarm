// Peripheral abstraction. On mock_drone builds (no DEMO_DRONE define) all
// functions become no-ops so we can call them unconditionally from main.cpp.
#pragma once
#include <Arduino.h>

namespace peripherals {

enum class FsmState : uint8_t {
    BOOT, IDLE, ARMED, TAKEOFF, FLYING, RTL, LANDING, ERROR_STATE
};

enum class BattWarn : uint8_t { NORMAL, WARN, CRITICAL };

enum class BuzzerPattern : uint8_t {
    NONE,
    BOOT_DONE,
    ARM,
    DISARM,
    TAKEOFF,
    RTL_START,
    LAND_DONE,
    LOW_BATT_LOOP,
    CRITICAL_LOOP,
};

void init();                                  // call once from setup()
void tick();                                  // call every loop()
void setFsmState(FsmState s, BattWarn b);     // updates RGB color/pattern
void buzzerPlay(BuzzerPattern p);             // non-blocking pattern
void motorsSet(uint8_t throttle);             // 0-255, no-op on mock
void onboardLedTick(FsmState s, bool wifi);   // simple blink on GPIO8

} // namespace peripherals

#include "peripherals.h"
#include "config.h"

namespace peripherals {

#ifdef DEMO_DRONE
static uint32_t g_buzzerNextChange = 0;
static uint8_t  g_buzzerStep = 0;
static BuzzerPattern g_buzzerPattern = BuzzerPattern::NONE;
static uint32_t g_rgbNextChange = 0;
static bool     g_rgbPhase = false;
static FsmState g_state = FsmState::BOOT;
static BattWarn g_battWarn = BattWarn::NORMAL;

static void rgbWrite(uint8_t r, uint8_t g, uint8_t b) {
    ledcWrite(RGB_R_CHAN, r);
    ledcWrite(RGB_G_CHAN, g);
    ledcWrite(RGB_B_CHAN, b);
}
#endif

static uint32_t g_onboardNextToggle = 0;
static bool     g_onboardOn = false;

void init() {
    pinMode(ONBOARD_LED_PIN, OUTPUT);
    digitalWrite(ONBOARD_LED_PIN, HIGH); // active-low → off

#ifdef DEMO_DRONE
    ledcSetup(RGB_R_CHAN, 5000, 8); ledcAttachPin(RGB_R_PIN, RGB_R_CHAN);
    ledcSetup(RGB_G_CHAN, 5000, 8); ledcAttachPin(RGB_G_PIN, RGB_G_CHAN);
    ledcSetup(RGB_B_CHAN, 5000, 8); ledcAttachPin(RGB_B_PIN, RGB_B_CHAN);
    rgbWrite(0, 0, 0);

    pinMode(BUZZER_PIN, OUTPUT);
    digitalWrite(BUZZER_PIN, LOW);

    ledcSetup(MOTOR_CHAN, MOTOR_PWM_FREQ, MOTOR_PWM_RES);
    ledcAttachPin(MOTOR_PIN, MOTOR_CHAN);
    ledcWrite(MOTOR_CHAN, 0);
#endif
}

void setFsmState(FsmState s, BattWarn b) {
#ifdef DEMO_DRONE
    g_state = s;
    g_battWarn = b;
#else
    (void)s; (void)b;
#endif
}

void motorsSet(uint8_t throttle) {
#ifdef DEMO_DRONE
    ledcWrite(MOTOR_CHAN, throttle);
#else
    (void)throttle;
#endif
}

void buzzerPlay(BuzzerPattern p) {
#ifdef DEMO_DRONE
    g_buzzerPattern = p;
    g_buzzerStep = 0;
    g_buzzerNextChange = millis();
#else
    (void)p;
#endif
}

// ── Tick loop ─────────────────────────────────────────────────────────
void tick() {
#ifdef DEMO_DRONE
    uint32_t now = millis();

    // RGB pattern per state
    uint8_t r = 0, g = 0, b = 0;
    bool pulse = false;
    uint32_t pulsePeriod = 1000;

    if (g_battWarn == BattWarn::CRITICAL) {
        r = 255; g = 0; b = 0; pulse = true; pulsePeriod = 125;
    } else if (g_battWarn == BattWarn::WARN) {
        r = 255; g = 80; b = 0; pulse = true; pulsePeriod = 250;
    } else {
        switch (g_state) {
            case FsmState::BOOT:    r = 200; g = 200; b = 200; break;
            case FsmState::IDLE:    r = 60;  g = 100; b = 255; break;
            case FsmState::ARMED:   r = 0;   g = 255; b = 60;  pulse = true; pulsePeriod = 1000; break;
            case FsmState::TAKEOFF:
            case FsmState::FLYING:  r = 0;   g = 255; b = 60;  pulse = true; pulsePeriod = 250;  break;
            case FsmState::RTL:     r = 255; g = 140; b = 0;   pulse = true; pulsePeriod = 500;  break;
            case FsmState::LANDING: r = 255; g = 140; b = 0;   pulse = true; pulsePeriod = 1000; break;
            case FsmState::ERROR_STATE: r = 255; g = 0; b = 0; break;
        }
    }

    if (pulse) {
        if ((int32_t)(now - g_rgbNextChange) >= 0) {
            g_rgbPhase = !g_rgbPhase;
            g_rgbNextChange = now + pulsePeriod / 2;
        }
        if (g_rgbPhase) rgbWrite(r, g, b);
        else rgbWrite(0, 0, 0);
    } else {
        rgbWrite(r, g, b);
    }

    // Buzzer pattern engine — sequences of (durMs, on)
    if (g_buzzerPattern != BuzzerPattern::NONE && (int32_t)(now - g_buzzerNextChange) >= 0) {
        struct Step { uint16_t dur; bool on; };
        static const Step BOOT_DONE_SEQ[]    = {{50, true}, {80, false}, {50, true}, {0, false}};
        static const Step ARM_SEQ[]          = {{200, true}, {0, false}};
        static const Step DISARM_SEQ[]       = {{500, true}, {0, false}};
        static const Step TAKEOFF_SEQ[]      = {{80, true}, {60, false}, {100, true}, {60, false}, {150, true}, {0, false}};
        static const Step RTL_START_SEQ[]    = {{200, true}, {100, false}, {200, true}, {100, false}, {200, true}, {100, false}, {200, true}, {0, false}};
        static const Step LAND_DONE_SEQ[]    = {{1000, true}, {0, false}};
        static const Step LOW_BATT_SEQ[]     = {{100, true}, {5000, false}};       // looping
        static const Step CRITICAL_SEQ[]     = {{200, true}, {300, false}};        // looping

        const Step* seq = nullptr;
        int seqLen = 0;
        bool loop = false;
        switch (g_buzzerPattern) {
            case BuzzerPattern::BOOT_DONE:    seq = BOOT_DONE_SEQ; seqLen = 4; break;
            case BuzzerPattern::ARM:          seq = ARM_SEQ; seqLen = 2; break;
            case BuzzerPattern::DISARM:       seq = DISARM_SEQ; seqLen = 2; break;
            case BuzzerPattern::TAKEOFF:      seq = TAKEOFF_SEQ; seqLen = 6; break;
            case BuzzerPattern::RTL_START:    seq = RTL_START_SEQ; seqLen = 8; break;
            case BuzzerPattern::LAND_DONE:    seq = LAND_DONE_SEQ; seqLen = 2; break;
            case BuzzerPattern::LOW_BATT_LOOP: seq = LOW_BATT_SEQ; seqLen = 2; loop = true; break;
            case BuzzerPattern::CRITICAL_LOOP: seq = CRITICAL_SEQ; seqLen = 2; loop = true; break;
            default: break;
        }
        if (seq && g_buzzerStep < seqLen) {
            digitalWrite(BUZZER_PIN, seq[g_buzzerStep].on ? HIGH : LOW);
            g_buzzerNextChange = now + seq[g_buzzerStep].dur;
            g_buzzerStep++;
            if (g_buzzerStep >= seqLen) {
                if (loop) g_buzzerStep = 0;
                else {
                    digitalWrite(BUZZER_PIN, LOW);
                    g_buzzerPattern = BuzzerPattern::NONE;
                }
            }
        }
    }
#endif
}

void onboardLedTick(FsmState s, bool wifi) {
    uint32_t now = millis();
    uint32_t period = 1000;
    if (!wifi) period = 200;        // fast blink while not connected
    else {
        switch (s) {
            case FsmState::ARMED:
            case FsmState::TAKEOFF:
            case FsmState::FLYING:  period = 250; break;
            case FsmState::RTL:
            case FsmState::LANDING: period = 500; break;
            case FsmState::ERROR_STATE: period = 100; break;
            default: period = 1000; break;
        }
    }
    if ((int32_t)(now - g_onboardNextToggle) >= 0) {
        g_onboardOn = !g_onboardOn;
        digitalWrite(ONBOARD_LED_PIN, g_onboardOn ? LOW : HIGH); // active-low
        g_onboardNextToggle = now + period / 2;
    }
}

} // namespace peripherals

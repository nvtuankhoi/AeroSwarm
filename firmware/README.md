# AeroSwarm — ESP32-C3 Firmware

PlatformIO project for the mock drone swarm.

## Build variants

| Variant | Hardware | Use for |
|---|---|---|
| `demo_sysid1` | Full peripherals (RGB LED + buzzer + 4 motors) | The 1 "demo" board on display |
| `mock_sysid2..5` | Battery only + onboard LED | The N-1 mock boards |

## First flash (USB tethered)

```bash
cd firmware
pio run -e demo_sysid1 -t upload
pio run -e mock_sysid2 -t upload
# repeat for sysid3, sysid4, sysid5
```

## WiFi setup (per drone, first boot)

1. After flash, the drone creates an open AP named `AeroSwarm-Setup-N` (where N = SysID).
2. Connect your phone/laptop to that AP — a captive portal opens automatically.
3. Enter your home WiFi SSID + password and submit.
4. Drone stores credentials in NVS, reboots, joins your network.
5. Future boots use saved creds; no portal needed.

## OTA flash (after WiFi configured)

```bash
pio run -e demo_sysid1 -t upload --upload-port aeroswarm-drone-1.local
# OTA password is "aeroswarm" (set in config.h via OTA_PASSWORD)
```

## Wiring (demo drone, ZERO solder via Western-Union splice + heat shrink)

| GPIO | Component | Notes |
|---|---|---|
| GPIO3 | Battery ADC | 100kΩ/100kΩ divider tap on B+ of 134N3P JST socket |
| GPIO4 | RGB Red | + 220Ω current limit resistor |
| GPIO5 | RGB Green | + 100Ω |
| GPIO6 | RGB Blue | + 100Ω |
| GPIO7 | Buzzer TMB12A05 | Active digital |
| GPIO10 | Motor PWM | 1kHz → 2× TIP120 bases via 1kΩ + 10kΩ pull-down → GND |
| GPIO8 | Onboard LED | Built-in, active-low, debug heartbeat |

Power: Li-Po 1S 502030 (300mAh) → 134N3P JST → USB-A → USB-C cable → ESP32-C3 USB-C.

## Telemetry

- HEARTBEAT 1 Hz
- GLOBAL_POSITION_INT 10 Hz
- BATTERY_STATUS 1 Hz
- STATUSTEXT on state transitions

## Failsafe

| Trigger | Threshold | Action |
|---|---|---|
| GCS heartbeat lost | 3 s no MSG_HEARTBEAT from sysid=255 | RTL |
| Battery low | < 3.55 V | STATUSTEXT WARN |
| Battery critical | < 3.30 V sustained 2 s | RTL → LAND |
| Geofence | distance(home, current) > 200 m | RTL |
| WiFi disconnect | 5× failed reconnect | RTL |

## State machine

```
BOOT → IDLE ⇄ ARMED → TAKEOFF → FLYING → RTL → LANDING → IDLE
                                          ↑      ↓
                                  failsafe events
```

## Reset

- Hold BOOT button (GPIO9) at power-on to enter download mode for USB reflash.
- To re-run captive portal (e.g. new WiFi), erase NVS:
  ```bash
  pio run -e demo_sysid1 -t erase
  ```

# AeroSwarm — Multi-UAV Command Center

Real-time dashboard giám sát và điều khiển đội drone ESP32-C3 qua giao thức MAVLink v2.

![Stack](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet) ![Stack](https://img.shields.io/badge/React-19-61DAFB?logo=react) ![Stack](https://img.shields.io/badge/Vite-6-646CFF?logo=vite) ![Stack](https://img.shields.io/badge/SignalR-realtime-00b954) ![Stack](https://img.shields.io/badge/MAVLink-v2-orange)

---

## Kiến trúc

```
AeroSwarm/
├── AeroSwarm.Api/                  # ASP.NET Core 9 — REST + SignalR + MAVLink
│   ├── Controllers/                # AuthController, DroneCommandController,
│   │                               # MissionController, ConfigController
│   ├── Hubs/                       # DroneHub (SignalR /hubs/drone)
│   ├── Workers/                    # MavlinkWorker (UDP:14550 RX)
│   │                               # HeartbeatSender (1Hz GCS→drone)
│   │                               # FailsafeMonitor (dropout + battery)
│   ├── Services/                   # IDroneStateService + DroneStateService (singleton)
│   ├── Core/                       # MavlinkV2 (encode/decode + CRC X.25 + CRC_EXTRA)
│   │                               # Geo (haversine + lat/lon offset)
│   │                               # SwarmLogic (VFormation, LineFormation, MissionPlanner)
│   ├── Models/                     # DroneTelemetry, TelemetryHistory, DroneEvent,
│   │                               # Flight, Waypoint
│   ├── Options/                    # SwarmOptions (typed config, configurable N drones)
│   ├── Data/                       # AppDbContext (EF Core + SQLite)
│   └── Program.cs                  # Serilog, Swagger, HealthChecks, JWT, SignalR, CORS
├── aeroswarm-ui/                   # React 19 + Vite 6 + Tailwind
│   └── src/
│       ├── components/             # Login, Dashboard, MissionPlanner
│       └── services/               # authService.js, swarmService.js
├── firmware/                       # ESP32-C3 PlatformIO project (v2 — modular)
│   ├── platformio.ini              # 2 build variants: demo_sysid1 + mock_sysid2..5
│   └── src/                        # main.cpp, config.h, mavlink.h, peripherals.{h,cpp}
└── esp32-firmware/                 # Legacy Arduino sketch (TX-only stub, kept for reference)
    └── drone_simulator/drone_simulator.ino
```

## Hardware

| Thiết bị | Thông số |
|---|---|
| **MCU** | ESP32-C3 Super Mini × N (1 demo + N-1 mock, configurable) |
| **Protocol** | MAVLink v2 over UDP (common dialect, CRC X.25 + CRC_EXTRA) |
| **Port** | 14550 (single, cả RX + TX) |
| **IP template** | `10.105.151.{IpStart + droneId - 1}` (cấu hình trong `appsettings.json`) |
| **Power** | Pin 1S LiPo 502030 300mAh + module 134N3P (USB-A out + Micro-USB in + JST PH 2.0) |
| **Demo drone extras** | LED RGB common cathode + Buzzer TMB12A05 + 4× motor 716 + 1× MOSFET IRLZ44N common PWM |
| **Assembly** | ZERO-solder qua Western-Union splice + ống co nhiệt cách điện trong shell 3D printed |

---

## Yêu cầu môi trường

| Công cụ | Phiên bản |
|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 9.0+ |
| [Node.js](https://nodejs.org/) | 18+ |
| npm | 9+ |

---

## Chạy dự án

### 1. Clone repository

```bash
git clone https://github.com/nvtuankhoi/AeroSwarm.git
cd AeroSwarm
```

---

### 2. Backend — ASP.NET Core API

```bash
cd AeroSwarm.Api

# Restore dependencies
dotnet restore

# Chạy server (port 5501)
dotnet run
```

Server khởi động tại `http://localhost:5501`  
Database SQLite (`aeroswarm.db`) được tạo tự động khi chạy lần đầu.

**Kiểm tra API:**
```bash
# Login để lấy JWT token
curl -X POST http://localhost:5501/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}'
```

---

### 3. Frontend — React + Vite

Mở terminal mới:

```bash
cd aeroswarm-ui

# Cài dependencies
npm install

# Chạy dev server (port 5173)
npm run dev
```

Mở trình duyệt tại **`http://localhost:5173`**

---

### 4. Đăng nhập

| Trường | Giá trị |
|---|---|
| **Username** | `admin` |
| **Password** | `admin123` |

---

### 5. ESP32-C3 Firmware (PlatformIO project — v2)

Project chính trong `firmware/`. Hai build variants:

```bash
cd firmware
# Demo drone (1 board với full peripherals)
pio run -e demo_sysid1 -t upload

# Mock drones (battery + onboard LED only)
pio run -e mock_sysid2 -t upload
pio run -e mock_sysid3 -t upload
pio run -e mock_sysid4 -t upload
pio run -e mock_sysid5 -t upload
```

**Setup WiFi cho mỗi drone (lần boot đầu):**
1. Drone tạo AP `AeroSwarm-Setup-{SysID}` (không mật khẩu)
2. Nối phone/laptop vào AP đó → captive portal tự mở
3. Nhập SSID + mật khẩu WiFi nhà → submit
4. Drone lưu vào NVS, reboot, join WiFi tự động

**OTA flash (sau khi WiFi đã setup):**
```bash
pio run -e demo_sysid1 -t upload --upload-port aeroswarm-drone-1.local
# Password OTA: "aeroswarm"
```

**Telemetry firmware gửi về backend:**
| Message | Tần suất | Nội dung |
|---|---|---|
| HEARTBEAT (MSG 0) | 1 Hz | mode + armed bit |
| GLOBAL_POSITION_INT (MSG 33) | 10 Hz | lat/lon/alt/vel/heading |
| BATTERY_STATUS (MSG 147) | 1 Hz | Voltage + percent từ LUT |
| STATUSTEXT (MSG 253) | event | State transitions, failsafe alerts |

**Firmware xử lý lệnh từ backend:** ARM/DISARM, NAV_TAKEOFF, NAV_RETURN_TO_LAUNCH, NAV_LAND, DO_SET_HOME, SET_MODE, MISSION_COUNT/ITEM_INT/CLEAR_ALL.

**Failsafe**: heartbeat watchdog 3s → RTL, low batt 3.30V sustained 2s → RTL+LAND, geofence 200m → RTL, WiFi 5× retry fail → RTL.

> **Firewall**: mở UDP 14550:
> ```powershell
> # PowerShell (Admin)
> netsh advfirewall firewall add rule name="AeroSwarm MAVLink UDP 14550" dir=in action=allow protocol=UDP localport=14550
> ```

> **Legacy `.ino`**: `esp32-firmware/drone_simulator/drone_simulator.ino` là sketch cũ TX-only (orbit giả lập), giữ làm reference. Dùng `firmware/` cho production.

---

```
ESP32-C3 Drone
    │ MAVLink v2 UDP packet (port 14550)
    ▼
MavlinkWorker.cs
    │ Parse HEARTBEAT (MSG_ID 0)           → mode, armed state
    │ Parse GLOBAL_POSITION_INT (MSG_ID 33) → lat, lon, alt, speed, heading
    │ Parse BATTERY_STATUS (MSG_ID 147)     → battery %, voltage
    │ Parse GPS_RAW_INT (MSG_ID 24)         → satellites visible
    │ Parse WIND (MSG_ID 168)               → wind speed, direction
    ▼
DroneHub.cs (SignalR)
    │ Broadcast "ReceiveTelemetry" → all connected clients
    │ Write TelemetryHistory to SQLite (mỗi 5 giây)
    ▼
Dashboard.jsx (React)
    │ Update drone markers trên Leaflet map
    └── Cập nhật DroneTelemetryCard (altitude, speed, heading, battery...)
```

---

## REST endpoints

### Per-drone commands

| Endpoint | Body | Hành động |
|---|---|---|
| `POST /api/drones/{id}/arm` | — | ARM động cơ |
| `POST /api/drones/{id}/disarm` | — | DISARM |
| `POST /api/drones/{id}/rtl` | — | Return to Launch |
| `POST /api/drones/{id}/land` | — | Emergency Land |
| `POST /api/drones/{id}/guided` | — | GUIDED mode |
| `POST /api/drones/{id}/takeoff` | `{"altitude": 10}` | Cất cánh (mét) |
| `POST /api/drones/{id}/goto` | `{"lat": ..., "lon": ..., "alt": ...}` | Bay đến waypoint |
| `POST /api/drones/{id}/sethome` | `{"lat": ..., "lon": ..., "alt": 0}` | Set home position |

### Swarm + mission

| Endpoint | Body | Hành động |
|---|---|---|
| `POST /api/missions/swarm` | `{ "formation": "V"\|"LINE", "spacingM": 15, "leaderWaypoints": [{"lat":..,"lon":..,"alt":..}, ...] }` | Plan + upload mission cho cả bầy đàn |
| `GET /api/flights?limit=50` | — | Lịch sử các flight |
| `GET /api/flights/{id}` | — | Chi tiết flight (waypoints + status) |

### Misc

| Endpoint | Auth | Mô tả |
|---|---|---|
| `POST /api/auth/login` | — | `{username, password}` → JWT |
| `GET /api/config` | Anonymous | `{ droneCount, droneIds, lowVoltage, criticalVoltage, ... }` |
| `GET /health` | Anonymous | Health check (DB + worker status) |
| `GET /swagger` | Dev only | Swagger UI |

Tất cả command endpoints yêu cầu header `Authorization: Bearer <token>`.

## SignalR hub (`/hubs/drone`)

| Event server → client | Payload |
|---|---|
| `ReceiveTelemetry` | Full `DroneTelemetry` snapshot — 10Hz mỗi packet UDP nhận được |
| `ReceiveEvent` | `{droneId, type, message, time}` — DroneEvent log |
| `ReceiveDroneStatus` | `{droneId, isOnline}` — dropout/reconnect |

## UI workflow

### Click-to-Fly (single drone GOTO)
1. Click marker drone (hoặc nút **GOTO** trên card) → chọn drone, con trỏ thành crosshair
2. Click bản đồ → gửi GOTO đến drone đã chọn

### PLAN MISSION (swarm V-shape / line)
1. Top bar → **PLAN MISSION** mở modal
2. Click bản đồ để thêm leader waypoints (xanh dashed line preview)
3. Chọn formation (V / LINE) + spacing + altitude → **UPLOAD**
4. Backend tính offset cho từng drone + gửi MISSION_COUNT + N × MISSION_ITEM_INT cho từng drone

### SET HOME
1. Top bar → **SET HOME** (toggle mode)
2. Click bản đồ → gửi `DO_SET_HOME` cho tất cả drone đang online

### Dropout indicator
Khi drone mất kết nối > 3s, card có viền đỏ animate-pulse + badge "DROPOUT". Khi reconnect, indicator tự clear.

---

## Database

SQLite file `aeroswarm.db` (tự động tạo qua `EnsureCreated()`):

| Table | Mô tả |
|---|---|
| `TelemetryHistories` | Snapshot telemetry mỗi 5s / drone |
| `DroneEvents` | Event log: SYS / WARN / CMD / ACK |
| `Flights` | Mission record: formation, spacing, status, start/end time |
| `Waypoints` | Per-drone waypoints linked to flight (FK Flights.Id) |

> **Schema migration**: Khi đổi schema (vd thêm bảng mới), xóa `aeroswarm.db` trước khi `dotnet run` để `EnsureCreated()` tạo lại schema. Bootstrap EF migrations chính thức: `dotnet ef migrations add InitialCreate && dotnet ef database update` rồi đổi `Program.cs` từ `EnsureCreated()` sang `Migrate()`.

---

## Cấu hình

**`AeroSwarm.Api/appsettings.json`** — phần `Swarm` quan trọng (configurable N drones + thresholds):
```json
{
  "Urls": "http://localhost:5501",
  "Jwt": { "Key": "...", "Issuer": "...", "Audience": "...", "ExpiresInMinutes": 480 },
  "ConnectionStrings": { "DefaultConnection": "Data Source=aeroswarm.db" },
  "Swarm": {
    "DroneCount": 5,
    "IpTemplate": "10.105.151.{0}",
    "IpStart": 101,
    "UdpPort": 14550,
    "HeartbeatIntervalSec": 1.0,
    "DropoutThresholdSec": 3.0,
    "TelemetryPersistIntervalSec": 5.0,
    "LowVoltagePerCell": 3.30,
    "CriticalVoltagePerCell": 3.20,
    "LiPoCellCount": 1
  },
  "Serilog": { "WriteTo": [ "Console", "File (logs/aeroswarm-*.log, rolling 7 days)" ] }
}
```

Đổi `DroneCount` → FE tự đọc qua `GET /api/config` và adapt số card hiển thị.

**`aeroswarm-ui/src/services/authService.js`** — đổi `BASE_URL` nếu deploy:
```js
const BASE_URL = 'http://localhost:5501/api'
```

**`firmware/src/config.h`** — pinout + voltage thresholds:
```cpp
constexpr uint8_t BATT_ADC_PIN     = 3;
constexpr uint8_t RGB_R_PIN        = 4;   // (DEMO_DRONE)
constexpr uint8_t RGB_G_PIN        = 5;
constexpr uint8_t RGB_B_PIN        = 6;
constexpr uint8_t BUZZER_PIN       = 7;
constexpr uint8_t MOTOR_PIN        = 10;
constexpr float   BATT_DIV_RATIO   = 2.0f;  // 100k/100k
constexpr float   BATT_CRITICAL_V  = 3.30f;
constexpr float   GEOFENCE_RADIUS_M = 200.0f;
constexpr const char* OTA_PASSWORD = "aeroswarm";
```

---

## Build production

**Backend:**
```bash
cd AeroSwarm.Api
dotnet publish -c Release -o ./publish
```

**Frontend:**
```bash
cd aeroswarm-ui
npm run build
# Output: aeroswarm-ui/dist/
```

---

## Tech Stack

| Layer | Công nghệ |
|---|---|
| **API** | ASP.NET Core 9, SignalR, EF Core 9 |
| **Auth** | JWT Bearer (HmacSha256) |
| **Database** | SQLite |
| **Protocol** | MAVLink v2 (raw UDP, manual parse) |
| **Frontend** | React 19, Vite 6, Tailwind CSS v3 |
| **Map** | Leaflet.js + react-leaflet v5, CartoDB Dark Matter |
| **Realtime** | @microsoft/signalr |
| **HTTP Client** | Axios |

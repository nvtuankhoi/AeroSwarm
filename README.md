# AeroSwarm — Multi-UAV Command Center

Real-time dashboard giám sát và điều khiển đội drone ESP32-C3 qua giao thức MAVLink v2.

![Stack](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet) ![Stack](https://img.shields.io/badge/React-19-61DAFB?logo=react) ![Stack](https://img.shields.io/badge/Vite-6-646CFF?logo=vite) ![Stack](https://img.shields.io/badge/SignalR-realtime-00b954) ![Stack](https://img.shields.io/badge/MAVLink-v2-orange)

---

## Kiến trúc

```
AeroSwarm/
├── AeroSwarm.Api/          # ASP.NET Core 9 — REST API + SignalR + MAVLink Worker
│   ├── Controllers/        # AuthController, DroneCommandController
│   ├── Hubs/               # DroneHub (SignalR)
│   ├── Workers/            # MavlinkWorker (UDP:14550)
│   ├── Models/             # DroneTelemetry, TelemetryHistory, DroneEvent
│   ├── Data/               # AppDbContext (EF Core + SQLite)
│   └── Program.cs
├── aeroswarm-ui/           # React 19 + Vite 6 + Tailwind CSS
│   └── src/
│       ├── components/     # Login, Dashboard
│       └── services/       # authService.js
└── esp32-firmware/
    └── drone_simulator/    # Arduino sketch — gửi MAVLink v2 giả lập
        └── drone_simulator.ino
```

## Hardware

| Thiết bị | Thông số |
|---|---|
| **MCU** | ESP32-C3 Super Mini × 5 |
| **Protocol** | MAVLink v2 over UDP |
| **Port** | 14550 |
| **IP drones** | `10.105.151.101` → `10.105.151.105` |

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

### 5. ESP32 Simulator (không có drone thật)

Flash sketch `esp32-firmware/drone_simulator/drone_simulator.ino` lên từng ESP32.  
Sửa 4 dòng đầu file trước khi flash:

```cpp
#define DRONE_ID    1           // 1–5, mỗi ESP32 một số khác nhau
#define WIFI_SSID   "YourSSID"
#define WIFI_PASS   "YourPassword"
#define SERVER_IP   "192.168.1.100"  // IP của PC đang chạy AeroSwarm.Api
```

Mỗi ESP32 sau khi kết nối WiFi sẽ tự động gửi MAVLink v2 đến backend:

| Message | Tần suất | Nội dung |
|---|---|---|
| HEARTBEAT (MSG 0) | 1 Hz | Mode GUIDED, trạng thái ARMED |
| GLOBAL_POSITION_INT (MSG 33) | 5 Hz | Bay vòng tròn ~50 m, altitude dao động |
| BATTERY_STATUS (MSG 147) | 0.2 Hz | Pin từ từ hao, reset khi hết |

> **Yêu cầu:** PC và ESP32 cùng subnet. Mở firewall UDP port 14550:
> ```powershell
> # PowerShell (Run as Administrator)
> netsh advfirewall firewall add rule name="AeroSwarm MAVLink UDP 14550" dir=in action=allow protocol=UDP localport=14550
> ```

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

## Điều khiển Drone

| Endpoint | Hành động |
|---|---|
| `POST /api/drones/{id}/arm` | ARM động cơ |
| `POST /api/drones/{id}/disarm` | DISARM động cơ |
| `POST /api/drones/{id}/rtl` | Return to Launch |
| `POST /api/drones/{id}/land` | Emergency Land |
| `POST /api/drones/{id}/guided` | Chuyển sang chế độ GUIDED (điều khiển vị trí) |
| `POST /api/drones/{id}/takeoff` | Cất cánh — body: `{"altitude": 10}` (mét) |
| `POST /api/drones/{id}/goto` | Bay đến toạ độ — body: `{"latitude": 10.77, "longitude": 106.70, "altitude": 15}` |

Tất cả endpoint yêu cầu header `Authorization: Bearer <token>`.

### Click-to-Fly (UI)

1. Nhấn nút **GUIDED** trên card drone để chuyển chế độ
2. Nhấn **TAKEOFF** để cất cánh
3. Nhấn **GOTO** (hoặc click marker trên bản đồ) để chọn drone → con trỏ chuyển thành crosshair
4. Click bất kỳ điểm nào trên bản đồ → gửi lệnh GOTO đến drone

---

## Database

SQLite file `aeroswarm.db` (tự động tạo, không cần migration):

| Table | Mô tả |
|---|---|
| `TelemetryHistories` | Snapshot telemetry mỗi 5 giây / drone |
| `DroneEvents` | Event log: kết nối, ngắt kết nối, cảnh báo |

---

## Cấu hình

**`AeroSwarm.Api/appsettings.json`**
```json
{
  "Urls": "http://localhost:5501",
  "Jwt": {
    "Key": "AeroSwarm_SuperSecret_Key_2025_MustBe32Chars!",
    "Issuer": "AeroSwarmApi",
    "Audience": "AeroSwarmClient",
    "ExpiresInMinutes": 480
  }
}
```

**`aeroswarm-ui/src/services/authService.js`** — đổi `BASE_URL` nếu deploy:
```js
const BASE_URL = 'http://localhost:5501/api'
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

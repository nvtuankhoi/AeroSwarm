# ✈️ AeroSwarm — Multi-UAV Command Center

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
└── aeroswarm-ui/           # React 19 + Vite 6 + Tailwind CSS
    └── src/
        ├── components/     # Login, Dashboard
        └── services/       # authService.js
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

## Luồng dữ liệu

```
ESP32-C3 Drone
    │ MAVLink v2 UDP packet (port 14550)
    ▼
MavlinkWorker.cs
    │ Parse HEARTBEAT (MSG_ID 0) → mode, armed state
    │ Parse GLOBAL_POSITION_INT (MSG_ID 33) → lat, lon, alt, speed, heading
    │ Parse BATTERY_STATUS (MSG_ID 147) → battery %, voltage
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

Tất cả endpoint yêu cầu header `Authorization: Bearer <token>`.

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

# AeroSwarm SITL Setup (macOS)

This directory contains scripts to run 5 ArduCopter SITL instances for local AeroSwarm backend/frontend development and demo.

## Port Reference

| Drone | SYSID | Instance (`-I`) | UDP Port |
|-------|-------|-----------------|----------|
| 1     | 1     | 0               | 14550    |
| 2     | 2     | 1               | 14560    |
| 3     | 3     | 2               | 14570    |
| 4     | 4     | 3               | 14580    |
| 5     | 5     | 4               | 14590    |

## macOS Recommendation: Native SITL

**Docker Desktop on macOS does not reliably route two-way UDP for multiple SITL instances** (Docker NAT collapses all outbound UDP sources to the same host port, so the backend cannot address individual drones). Therefore, on macOS we recommend running ArduPilot SITL **natively**.

### Quick Start (native)

1. **Install ArduPilot SITL** (one-time, 15–40 min depending on hardware):
   ```bash
   ./sitl/install-ardupilot-mac.sh
   ```
   This clones `github.com/ArduPilot/ardupilot` into `~/ardupilot`, installs Homebrew prerequisites, and builds the SITL binary.

2. **Start the swarm**:
   ```bash
   ./sitl/start-sitl.sh
   ```

3. **Start the full demo** (SITL + backend + frontend):
   ```bash
   ./demo-sitl.sh --native
   ```

### Updating an existing ArduPilot install

If you already ran the installer and want the latest ArduPilot code:

```bash
cd ~/ardupilot
git pull origin master
git submodule update --init --recursive
./waf configure --board sitl
./waf copter
```

## Docker Path (Linux recommended, macOS limited)

The included `docker-compose.yml` works well on **Linux** with Docker `host` network mode or direct UDP routing. On **macOS** it can receive heartbeats but replies to individual drones may not route correctly, so ARM/TAKEOFF/GOTO commands may behave unreliably in a swarm.

If you still want to try Docker (e.g. for a quick smoke test on macOS):

```bash
docker compose -f sitl/docker-compose.yml up -d
```

To stop:

```bash
./sitl/stop-sitl.sh
```

## Verify in QGroundControl

1. Open QGroundControl.
2. It should auto-detect UDP connections on `127.0.0.1:14550`, `14560`, ..., `14590`.
3. You should see 5 drones appear on the map near CMAC after GPS lock (typically 5–10 seconds).

If QGC does not auto-connect, add manual UDP links for each port.

## Upload a Mission

Upload the included demo mission to all 5 drones:

```bash
python3 sitl/upload-mission.py
```

Upload a custom mission file:

```bash
python3 sitl/upload-mission.py --mission path/to/mission.wpl
```

Upload and arm/start the mission on each drone:

```bash
python3 sitl/upload-mission.py --arm
```

## Stop SITL

```bash
./sitl/stop-sitl.sh
```

This kills any running `sim_vehicle.py` / `arducopter` processes and stops the Docker Compose stack if it is running.

## File Overview

- `install-ardupilot-mac.sh` — clones and builds ArduPilot SITL natively on macOS
- `start-sitl.sh` — native macOS launcher for 5 SITL instances
- `stop-sitl.sh` — stops native and Docker SITL instances
- `docker-compose.yml` — 5 ArduCopter SITL containers (Linux preferred)
- `upload-mission.py` — uploads a QGC WPL mission to all 5 drones in parallel
- `demo-mission.wpl` — example square mission around CMAC

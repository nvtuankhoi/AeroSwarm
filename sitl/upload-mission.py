#!/usr/bin/env python3
"""Upload a QGC WPL mission to all 5 AeroSwarm SITL drones in parallel."""

import argparse
import os
import sys
import threading
from pathlib import Path

try:
    from dronekit import connect, Command
    from pymavlink import mavutil
except ImportError as exc:
    print(f"Error: missing dependency - {exc}")
    print("Install with: pip install dronekit pymavlink")
    sys.exit(1)


PORTS = [14550, 14560, 14570, 14580, 14590]
SYS_IDS = [1, 2, 3, 4, 5]
DEFAULT_TIMEOUT = 30


def upload_to_drone(sysid: int, port: int, mission_path: Path, arm: bool) -> None:
    connection_string = f"udp:127.0.0.1:{port}"
    label = f"Drone {sysid} ({connection_string})"

    try:
        print(f"[{label}] Connecting...")
        vehicle = connect(
            connection_string,
            wait_ready=True,
            timeout=DEFAULT_TIMEOUT,
            source_system=255,
            source_component=0,
        )
    except Exception as exc:  # noqa: BLE001
        print(f"[{label}] Connection failed: {exc}")
        return

    try:
        print(f"[{label}] Connected (mode={vehicle.mode.name}, armed={vehicle.armed}).")

        # Clear existing mission
        cmds = vehicle.commands
        cmds.clear()

        # Load mission file
        mission_qgc = load_mission_file(mission_path)
        for item in mission_qgc:
            cmds.add(item)

        # Upload
        print(f"[{label}] Uploading {len(mission_qgc)} mission items...")
        cmds.upload()
        print(f"[{label}] Mission uploaded successfully.")

        if arm:
            print(f"[{label}] Arming and starting mission...")
            vehicle.mode = vehicle.mode_mapping()["AUTO"]
            vehicle.armed = True
            print(f"[{label}] Armed and in AUTO mode.")
    except Exception as exc:  # noqa: BLE001
        print(f"[{label}] Error during upload: {exc}")
    finally:
        vehicle.close()


def load_mission_file(path: Path) -> list[Command]:
    """Parse a QGC WPL file into DroneKit Command objects."""
    commands: list[Command] = []

    with path.open("r", encoding="utf-8") as fh:
        lines = fh.readlines()

    if not lines or not lines[0].startswith("QGC WPL"):
        raise ValueError("Invalid QGC WPL mission file")

    for line in lines[1:]:
        line = line.strip()
        if not line or line.startswith("#"):
            continue

        parts = line.split("\t")
        if len(parts) < 12:
            continue

        # QGC WPL format:
        # seq current frame command p1 p2 p3 p4 param1 param2 param3 param4 lat lon alt
        # DroneKit Command constructor:
        # Command(0, 0, 0, frame, command, current, autocontinue,
        #         param1, param2, param3, param4, x, y, z)
        seq = int(parts[0])
        current = int(parts[1])
        frame = int(parts[2])
        command = int(parts[3])
        p1 = float(parts[4])
        p2 = float(parts[5])
        p3 = float(parts[6])
        p4 = float(parts[7])
        lat = float(parts[8])
        lon = float(parts[9])
        alt = float(parts[10])

        cmd = Command(
            0, 0, 0,
            frame,
            command,
            current,
            1,  # autocontinue
            p1, p2, p3, p4,
            lat, lon, alt,
        )
        commands.append(cmd)

    return commands


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Upload a mission to all 5 AeroSwarm SITL drones.",
    )
    parser.add_argument(
        "--mission",
        type=Path,
        default=Path(__file__).parent / "demo-mission.wpl",
        help="Path to QGC WPL mission file (default: sitl/demo-mission.wpl)",
    )
    parser.add_argument(
        "--arm",
        action="store_true",
        help="After upload, set mode to AUTO and arm each drone",
    )
    args = parser.parse_args()

    mission_path = args.mission.resolve()
    if not mission_path.exists():
        print(f"Mission file not found: {mission_path}")
        return 1

    print(f"Uploading mission: {mission_path}")
    print(f"Targets: {len(PORTS)} drones on ports {PORTS}")
    print("")

    threads: list[threading.Thread] = []
    for sysid, port in zip(SYS_IDS, PORTS, strict=True):
        t = threading.Thread(
            target=upload_to_drone,
            args=(sysid, port, mission_path, args.arm),
            daemon=True,
        )
        t.start()
        threads.append(t)

    for t in threads:
        t.join()

    print("")
    print("Mission upload complete.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

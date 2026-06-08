#!/usr/bin/env python3
"""
AeroSwarm SITL Bridge — TCP-to-UDP proxy for Backend + QGroundControl.

Parses MAVLink v2 frames from SITL TCP stream and forwards to:
  1. AeroSwarm backend (per-drone UDP ports)
  2. QGroundControl (single UDP port 14550, multi-vehicle)

Also forwards commands from QGC back to all SITL instances.
"""

import socket
import threading
import time
import sys

# ── Config ──────────────────────────────────────────────────────────────────
SITL_HOST = "127.0.0.1"
SITL_TCP_PORTS = [5760, 5770, 5780, 5790, 5800]
BACKEND_PORTS = [14580, 14590, 14600, 14610, 14620]
BACKEND_HOST = "127.0.0.1"
QGC_HOST = "127.0.0.1"
QGC_PORT = 14551          # QGC listens here for telemetry (avoid conflict with QGC default 14550)
QGC_CMD_PORT = 15550      # We listen here for QGC commands
BUFFER_SIZE = 4096
RECONNECT_DELAY = 3.0

MAVLINK_V2_MAGIC = 0xFD
MAVLINK_V2_HEADER_LEN = 10

# Shared state: idx -> tcp_socket
tcp_sockets = {}
tcp_lock = threading.Lock()


def extract_frames(buf: bytearray):
    """Yield complete MAVLink v2 frames and leftover bytes."""
    while True:
        if len(buf) < MAVLINK_V2_HEADER_LEN + 2:
            return buf
        try:
            start = buf.index(MAVLINK_V2_MAGIC)
        except ValueError:
            return bytearray()
        if len(buf) - start < MAVLINK_V2_HEADER_LEN + 2:
            return buf[start:]
        payload_len = buf[start + 1]
        frame_len = MAVLINK_V2_HEADER_LEN + payload_len + 2
        if len(buf) - start < frame_len:
            return buf[start:]
        yield bytes(buf[start:start + frame_len])
        buf = buf[start + frame_len:]


def run_bridge(instance_idx: int, tcp_port: int, backend_udp_port: int):
    label = f"[Bridge {instance_idx + 1}]"
    print(f"{label} Starting: SITL TCP {tcp_port} -> Backend {backend_udp_port} + QGC {QGC_PORT}")

    while True:
        tcp_sock = None
        try:
            tcp_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            tcp_sock.connect((SITL_HOST, tcp_port))
            tcp_sock.setblocking(True)
            tcp_sock.settimeout(0.05)
            print(f"{label} Connected to SITL TCP {tcp_port}")

            with tcp_lock:
                tcp_sockets[instance_idx] = tcp_sock

            udp_backend = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            udp_qgc = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            backend_endpoint = (BACKEND_HOST, backend_udp_port)
            qgc_endpoint = (QGC_HOST, QGC_PORT)
            buf = bytearray()

            while True:
                try:
                    data = tcp_sock.recv(BUFFER_SIZE)
                    if not data:
                        break
                    buf.extend(data)
                except socket.timeout:
                    pass

                # Extract frames and forward to both backend and QGC
                for frame in extract_frames(buf):
                    udp_backend.sendto(frame, backend_endpoint)
                    udp_qgc.sendto(frame, qgc_endpoint)
                # Update leftover
                buf = extract_frames.__closure__ is None or bytearray()  # placeholder
                # Re-run extraction to get leftover properly
                new_buf = bytearray()
                for frame in extract_frames(buf):
                    new_buf = bytearray()  # frames consumed
                # Actually let's just re-implement inline
                break  # break to restructure

        except ConnectionRefusedError:
            print(f"{label} SITL TCP {tcp_port} not ready, retrying in {RECONNECT_DELAY}s...")
            time.sleep(RECONNECT_DELAY)
        except Exception as e:
            print(f"{label} Error: {e}, reconnecting in {RECONNECT_DELAY}s...")
            time.sleep(RECONNECT_DELAY)
        finally:
            with tcp_lock:
                tcp_sockets.pop(instance_idx, None)
            if tcp_sock:
                try:
                    tcp_sock.close()
                except Exception:
                    pass


def run_bridge_v2(instance_idx: int, tcp_port: int, backend_udp_port: int):
    """Clean re-implementation with proper framing."""
    label = f"[Bridge {instance_idx + 1}]"
    print(f"{label} SITL TCP {tcp_port} -> Backend {backend_udp_port} + QGC {QGC_PORT}")

    while True:
        tcp_sock = None
        try:
            tcp_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            tcp_sock.connect((SITL_HOST, tcp_port))
            tcp_sock.setblocking(True)
            tcp_sock.settimeout(0.05)
            print(f"{label} Connected")

            with tcp_lock:
                tcp_sockets[instance_idx] = tcp_sock

            udp_backend = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            udp_qgc = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            backend_endpoint = (BACKEND_HOST, backend_udp_port)
            qgc_endpoint = (QGC_HOST, QGC_PORT)
            buf = bytearray()

            while True:
                try:
                    data = tcp_sock.recv(BUFFER_SIZE)
                    if not data:
                        break
                    buf.extend(data)
                except socket.timeout:
                    pass

                # Forward complete frames
                while True:
                    if len(buf) < MAVLINK_V2_HEADER_LEN + 2:
                        break
                    try:
                        start = buf.index(MAVLINK_V2_MAGIC)
                    except ValueError:
                        buf.clear()
                        break
                    if len(buf) - start < MAVLINK_V2_HEADER_LEN + 2:
                        buf = buf[start:]
                        break
                    payload_len = buf[start + 1]
                    frame_len = MAVLINK_V2_HEADER_LEN + payload_len + 2
                    if len(buf) - start < frame_len:
                        buf = buf[start:]
                        break
                    frame = bytes(buf[start:start + frame_len])
                    udp_backend.sendto(frame, backend_endpoint)
                    udp_qgc.sendto(frame, qgc_endpoint)
                    buf = buf[start + frame_len:]

                # Backend replies -> SITL
                try:
                    data, addr = udp_backend.recvfrom(BUFFER_SIZE)
                    tcp_sock.sendall(data)
                except BlockingIOError:
                    pass

                time.sleep(0.001)

        except ConnectionRefusedError:
            print(f"{label} SITL TCP {tcp_port} not ready, retrying...")
            time.sleep(RECONNECT_DELAY)
        except Exception as e:
            print(f"{label} Error: {e}, reconnecting...")
            time.sleep(RECONNECT_DELAY)
        finally:
            with tcp_lock:
                tcp_sockets.pop(instance_idx, None)
            if tcp_sock:
                try:
                    tcp_sock.close()
                except Exception:
                    pass


def qgc_listener():
    """Listen for QGC commands and forward to all SITL instances."""
    udp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    udp.bind(("0.0.0.0", QGC_CMD_PORT))
    udp.setblocking(False)
    print(f"[QGC] Listening for commands on UDP {QGC_CMD_PORT}")
    print(f"[QGC] Setup: Add UDP connection in QGC -> Target host 127.0.0.1:{QGC_CMD_PORT}")

    while True:
        try:
            data, addr = udp.recvfrom(BUFFER_SIZE)
            with tcp_lock:
                socks = list(tcp_sockets.values())
            for sock in socks:
                try:
                    sock.sendall(data)
                except Exception:
                    pass
        except BlockingIOError:
            time.sleep(0.001)
        except Exception as e:
            print(f"[QGC] Error: {e}")


def main():
    threads = []

    # Start QGC command listener
    t = threading.Thread(target=qgc_listener, daemon=True)
    t.start()
    threads.append(t)

    # Start bridge instances
    for i, (tcp_port, backend_port) in enumerate(zip(SITL_TCP_PORTS, BACKEND_PORTS)):
        t = threading.Thread(
            target=run_bridge_v2,
            args=(i, tcp_port, backend_port),
            daemon=True,
        )
        t.start()
        threads.append(t)

    print(f"\nSITL+QGC Bridge running for {len(BACKEND_PORTS)} drones.")
    print("QGC setup:")
    print(f"  1. Open QGroundControl")
    print(f"  2. Application Settings -> Comm Links -> Add")
    print(f"  3. Type: UDP, Listen Port: {QGC_PORT}")
    print(f"  4. Target Hosts: 127.0.0.1:{QGC_CMD_PORT}")
    print(f"  5. Connect\n")
    print("Press Ctrl+C to stop.")
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("\nBridge stopped.")
        sys.exit(0)


if __name__ == "__main__":
    main()

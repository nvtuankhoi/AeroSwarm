#!/usr/bin/env python3
"""
AeroSwarm SITL Bridge — TCP-to-UDP MAVLink frame proxy for ArduPilot SITL.

Parses individual MAVLink v2 frames from the TCP byte stream and forwards
each complete frame as a separate UDP packet to the AeroSwarm backend.

Usage:
    python3 sitl/sitl-bridge.py
"""

import socket
import threading
import time
import sys

# ── Config ──────────────────────────────────────────────────────────────────
SITL_HOST = "127.0.0.1"
SITL_TCP_BASE = 5760
SITL_TCP_STEP = 10
BACKEND_PORTS = [14580, 14590, 14600, 14610, 14620]
BACKEND_HOST = "127.0.0.1"
BUFFER_SIZE = 4096
RECONNECT_DELAY = 3.0

MAVLINK_V2_MAGIC = 0xFD
MAVLINK_V2_HEADER_LEN = 10


def run_bridge(instance_idx: int, tcp_port: int, backend_udp_port: int):
    label = f"[Bridge {instance_idx + 1}]"
    print(f"{label} Starting: SITL TCP {tcp_port} <-> Backend UDP {backend_udp_port}")

    while True:
        tcp_sock = None
        udp_sock = None
        try:
            tcp_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            tcp_sock.connect((SITL_HOST, tcp_port))
            tcp_sock.setblocking(True)
            tcp_sock.settimeout(0.05)
            print(f"{label} Connected to SITL TCP {tcp_port}")

            udp_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            udp_sock.bind(("0.0.0.0", 0))
            udp_sock.setblocking(False)
            local_udp_addr = udp_sock.getsockname()
            print(f"{label} UDP ephemeral {local_udp_addr} -> backend {backend_udp_port}")

            backend_endpoint = (BACKEND_HOST, backend_udp_port)
            buf = bytearray()
            frames_fwd = 0

            while True:
                # TCP → buf
                try:
                    data = tcp_sock.recv(BUFFER_SIZE)
                    if not data:
                        break
                    buf.extend(data)
                except socket.timeout:
                    pass

                # Extract complete frames from buf
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
                    udp_sock.sendto(frame, backend_endpoint)
                    frames_fwd += 1
                    buf = buf[start + frame_len:]

                # UDP → TCP (backend replies)
                try:
                    data, addr = udp_sock.recvfrom(BUFFER_SIZE)
                    tcp_sock.sendall(data)
                except BlockingIOError:
                    pass

                time.sleep(0.001)

        except ConnectionRefusedError:
            print(f"{label} SITL TCP {tcp_port} not ready, retrying in {RECONNECT_DELAY}s...")
            time.sleep(RECONNECT_DELAY)
        except Exception as e:
            print(f"{label} Error: {e}, reconnecting in {RECONNECT_DELAY}s...")
            time.sleep(RECONNECT_DELAY)
        finally:
            if tcp_sock:
                try:
                    tcp_sock.close()
                except Exception:
                    pass
            if udp_sock:
                try:
                    udp_sock.close()
                except Exception:
                    pass


def main():
    threads = []
    for i, udp_port in enumerate(BACKEND_PORTS):
        tcp_port = SITL_TCP_BASE + i * SITL_TCP_STEP
        t = threading.Thread(
            target=run_bridge,
            args=(i, tcp_port, udp_port),
            daemon=True,
        )
        t.start()
        threads.append(t)

    print(f"SITL bridge running for {len(BACKEND_PORTS)} drones.")
    print("Press Ctrl+C to stop.")
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("\nBridge stopped.")
        sys.exit(0)


if __name__ == "__main__":
    main()

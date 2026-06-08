#!/usr/bin/env python3
"""
AeroSwarm SITL Bridge — TCP-to-UDP bidirectional proxy for ArduPilot SITL.

Each ArduPilot SITL instance exposes a primary MAVLink console on TCP.
This bridge connects to those TCP ports and forwards to UDP ports
that the AeroSwarm backend listens on.

The bridge uses its own ephemeral UDP source port to receive replies
from the backend, so it does not conflict with the backend's listeners.

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
BACKEND_PORTS = [14550, 14560, 14570, 14580, 14590]
BACKEND_HOST = "127.0.0.1"
BUFFER_SIZE = 2048
RECONNECT_DELAY = 3.0

# ── Bridge logic ────────────────────────────────────────────────────────────


def bridge_instance(instance_idx: int, tcp_port: int, backend_udp_port: int):
    label = f"[Bridge {instance_idx + 1}]"
    print(
        f"{label} Starting: SITL TCP {tcp_port} <-> Backend UDP {backend_udp_port}")

    while True:
        try:
            # Connect to SITL TCP
            tcp_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            tcp_sock.connect((SITL_HOST, tcp_port))
            tcp_sock.setblocking(False)
            print(f"{label} Connected to SITL TCP {tcp_port}")

            # UDP socket on ephemeral port — we send TO backend port,
            # and backend replies back to this ephemeral port.
            udp_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            udp_sock.bind(("0.0.0.0", 0))
            udp_sock.setblocking(False)
            local_udp_addr = udp_sock.getsockname()
            print(
                f"{label} UDP ephemeral {local_udp_addr} -> backend {backend_udp_port}")

            backend_endpoint = (BACKEND_HOST, backend_udp_port)

            while True:
                # TCP → UDP (SITL → backend)
                try:
                    data = tcp_sock.recv(BUFFER_SIZE)
                    if data:
                        udp_sock.sendto(data, backend_endpoint)
                    else:
                        break  # connection closed
                except BlockingIOError:
                    pass

                # UDP → TCP (backend → SITL)
                try:
                    data, addr = udp_sock.recvfrom(BUFFER_SIZE)
                    tcp_sock.sendall(data)
                except BlockingIOError:
                    pass

                time.sleep(0.001)

        except ConnectionRefusedError:
            print(
                f"{label} SITL TCP {tcp_port} not ready, retrying in {RECONNECT_DELAY}s...")
            time.sleep(RECONNECT_DELAY)
        except Exception as e:
            print(f"{label} Error: {e}, reconnecting in {RECONNECT_DELAY}s...")
            time.sleep(RECONNECT_DELAY)
        finally:
            try:
                tcp_sock.close()
            except Exception:
                pass
            try:
                udp_sock.close()
            except Exception:
                pass


def main():
    threads = []
    for i, udp_port in enumerate(BACKEND_PORTS):
        tcp_port = SITL_TCP_BASE + i * SITL_TCP_STEP
        t = threading.Thread(
            target=bridge_instance,
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

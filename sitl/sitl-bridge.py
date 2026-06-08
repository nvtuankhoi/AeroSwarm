#!/usr/bin/env python3
"""
AeroSwarm SITL Bridge — TCP-to-UDP proxy for Backend + QGroundControl.

SITL instances expose MAVLink on TCP console ports (5760, 5770, ...).
Bridge connects via TCP, sends GCS heartbeat to activate telemetry streams,
and forwards MAVLink v2 frames:
  SITL TCP <-> Bridge <-> Backend UDP (per-drone ports) + QGC UDP
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
QGC_PORT = 14551          # QGC listens here for telemetry
QGC_CMD_PORT = 15550      # We listen here for QGC commands
BUFFER_SIZE = 4096
RECONNECT_DELAY = 3.0

MAVLINK_V2_MAGIC = 0xFD
MAVLINK_V2_HEADER_LEN = 10

# Shared state: idx -> tcp_socket
tcp_sockets = {}
tcp_lock = threading.Lock()


_hb_seq = 0
_hb_lock = threading.Lock()


def build_gcs_heartbeat():
    """Build a MAVLink v2 GCS HEARTBEAT (sysid=255, compid=190)."""
    global _hb_seq
    with _hb_lock:
        seq = _hb_seq
        _hb_seq = (_hb_seq + 1) & 0xFF

    payload = bytes([0, 0, 0, 0, 6, 8, 0, 4, 3])  # 9 bytes
    msg_id = 0
    crc_extra = 50

    def crc_accum(crc, b):
        tmp = b ^ (crc & 0xFF)
        tmp ^= (tmp << 4) & 0xFF
        return (crc >> 8) ^ (tmp << 8) ^ (tmp << 3) ^ (tmp >> 4)

    frame = bytes([0xFD, len(payload), 0, 0, seq, 255, 190])
    frame += msg_id.to_bytes(3, 'little')
    frame += payload
    crc = 0xFFFF
    for b in frame[1:]:
        crc = crc_accum(crc, b)
    crc = crc_accum(crc, crc_extra)
    frame += (crc & 0xFFFF).to_bytes(2, 'little')
    return frame


def extract_frames(buf: bytearray):
    """Yield complete MAVLink v2 frames and consume them from buf."""
    while True:
        if len(buf) < MAVLINK_V2_HEADER_LEN + 2:
            return
        try:
            start = buf.index(MAVLINK_V2_MAGIC)
        except ValueError:
            buf.clear()
            return
        if len(buf) - start < MAVLINK_V2_HEADER_LEN + 2:
            del buf[:start]
            return
        payload_len = buf[start + 1]
        frame_len = MAVLINK_V2_HEADER_LEN + payload_len + 2
        if len(buf) - start < frame_len:
            del buf[:start]
            return
        yield bytes(buf[start:start + frame_len])
        del buf[start:start + frame_len]


def run_bridge(instance_idx: int, tcp_port: int, backend_udp_port: int):
    """Bridge one SITL instance over TCP."""
    label = f"[Bridge {instance_idx + 1}]"
    backend_endpoint = (BACKEND_HOST, backend_udp_port)
    qgc_endpoint = (QGC_HOST, QGC_PORT)

    print(f"{label} SITL TCP {tcp_port} -> Backend {backend_udp_port} + QGC {QGC_PORT}")

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
            udp_backend.setblocking(False)
            udp_qgc = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            buf = bytearray()

            while True:
                try:
                    data = tcp_sock.recv(BUFFER_SIZE)
                    if not data:
                        break
                    buf.extend(data)
                except socket.timeout:
                    pass

                # Forward complete frames to backend and QGC
                for frame in extract_frames(buf):
                    udp_backend.sendto(frame, backend_endpoint)
                    udp_qgc.sendto(frame, qgc_endpoint)

                # Backend replies -> SITL
                try:
                    data, addr = udp_backend.recvfrom(BUFFER_SIZE)
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
            with tcp_lock:
                tcp_sockets.pop(instance_idx, None)
            if tcp_sock:
                try:
                    tcp_sock.close()
                except Exception:
                    pass


def heartbeat_sender():
    """Send GCS heartbeat to all connected SITL instances at 1 Hz."""
    while True:
        hb = build_gcs_heartbeat()
        with tcp_lock:
            socks = list(tcp_sockets.values())
        for sock in socks:
            try:
                sock.sendall(hb)
            except Exception:
                pass
        time.sleep(1.0)


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

    # Start GCS heartbeat sender (required to activate SITL telemetry)
    t = threading.Thread(target=heartbeat_sender, daemon=True)
    t.start()
    threads.append(t)

    # Start QGC command listener
    t = threading.Thread(target=qgc_listener, daemon=True)
    t.start()
    threads.append(t)

    # Start bridge instances
    for i, (tcp_port, backend_port) in enumerate(zip(SITL_TCP_PORTS, BACKEND_PORTS)):
        t = threading.Thread(
            target=run_bridge,
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

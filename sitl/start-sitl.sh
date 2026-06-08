#!/bin/zsh
# Start 5 ArduCopter SITL instances for AeroSwarm on macOS
# Uses TCP console ports + Python bridge to forward to UDP for backend.
# Requires ArduPilot built locally. Run install-ardupilot-mac.sh first on macOS.

set -euo pipefail

LOCATION="${LOCATION:-CMAC}"
SCRIPT_DIR="${0:A:h}"
PROJECT_ROOT="${SCRIPT_DIR:h}"

# ArduCopter SITL binary path
ARDUCOPTER_BIN="${HOME}/ardupilot/build/sitl/bin/arducopter"
if [[ ! -f "$ARDUCOPTER_BIN" ]]; then
    echo "Error: arducopter binary not found at $ARDUCOPTER_BIN"
    echo ""
    echo "Build ArduPilot SITL first:"
    echo "  cd ~/ardupilot && ./waf configure --board sitl && ./waf build --target bin/arducopter"
    exit 1
fi

SYS_IDS=(4 5 6 7 8)
INSTANCE_IDS=(0 1 2 3 4)
SITL_TCP_PORTS=(5760 5770 5780 5790 5800)
BACKEND_PORTS=(14580 14590 14600 14610 14620)

PIDS=()

start_bridge() {
    echo ""
    echo "Starting SITL TCP→UDP bridge..."
    python3 "${PROJECT_ROOT}/sitl/sitl-bridge.py" &
    BRIDGE_PID=$!
    PIDS+=($BRIDGE_PID)
    sleep 2
    if ! kill -0 $BRIDGE_PID 2>/dev/null; then
        echo "Error: bridge failed to start."
        exit 1
    fi
    echo "Bridge running (PID $BRIDGE_PID)."
}

cleanup() {
    echo ""
    echo "Stopping SITL instances and bridge..."
    for pid in "${PIDS[@]}"; do
        if kill -0 "$pid" 2>/dev/null; then
            kill "$pid" 2>/dev/null || true
        fi
    done
    wait 2>/dev/null || true
    echo "All SITL instances stopped."
}
trap cleanup EXIT INT TERM

echo "Starting 5 ArduCopter SITL instances (location: $LOCATION, drones 4-8)..."
echo ""

for i in {1..5}; do
    idx=$((i))
    sysid=${SYS_IDS[$idx]}
    instance=${INSTANCE_IDS[$idx]}
    tcp_port=${SITL_TCP_PORTS[$idx]}

    echo "  - Drone $i: SYSID=$sysid, instance=$instance, TCP console=$tcp_port"
    mkdir -p "/tmp/sitl_instance_${instance}"
    cd "/tmp/sitl_instance_${instance}"
    nohup "$ARDUCOPTER_BIN" \
        --model + \
        --speedup 1 \
        --sysid "$sysid" \
        --slave 0 \
        --sim-address=127.0.0.1 \
        -I "$instance" \
        --home -35.363261,149.16523,584.0,353.0 \
        > "/tmp/sitl_instance_${instance}/ardu.log" 2>&1 &
    PIDS+=($!)
    cd - > /dev/null
    sleep 3
done

start_bridge

echo ""
echo "All SITL instances and bridge launched."
echo "Summary:"
printf "%-8s %-8s %-10s %-15s %-15s\n" "DRONE" "SYSID" "INSTANCE" "SITL TCP" "BACKEND UDP"
for i in {1..5}; do
    idx=$((i))
    printf "%-8s %-8s %-10s %-15s %-15s\n" \
        "$i" "${SYS_IDS[$idx]}" "${INSTANCE_IDS[$idx]}" \
        "${SITL_TCP_PORTS[$idx]}" "${BACKEND_PORTS[$idx]}"
done
echo ""
echo "Press Ctrl+C to stop all instances."

wait

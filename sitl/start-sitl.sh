#!/bin/zsh
# Start 5 ArduCopter SITL instances for AeroSwarm on macOS
# Uses TCP console ports + Python bridge to forward to UDP for backend.
# Requires ArduPilot built locally. Run install-ardupilot-mac.sh first on macOS.

set -euo pipefail

LOCATION="${LOCATION:-CMAC}"
SCRIPT_DIR="${0:A:h}"
PROJECT_ROOT="${SCRIPT_DIR:h}"

# Prefer local symlink created by install-ardupilot-mac.sh, then PATH
SIM_VEHICLE_CMD="${PROJECT_ROOT}/sitl/.bin/sim_vehicle.py"
if [[ ! -f "$SIM_VEHICLE_CMD" ]]; then
    if command -v sim_vehicle.py &>/dev/null; then
        SIM_VEHICLE_CMD="sim_vehicle.py"
    else
        echo "Error: sim_vehicle.py not found."
        echo ""
        echo "Install ArduPilot SITL first:"
        echo "  ./sitl/install-ardupilot-mac.sh"
        echo ""
        echo "Or add ArduPilot Tools/autotest to your PATH:"
        echo "  export PATH=\"\$HOME/ardupilot/Tools/autotest:\$PATH\""
        exit 1
    fi
fi

SYS_IDS=(1 2 3 4 5)
INSTANCE_IDS=(0 1 2 3 4)
SITL_TCP_PORTS=(5760 5770 5780 5790 5800)
BACKEND_PORTS=(14550 14560 14570 14580 14590)

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

echo "Starting 5 ArduCopter SITL instances (location: $LOCATION)..."
echo ""

for i in {1..5}; do
    idx=$((i))
    sysid=${SYS_IDS[$idx]}
    instance=${INSTANCE_IDS[$idx]}
    tcp_port=${SITL_TCP_PORTS[$idx]}

    echo "  - Drone $i: SYSID=$sysid, instance=$instance, TCP console=$tcp_port"
    "$SIM_VEHICLE_CMD" \
        -v ArduCopter \
        --sysid "$sysid" \
        -I "$instance" \
        --location "$LOCATION" \
        --no-mavproxy &

    PIDS+=($!)
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

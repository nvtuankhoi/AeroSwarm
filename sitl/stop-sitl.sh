#!/bin/zsh
# Stop all running SITL instances for AeroSwarm

set -euo pipefail

SCRIPT_DIR="${0:A:h}"

echo "Stopping SITL processes..."

# Kill native sim_vehicle.py processes
if pgrep -f "sim_vehicle.py" >/dev/null 2>&1; then
    pkill -f "sim_vehicle.py" || true
    echo "  - Killed sim_vehicle.py processes"
else
    echo "  - No sim_vehicle.py processes found"
fi

# Also kill any ardupilot SITL binaries that may be running
for bin in arducopter arduplane ardusub ardurover antennatracker; do
    if pgrep -x "$bin" >/dev/null 2>&1; then
        pkill -x "$bin" || true
        echo "  - Killed $bin processes"
    fi
done

# Stop Docker Compose if running
if [[ -f "$SCRIPT_DIR/docker-compose.yml" ]]; then
    if docker compose -f "$SCRIPT_DIR/docker-compose.yml" ps -q 2>/dev/null | grep -q .; then
        echo "  - Stopping Docker Compose SITL containers..."
        docker compose -f "$SCRIPT_DIR/docker-compose.yml" down
    else
        echo "  - No Docker Compose SITL containers running"
    fi
fi

echo ""
echo "All SITL instances stopped."

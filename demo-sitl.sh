#!/bin/zsh
# AeroSwarm SITL Demo Launcher (macOS)
# One command to start: SITL swarm → backend → frontend
#
# Usage:
#   ./demo-sitl.sh              # default: native SITL on macOS
#   ./demo-sitl.sh --native     # use native sim_vehicle.py (ArduPilot must be built)
#   ./demo-sitl.sh --docker     # use Docker (not recommended on macOS, see README)

set -euo pipefail

PROJECT_ROOT="${0:A:h}"
SITL_DIR="$PROJECT_ROOT/sitl"

# Default to native on macOS; allow --docker override
MODE="${1:-native}"
if [[ "$MODE" == "--docker" ]]; then MODE="docker"; fi
if [[ "$MODE" == "--native" ]]; then MODE="native"; fi

# Colors
BOLD=$'\e[1m'
RESET=$'\e[0m'
GREEN=$'\e[32m'
BLUE=$'\e[34m'
YELLOW=$'\e[33m'
RED=$'\e[31m'
CYAN=$'\e[36m'

log() {
    local level="$1"; shift
    local color
    case "$level" in
        info)  color="$BLUE" ;;
        ok)    color="$GREEN" ;;
        warn)  color="$YELLOW" ;;
        error) color="$RED" ;;
        step)  color="$CYAN" ;;
    esac
    printf "%s[%s]%s %s\n" "$color" "$level" "$RESET" "$*"
}

cleanup() {
    echo ""
    log warn "Shutting down demo..."
    [[ -n "${SITL_PID:-}" ]] && kill "$SITL_PID" 2>/dev/null || true
    [[ -n "${BE_PID:-}" ]]   && kill "$BE_PID" 2>/dev/null || true
    [[ -n "${FE_PID:-}" ]]   && kill "$FE_PID" 2>/dev/null || true
    if [[ "$MODE" == "docker" ]]; then
        (cd "$SITL_DIR" && docker compose down 2>/dev/null) || true
    fi
    log ok "Demo stopped."
}
trap cleanup EXIT INT TERM

# ─── Validate prerequisites ──────────────────────────────────────────────────
log step "AeroSwarm SITL Demo"

SIM_VEHICLE_CMD="${SITL_DIR}/.bin/sim_vehicle.py"
if [[ ! -f "$SIM_VEHICLE_CMD" ]] && ! command -v sim_vehicle.py &>/dev/null; then
    HAS_SIM_VEHICLE=0
else
    HAS_SIM_VEHICLE=1
fi

if [[ "$MODE" == "native" ]]; then
    if [[ "$HAS_SIM_VEHICLE" -eq 0 ]]; then
        log error "sim_vehicle.py not found."
        log info "Install ArduPilot SITL first: ./sitl/install-ardupilot-mac.sh"
        log info "Or run with --docker (not recommended for swarm on macOS)"
        exit 1
    fi
    log info "Using native SITL (sim_vehicle.py)"
else
    if ! command -v docker &>/dev/null; then
        log error "docker not found. Install Docker Desktop or use --native"
        exit 1
    fi
    log warn "Using Docker SITL. Note: on macOS, UDP replies to individual drones"
    log warn "may not route correctly due to Docker NAT. ARM/TAKEOFF/GOTO can be unreliable."
    log info "For a reliable demo on macOS, use: ./demo-sitl.sh --native"
fi

if ! command -v dotnet &>/dev/null; then
    log error "dotnet SDK not found."
    exit 1
fi

if ! command -v npm &>/dev/null; then
    log error "npm not found."
    exit 1
fi

# ─── Start SITL ──────────────────────────────────────────────────────────────
log step "Starting 5 ArduCopter SITL instances..."
if [[ "$MODE" == "native" ]]; then
    "$SITL_DIR/start-sitl.sh" &
    SITL_PID=$!
else
    (cd "$SITL_DIR" && docker compose up -d) &
    SITL_PID=$!
fi
wait "$SITL_PID"
SITL_PID=""

# Give SITL a moment to bind UDP ports
sleep 3
log ok "SITL swarm should be listening on UDP 14550/60/70/80/90"

# ─── Start Backend ───────────────────────────────────────────────────────────
log step "Starting AeroSwarm API (localhost:5501)..."
(cd "$PROJECT_ROOT/AeroSwarm.Api" && dotnet run --no-launch-profile --urls "http://localhost:5501") &
BE_PID=$!

# Wait for backend health
log info "Waiting for backend to be ready..."
for i in {1..60}; do
    if curl -sf http://localhost:5501/health >/dev/null 2>&1; then
        log ok "Backend is up"
        break
    fi
    sleep 1
done

if ! curl -sf http://localhost:5501/health >/dev/null 2>&1; then
    log error "Backend did not start within 60s"
    exit 1
fi

# ─── Start Frontend ──────────────────────────────────────────────────────────
log step "Starting AeroSwarm UI (localhost:5173)..."
(cd "$PROJECT_ROOT/aeroswarm-ui" && npm run dev) &
FE_PID=$!

# Wait for Vite
log info "Waiting for frontend to be ready..."
for i in {1..60}; do
    if curl -sf http://localhost:5173 >/dev/null 2>&1; then
        log ok "Frontend is up"
        break
    fi
    sleep 1
done

if ! curl -sf http://localhost:5173 >/dev/null 2>&1; then
    log error "Frontend did not start within 60s"
    exit 1
fi

# ─── Summary ─────────────────────────────────────────────────────────────────
echo ""
echo "${BOLD}${GREEN}╔════════════════════════════════════════════════════════════╗${RESET}"
echo "${BOLD}${GREEN}║           AeroSwarm SITL Demo is running!                  ║${RESET}"
echo "${BOLD}${GREEN}╚════════════════════════════════════════════════════════════╝${RESET}"
echo ""
log info "Dashboard:  ${BOLD}http://localhost:5173/${RESET}"
log info "Backend:    ${BOLD}http://localhost:5501/${RESET}"
log info "Swagger:    ${BOLD}http://localhost:5501/swagger${RESET}"
echo ""
log info "SITL drones:"
printf "  %-8s %-8s %-12s\n" "DRONE" "SYSID" "UDP PORT"
ports=(14550 14560 14570 14580 14590)
for i in {1..5}; do
    printf "  %-8s %-8s %-12s\n" "$i" "$i" "${ports[$i]}"
done
echo ""
log info "Optional: upload a demo mission to all drones"
log info "  python3 sitl/upload-mission.py"
echo ""
log warn "Press Ctrl+C to stop everything."

# Keep the script alive so cleanup works
wait

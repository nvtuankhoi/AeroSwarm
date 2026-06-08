#!/bin/zsh
# Install ArduPilot SITL natively on macOS for AeroSwarm demo
# This script clones, sets up prerequisites, and builds ArduPilot SITL.
# Estimated time: 15-40 minutes depending on machine and network.

set -euo pipefail

BOLD=$'\e[1m'
RESET=$'\e[0m'
GREEN=$'\e[32m'
BLUE=$'\e[34m'
YELLOW=$'\e[33m'
RED=$'\e[31m'

SCRIPT_DIR="${0:A:h}"
PROJECT_ROOT="${SCRIPT_DIR:h}"

# Default install location (adjust with ARDUPILOT_DIR env var)
ARDUPILOT_DIR="${ARDUPILOT_DIR:-$HOME/ardupilot}"

tip() { echo "${BLUE}[tip]${RESET} $*"; }
ok()  { echo "${GREEN}[ok]${RESET} $*"; }
warn() { echo "${YELLOW}[warn]${RESET} $*"; }
err()  { echo "${RED}[err]${RESET} $*" >&2; }

# ─── Pre-flight checks ───────────────────────────────────────────────────────
echo "${BOLD}AeroSwarm ArduPilot SITL Installer for macOS${RESET}"
echo "Target directory: ${ARDUPILOT_DIR}"
echo ""

if [[ "$(uname -s)" != "Darwin" ]]; then
    err "This script is only for macOS. Use Docker or install manually on Linux."
    exit 1
fi

if ! command -v git &>/dev/null; then
    err "git is required. Install Xcode Command Line Tools: xcode-select --install"
    exit 1
fi

if ! command -v python3 &>/dev/null; then
    err "python3 is required. Install from https://python.org or Homebrew."
    exit 1
fi

if ! command -v brew &>/dev/null; then
    warn "Homebrew not found. ArduPilot prereqs installer will likely fail."
    warn "Install Homebrew first: https://brew.sh"
    echo ""
    read -q "REPLY?Continue anyway? [y/N] "
    echo ""
    [[ "$REPLY" == "y" ]] || exit 1
fi

# ─── Clone ArduPilot ─────────────────────────────────────────────────────────
if [[ -d "$ARDUPILOT_DIR/.git" ]]; then
    tip "Existing ArduPilot repo found at $ARDUPILOT_DIR"
    tip "Pulling latest changes..."
    cd "$ARDUPILOT_DIR"
    git pull origin master || warn "git pull failed, continuing with local copy"
else
    tip "Cloning ArduPilot repository (this may take a few minutes)..."
    git clone --recurse-submodules https://github.com/ArduPilot/ardupilot.git "$ARDUPILOT_DIR"
    cd "$ARDUPILOT_DIR"
fi

# ─── Update submodules ───────────────────────────────────────────────────────
tip "Updating submodules..."
cd "$ARDUPILOT_DIR"
git submodule update --init --recursive || warn "Some submodules may have failed; continuing"

# ─── Install macOS prerequisites ─────────────────────────────────────────────
tip "Installing macOS prerequisites via ArduPilot's installer..."
tip "You may be prompted for your password by Homebrew."
if [[ -f "./Tools/environment_install/install-prereqs-mac.sh" ]]; then
    ./Tools/environment_install/install-prereqs-mac.sh
else
    err "install-prereqs-mac.sh not found. ArduPilot source may be incomplete."
    exit 1
fi

# Source the ArduPilot environment (adds waf, mavproxy, etc. to PATH)
if [[ -f "$HOME/.ardupilot_env" ]]; then
    source "$HOME/.ardupilot_env"
fi

# ─── Build SITL ──────────────────────────────────────────────────────────────
tip "Configuring SITL build..."
./waf distclean 2>/dev/null || true
./waf configure --board sitl

tip "Building ArduCopter SITL (this takes 10-30 minutes)..."
./waf copter

# ─── Verify ──────────────────────────────────────────────────────────────────
SIM_VEHICLE="$ARDUPILOT_DIR/Tools/autotest/sim_vehicle.py"
if [[ ! -f "$SIM_VEHICLE" ]]; then
    err "Build succeeded but sim_vehicle.py was not found at $SIM_VEHICLE"
    exit 1
fi

ok "ArduCopter SITL built successfully!"
ok "sim_vehicle.py: $SIM_VEHICLE"

# ─── Symlink into project for convenience ────────────────────────────────────
BIN_DIR="$PROJECT_ROOT/sitl/.bin"
mkdir -p "$BIN_DIR"
ln -sf "$SIM_VEHICLE" "$BIN_DIR/sim_vehicle.py"
ln -sf "$ARDUPILOT_DIR/build/sitl/bin/arducopter" "$BIN_DIR/arducopter"

# ─── PATH reminder ───────────────────────────────────────────────────────────
echo ""
echo "${BOLD}${GREEN}Installation complete!${RESET}"
echo ""
tip "Add this to your shell profile (e.g. ~/.zshrc) to use sim_vehicle.py globally:"
echo ""
echo "  export PATH=\"$ARDUPILOT_DIR/Tools/autotest:\$PATH\""
echo ""
tip "Or use the local symlink for this project:"
echo ""
echo "  export PATH=\"$BIN_DIR:\$PATH\""
echo ""
tip "Test with a single SITL instance:"
echo ""
echo "  sim_vehicle.py -v ArduCopter --console --map"
echo ""
tip "Start the full AeroSwarm SITL swarm:"
echo ""
echo "  ./sitl/start-sitl.sh"
echo "  ./demo-sitl.sh --native"
echo ""

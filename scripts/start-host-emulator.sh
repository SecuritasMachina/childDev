#!/usr/bin/env bash
# Starts the Android emulator on the host and enables ADB TCP for Docker container access.
# Run this BEFORE docker compose run mobile-test.
#
# Note: Android emulator 36.5.11 SIGSEGVs inside Docker containers on Fedora 43 / kernel 7.x
# (12 configurations tested: API levels 29/34, Ubuntu 20.04/22.04, GPU modes off/guest/swiftshader,
# KVM on/off, root/UID 1000). The emulator works fine on the host.

set -euo pipefail

ANDROID_SDK_ROOT="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-$HOME/android-sdk}}"
ADB="${ANDROID_SDK_ROOT}/platform-tools/adb"
EMULATOR="${ANDROID_SDK_ROOT}/emulator/emulator"
AVD="${1:-childdev_test}"
ADB_TCP_PORT=5555

log() { echo "[$(date '+%H:%M:%S')] $*"; }

if [ ! -x "$EMULATOR" ]; then
    echo "ERROR: Android emulator not found at $EMULATOR"
    echo "Set ANDROID_HOME or ANDROID_SDK_ROOT to your SDK path."
    exit 1
fi

# Kill any existing emulator
log "Stopping any running emulator..."
"$ADB" emu kill 2>/dev/null || true
sleep 2

# Start emulator headlessly
log "Starting emulator (AVD: $AVD)..."
ANDROID_SDK_ROOT="$ANDROID_SDK_ROOT" \
ANDROID_HOME="$ANDROID_SDK_ROOT" \
"$EMULATOR" \
    -avd "$AVD" \
    -no-window \
    -no-audio \
    -no-boot-anim \
    -gpu swiftshader_indirect \
    -no-snapshot \
    &

EMULATOR_PID=$!
log "Emulator PID: $EMULATOR_PID"

# Wait for boot
log "Waiting for emulator to boot (up to 3 minutes)..."
BOOT_TIMEOUT=180
ELAPSED=0
until "$ADB" shell getprop sys.boot_completed 2>/dev/null | grep -q "1"; do
    sleep 5
    ELAPSED=$((ELAPSED + 5))
    if ! kill -0 "$EMULATOR_PID" 2>/dev/null; then
        log "ERROR: Emulator process died."
        exit 1
    fi
    if [ $ELAPSED -ge $BOOT_TIMEOUT ]; then
        log "ERROR: Emulator did not boot within ${BOOT_TIMEOUT}s"
        exit 1
    fi
    log "  ...waiting (${ELAPSED}s / ${BOOT_TIMEOUT}s)"
done
log "Emulator booted!"

# Enable ADB TCP so the Docker container can connect
log "Enabling ADB TCP on port $ADB_TCP_PORT..."
"$ADB" tcpip $ADB_TCP_PORT
sleep 2

log ""
log "============================================================"
log "  Emulator is ready. Now run:"
log "    docker compose -f docker-compose.mobile-test.yml run --rm mobile-test"
log "============================================================"
log ""
log "  To run with custom monkey event count:"
log "    docker compose -f docker-compose.mobile-test.yml run --rm -e MONKEY_EVENTS=1000 mobile-test"

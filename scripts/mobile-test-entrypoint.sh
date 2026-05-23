#!/usr/bin/env bash
# Mobile CI pipeline: unit tests + APK build (always run)
# Emulator phases (install, logcat, monkey) run only when ENABLE_EMULATOR_TESTS=true
# and a host emulator is reachable via ADB TCP at HOST_ADB_PORT.
#
# Emulator note: Android emulator 36.x SIGSEGVs on Fedora 43 / Linux kernel 7.x
# due to a QEMU protected-range munmap bug (all GPU modes, all API levels tested).
# Enable emulator phases only on compatible hardware (older kernel, real VM, or
# once Google ships a fix).
set -euo pipefail

RESULTS_DIR="/results"
LOGCAT_FILE="${RESULTS_DIR}/logcat.txt"
MONKEY_LOG="${RESULTS_DIR}/monkey.txt"
UNIT_LOG="${RESULTS_DIR}/unit-tests.txt"
APK_PATH="/src/ChildDev.Mobile/bin/Debug/net8.0-android/levelup.securitasmachina.org-Signed.apk"
PACKAGE_NAME="levelup.securitasmachina.org"
MONKEY_EVENTS="${MONKEY_EVENTS:-500}"
HOST_ADB_PORT="${HOST_ADB_PORT:-5555}"
ENABLE_EMULATOR_TESTS="${ENABLE_EMULATOR_TESTS:-false}"
TEST_USER="androidtest"

mkdir -p "$RESULTS_DIR"
chown -R ${TEST_USER}:${TEST_USER} "$RESULTS_DIR" 2>/dev/null || true

log() { echo "[$(date '+%H:%M:%S')] $*"; }

# Run a command as the non-root androidtest user
as_test_user() { gosu "$TEST_USER" bash -c "$*"; }

# ── Phase 1: Unit tests ──────────────────────────────────────────────────────
log "=== PHASE 1: Unit Tests ==="
as_test_user "dotnet test \
    /src/ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj \
    /p:SkipMauiTargets=true \
    --verbosity normal \
    --logger 'trx;LogFileName=${RESULTS_DIR}/unit-results.trx'" \
    2>&1 | tee "$UNIT_LOG"

UNIT_EXIT=${PIPESTATUS[0]}
log "Unit test exit code: $UNIT_EXIT"

# ── Phase 2: Build APK ───────────────────────────────────────────────────────
log "=== PHASE 2: Build Android APK ==="
as_test_user "dotnet build /src/ChildDev.Mobile/LevelUp.csproj \
    -f net8.0-android \
    -c Debug \
    /p:JavaSdkDirectory=${JAVA_HOME}" \
    2>&1 | tee "${RESULTS_DIR}/build.txt"

BUILD_EXIT=${PIPESTATUS[0]}
log "Build exit code: $BUILD_EXIT"
if [ $BUILD_EXIT -ne 0 ]; then
    log "ERROR: APK build failed. Check ${RESULTS_DIR}/build.txt"
    exit $BUILD_EXIT
fi

MONKEY_EXIT=0
ERRORS_FOUND=0

if [ "$ENABLE_EMULATOR_TESTS" = "true" ]; then
    # ── Phase 3: Connect to host emulator via ADB TCP ────────────────────────
    log "=== PHASE 3: Connecting to host emulator ==="
    HOST_ADDR="host.docker.internal:${HOST_ADB_PORT}"
    log "Connecting to $HOST_ADDR ..."
    adb connect "$HOST_ADDR" 2>&1 | tee "${RESULTS_DIR}/adb-connect.txt"

    # ── Phase 4: Wait for device to be ready ─────────────────────────────────
    log "=== PHASE 4: Waiting for device ==="
    BOOT_TIMEOUT=120
    ELAPSED=0
    until adb -s "$HOST_ADDR" shell getprop sys.boot_completed 2>/dev/null | grep -q "1"; do
        sleep 5
        ELAPSED=$((ELAPSED + 5))
        if [ $ELAPSED -ge $BOOT_TIMEOUT ]; then
            log "ERROR: Device not ready within ${BOOT_TIMEOUT}s"
            log "ADB devices:"
            adb devices
            exit 1
        fi
        log "  ...waiting for device (${ELAPSED}s / ${BOOT_TIMEOUT}s)"
    done
    log "Device ready."
    export ANDROID_SERIAL="$HOST_ADDR"

    # Disable animations for stable monkey runs
    adb shell settings put global window_animation_scale 0 2>/dev/null || true
    adb shell settings put global transition_animation_scale 0 2>/dev/null || true
    adb shell settings put global animator_duration_scale 0 2>/dev/null || true

    # ── Phase 5: Install APK ──────────────────────────────────────────────────
    log "=== PHASE 5: Installing APK ==="
    [ ! -f "$APK_PATH" ] && APK_PATH="/src/ChildDev.Mobile/bin/Debug/net8.0-android/levelup.securitasmachina.org.apk"
    log "APK: $APK_PATH"
    adb install -r "$APK_PATH" 2>&1 | tee "${RESULTS_DIR}/install.txt"

    # ── Phase 6: Logcat ───────────────────────────────────────────────────────
    log "=== PHASE 6: Clear Logcat ==="
    adb logcat -c
    sleep 1
    adb logcat -v threadtime > "$LOGCAT_FILE" 2>&1 &
    LOGCAT_PID=$!
    log "Logcat PID: $LOGCAT_PID"
    sleep 2

    # ── Phase 7: Monkey runner ────────────────────────────────────────────────
    log "=== PHASE 7: Monkey Runner (${MONKEY_EVENTS} events) ==="
    adb shell monkey \
        -p "$PACKAGE_NAME" \
        --throttle 150 \
        --ignore-crashes \
        --ignore-timeouts \
        --ignore-security-exceptions \
        --monitor-native-crashes \
        -v -v \
        "$MONKEY_EVENTS" \
        2>&1 | tee "$MONKEY_LOG"

    MONKEY_EXIT=${PIPESTATUS[0]}
    log "Monkey exit code: $MONKEY_EXIT"

    sleep 3
    kill $LOGCAT_PID 2>/dev/null || true

    # ── Phase 8: Analyse results ──────────────────────────────────────────────
    log "=== PHASE 8: Analyzing Results ==="
    log "--- Crashes in logcat ---"
    if grep -E "AndroidRuntime|FATAL EXCEPTION|ANR in|Force finishing" "$LOGCAT_FILE" 2>/dev/null | tee "${RESULTS_DIR}/crashes.txt" | grep -q .; then
        log "WARNING: Crashes/ANRs detected — see ${RESULTS_DIR}/crashes.txt"
        ERRORS_FOUND=1
    else
        log "No crashes detected in logcat."
    fi

    log "--- Monkey crash summary ---"
    if grep -E "Crash|Exception|// CRASH" "$MONKEY_LOG" | tee "${RESULTS_DIR}/monkey-crashes.txt" | grep -q .; then
        log "WARNING: Monkey reported crashes — see ${RESULTS_DIR}/monkey-crashes.txt"
        ERRORS_FOUND=1
    else
        log "Monkey: no crash events."
    fi

    adb disconnect "$HOST_ADDR" 2>/dev/null || true
else
    log "=== Emulator phases skipped (ENABLE_EMULATOR_TESTS != true) ==="
    log "    Reason: Android emulator 36.x SIGSEGVs on Linux kernel 7.x (QEMU protected-range bug)."
    log "    To enable: set ENABLE_EMULATOR_TESTS=true and run scripts/start-host-emulator.sh first."
fi

log ""
log "=========================================="
log "  RESULTS SUMMARY"
log "=========================================="
log "  Unit tests:   $([ $UNIT_EXIT -eq 0 ] && echo 'PASS' || echo 'FAIL')"
log "  APK build:    $([ $BUILD_EXIT -eq 0 ] && echo 'PASS' || echo 'FAIL')"
if [ "$ENABLE_EMULATOR_TESTS" = "true" ]; then
    log "  Monkey ($MONKEY_EVENTS events): $([ $MONKEY_EXIT -eq 0 ] && echo 'PASS' || echo 'FAIL')"
    log "  Crash check:  $([ $ERRORS_FOUND -eq 0 ] && echo 'CLEAN' || echo 'ERRORS FOUND')"
else
    log "  Monkey/logcat: SKIPPED (emulator unavailable on kernel 7.x)"
fi
log "  Results dir:  $RESULTS_DIR"
log "=========================================="

OVERALL=0
[ $UNIT_EXIT -ne 0 ] && OVERALL=1
[ $BUILD_EXIT -ne 0 ] && OVERALL=1
exit $OVERALL

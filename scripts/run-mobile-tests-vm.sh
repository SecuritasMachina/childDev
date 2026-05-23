#!/usr/bin/env bash
# Full mobile test pipeline to run directly inside the Ubuntu VM (no Docker).
# Phases: unit tests → copy to local disk → APK build → emulator → install → logcat → monkey → analyze
#
# NOTE: Source is on vboxsf which silently corrupts NDK linker mmap writes (sparse files).
# The APK must be built on local ext4 (BUILD_DIR) to get valid native .so files.
set -euo pipefail

SRC_DIR="${SRC_DIR:-/src}"
BUILD_DIR="${BUILD_DIR:-$HOME/build-src}"
RESULTS_DIR="${RESULTS_DIR:-$HOME/test-results}"
ANDROID_SDK_ROOT="${ANDROID_SDK_ROOT:-$HOME/android-sdk}"
ANDROID_HOME="$ANDROID_SDK_ROOT"
JAVA_HOME="${JAVA_HOME:-/usr/lib/jvm/java-17-openjdk-amd64}"
AVD_NAME="${AVD_NAME:-childdev_test}"
PACKAGE_NAME="levelup.securitasmachina.org"
MONKEY_EVENTS="${MONKEY_EVENTS:-500}"
APK_PATH="$BUILD_DIR/ChildDev.Mobile/bin/Debug/net8.0-android/levelup.securitasmachina.org-Signed.apk"

export ANDROID_SDK_ROOT ANDROID_HOME JAVA_HOME
export PATH="$JAVA_HOME/bin:$ANDROID_SDK_ROOT/cmdline-tools/latest/bin:$ANDROID_SDK_ROOT/platform-tools:$ANDROID_SDK_ROOT/emulator:$PATH"

ADB="$ANDROID_SDK_ROOT/platform-tools/adb"
EMULATOR="$ANDROID_SDK_ROOT/emulator/emulator"

mkdir -p "$RESULTS_DIR"
log() { echo "[$(date '+%H:%M:%S')] $*"; }

# ── Phase 1: Unit tests (run from vboxsf — managed code only, no NDK) ────────
log "=== PHASE 1: Unit Tests ==="
dotnet test \
    "$SRC_DIR/ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj" \
    /p:SkipMauiTargets=true \
    --verbosity normal \
    --logger "trx;LogFileName=$RESULTS_DIR/unit-results.trx" \
    2>&1 | tee "$RESULTS_DIR/unit-tests.txt"
UNIT_EXIT=${PIPESTATUS[0]}
log "Unit test exit code: $UNIT_EXIT"

# ── Phase 2: Copy source to local disk ───────────────────────────────────────
log "=== PHASE 2: Copying source to local disk (avoids vboxsf NDK write corruption) ==="
mkdir -p "$BUILD_DIR"
rsync -a --delete \
    --exclude='bin/' \
    --exclude='obj/' \
    "$SRC_DIR/" "$BUILD_DIR/"
log "Source synced to $BUILD_DIR"

# ── Phase 3: Build APK ────────────────────────────────────────────────────────
log "=== PHASE 3: Build Android APK ==="
rm -rf "$BUILD_DIR/ChildDev.Mobile/bin" "$BUILD_DIR/ChildDev.Mobile/obj"
dotnet build "$BUILD_DIR/ChildDev.Mobile/ChildDev.Mobile.csproj" \
    -f net8.0-android \
    -c Debug \
    /p:JavaSdkDirectory="$JAVA_HOME" \
    /p:AndroidSupportedAbis=x86 \
    /p:EmbedAssembliesIntoApk=true \
    2>&1 | tee "$RESULTS_DIR/build.txt"
BUILD_EXIT=${PIPESTATUS[0]}
log "Build exit code: $BUILD_EXIT"
if [ $BUILD_EXIT -ne 0 ]; then
    log "ERROR: APK build failed."
    exit $BUILD_EXIT
fi

# Verify the APK is real (catches vboxsf sparse-file regression — build must run on local ext4)
APK_SIZE=$(stat -c %s "$APK_PATH")
if [ "$APK_SIZE" -lt 1000000 ]; then
    log "ERROR: APK too small (${APK_SIZE} bytes) — likely vboxsf write corruption"
    exit 1
fi
log "APK size OK: ${APK_SIZE} bytes"

# ── Phase 4: Start emulator ───────────────────────────────────────────────────
log "=== PHASE 4: Starting Android Emulator ==="
"$ADB" emu kill 2>/dev/null || true
pkill -f "Xvfb :99" 2>/dev/null || true
pkill -f "emulator" 2>/dev/null || true
sleep 2

Xvfb :99 -screen 0 1280x800x24 -ac +extension GLX +render &
XVFB_PID=$!
sleep 2

DISPLAY=:99 "$EMULATOR" \
    -avd "$AVD_NAME" \
    -no-window \
    -no-audio \
    -no-boot-anim \
    -gpu swiftshader_indirect \
    -no-snapshot \
    -no-metrics \
    > "$RESULTS_DIR/emulator.log" 2>&1 &
EMULATOR_PID=$!
log "Emulator PID: $EMULATOR_PID"

# ── Phase 5: Wait for boot ────────────────────────────────────────────────────
log "=== PHASE 5: Waiting for emulator boot (up to 10 min) ==="
BOOT_TIMEOUT=600
ELAPSED=0
until "$ADB" shell getprop sys.boot_completed 2>/dev/null | grep -q "1"; do
    sleep 10
    ELAPSED=$((ELAPSED + 10))
    if ! kill -0 "$EMULATOR_PID" 2>/dev/null; then
        log "ERROR: Emulator died. Last 30 lines of log:"
        tail -30 "$RESULTS_DIR/emulator.log"
        kill $XVFB_PID 2>/dev/null || true
        exit 1
    fi
    if [ $ELAPSED -ge $BOOT_TIMEOUT ]; then
        log "ERROR: Emulator did not boot within ${BOOT_TIMEOUT}s"
        tail -30 "$RESULTS_DIR/emulator.log"
        kill $EMULATOR_PID $XVFB_PID 2>/dev/null || true
        exit 1
    fi
    log "  ...waiting (${ELAPSED}s / ${BOOT_TIMEOUT}s)"
done
sleep 5
log "Emulator booted!"

# Disable animations
"$ADB" shell settings put global window_animation_scale 0 2>/dev/null || true
"$ADB" shell settings put global transition_animation_scale 0 2>/dev/null || true
"$ADB" shell settings put global animator_duration_scale 0 2>/dev/null || true

# ── Phase 6: Install APK ──────────────────────────────────────────────────────
log "=== PHASE 6: Installing APK ==="
log "APK: $APK_PATH"
"$ADB" uninstall "$PACKAGE_NAME" 2>/dev/null || true
"$ADB" install "$APK_PATH" 2>&1 | tee "$RESULTS_DIR/install.txt"

# ── Phase 7: Logcat ───────────────────────────────────────────────────────────
log "=== PHASE 7: Logcat ==="
LOGCAT_FILE="$RESULTS_DIR/logcat.txt"
"$ADB" logcat -c
sleep 1
"$ADB" logcat -v threadtime > "$LOGCAT_FILE" 2>&1 &
LOGCAT_PID=$!
log "Logcat PID: $LOGCAT_PID (streaming to $LOGCAT_FILE)"
sleep 2

# ── Phase 8: Monkey ───────────────────────────────────────────────────────────
log "=== PHASE 8: Monkey Runner (${MONKEY_EVENTS} events) ==="
set +e
"$ADB" shell monkey \
    -p "$PACKAGE_NAME" \
    --throttle 150 \
    --ignore-crashes \
    --ignore-timeouts \
    --ignore-security-exceptions \
    --monitor-native-crashes \
    -v -v \
    "$MONKEY_EVENTS" \
    2>&1 | tee "$RESULTS_DIR/monkey.txt"
MONKEY_EXIT=${PIPESTATUS[0]}
set -e
log "Monkey exit code: $MONKEY_EXIT"

sleep 3
kill $LOGCAT_PID 2>/dev/null || true

# ── Phase 9: Analyze results ──────────────────────────────────────────────────
log "=== PHASE 9: Analyzing Results ==="
ERRORS_FOUND=0

log "--- Crash check ---"
if grep -E "AndroidRuntime|FATAL EXCEPTION|ANR in|Force finishing" "$LOGCAT_FILE" 2>/dev/null \
        | grep "levelup.securitasmachina.org" \
        | tee "$RESULTS_DIR/crashes.txt" | grep -q .; then
    log "WARNING: App crashes/ANRs in logcat — see $RESULTS_DIR/crashes.txt"
    ERRORS_FOUND=1
else
    log "No app crashes in logcat."
fi

if grep -E "// CRASH.*levelup|levelup.*CRASH" "$RESULTS_DIR/monkey.txt" \
        | tee "$RESULTS_DIR/monkey-crashes.txt" | grep -q .; then
    log "WARNING: Monkey reported app crashes."
    ERRORS_FOUND=1
else
    log "Monkey: clean."
fi

# Cleanup
kill $EMULATOR_PID $XVFB_PID 2>/dev/null || true

log ""
log "=========================================="
log "  RESULTS SUMMARY"
log "=========================================="
log "  Unit tests:   $([ $UNIT_EXIT -eq 0 ] && echo 'PASS' || echo 'FAIL')"
log "  APK build:    $([ $BUILD_EXIT -eq 0 ] && echo 'PASS' || echo 'FAIL')"
log "  Monkey ($MONKEY_EVENTS events): $([ $MONKEY_EXIT -eq 0 ] && echo 'PASS' || echo 'FAIL')"
log "  Crash check:  $([ $ERRORS_FOUND -eq 0 ] && echo 'CLEAN' || echo 'ERRORS FOUND')"
log "  Results:      $RESULTS_DIR"
log "=========================================="

OVERALL=0
[ $UNIT_EXIT -ne 0 ] && OVERALL=1
[ $BUILD_EXIT -ne 0 ] && OVERALL=1
exit $OVERALL

#!/usr/bin/env bash
# mobile-release-entrypoint.sh — produce a SIGNED Android release AAB + APK.
#
# Runs inside Dockerfile.mobile-release. Expects (mounted/injected at runtime):
#   /keystore/levelup-release.keystore   (read-only mount)
#   LEVELUP_KEY_ALIAS / LEVELUP_KEYSTORE_PASS / LEVELUP_KEY_PASS   (--env-file)
#   /out                                 (host-mounted output dir)
# Signed AAB+APK are copied to /out with SHA256SUMS. Passwords are passed to
# msbuild via env: indirection so they never appear in the process command line.
set -euo pipefail

PROJECT="/src/ChildDev.Mobile/LevelUp.csproj"
OUT_DIR="/src/ChildDev.Mobile/bin/Release/net8.0-android"
KEYSTORE="/keystore/levelup-release.keystore"
APP_ID="levelup.securitasmachina.org"
DEST="/out"
MIN_BYTES=$((1024 * 1024))   # 1 MB sanity guard

log() { echo "[$(date '+%H:%M:%S')] $*"; }

[[ -f "$KEYSTORE" ]] || { log "ERROR: keystore not mounted at $KEYSTORE"; exit 1; }
: "${LEVELUP_KEY_ALIAS:?missing LEVELUP_KEY_ALIAS}"
: "${LEVELUP_KEYSTORE_PASS:?missing LEVELUP_KEYSTORE_PASS}"
: "${LEVELUP_KEY_PASS:?missing LEVELUP_KEY_PASS}"

log "Cleaning stale build output ..."
rm -rf /src/ChildDev.Mobile/bin /src/ChildDev.Mobile/obj

log "Publishing SIGNED release (AAB + APK) ..."
dotnet publish "$PROJECT" \
  -f net8.0-android \
  -c Release \
  /p:JavaSdkDirectory="$JAVA_HOME" \
  /p:AndroidSdkDirectory="$ANDROID_SDK_ROOT" \
  /p:AndroidKeyStore=true \
  /p:AndroidSigningKeyStore="$KEYSTORE" \
  /p:AndroidSigningKeyAlias="$LEVELUP_KEY_ALIAS" \
  /p:AndroidSigningStorePass=env:LEVELUP_KEYSTORE_PASS \
  /p:AndroidSigningKeyPass=env:LEVELUP_KEY_PASS \
  /p:AndroidPackageFormats=aab%3Bapk \
  /p:AndroidCreatePackagePerAbi=false \
  --nologo

AAB="$OUT_DIR/${APP_ID}-Signed.aab"
APK="$OUT_DIR/${APP_ID}-Signed.apk"
# Some SDK versions emit the AAB without the -Signed suffix even when signed.
[[ -f "$AAB" ]] || AAB="$OUT_DIR/${APP_ID}.aab"

for f in "$AAB" "$APK"; do
  [[ -f "$f" ]] || { log "ERROR: expected artifact missing: $f"; ls -la "$OUT_DIR" || true; exit 1; }
  sz=$(stat -c%s "$f")
  (( sz >= MIN_BYTES )) || { log "ERROR: $f suspiciously small ($sz bytes)"; exit 1; }
done

mkdir -p "$DEST"
cp -f "$AAB" "$DEST/LevelUp-release.aab"
cp -f "$APK" "$DEST/LevelUp-release.apk"
( cd "$DEST" && sha256sum LevelUp-release.aab LevelUp-release.apk > LevelUp-release.SHA256SUMS )

log "=== artifacts staged to $DEST ==="
ls -la "$DEST"/LevelUp-release.*
log "Done."

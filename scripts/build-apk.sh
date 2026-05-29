#!/usr/bin/env bash
# build-apk.sh — clean-build the Android APK and stage it for distribution.
#
# Why this exists: incremental MAUI/Android builds can silently keep stale C#,
# producing an APK that doesn't reflect recent source changes. The deploy scripts
# (deploy2web.sh / deploy.sh) only UPLOAD a pre-built APK with a size guard — they
# never rebuild it — so a stale APK sails straight through to users. This script is
# the single, reproducible source of the distributed APK: it always cleans first,
# builds, verifies, and copies the result to wwwroot/downloads/LevelUp.apk.
#
# Usage:
#   ./scripts/build-apk.sh                 # Release build (default), stage for deploy
#   CONFIG=Debug ./scripts/build-apk.sh    # Debug build (embeds assemblies for sideload)
#
# Overridable env (the system dotnet can't build net8.0-android — it errors on the
# wasi-experimental workload — so DOTNET defaults to the MAUI install if present):
#   DOTNET, JAVA_HOME, ANDROID_SDK_ROOT, CONFIG
set -euo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

PROJECT="$ROOT_DIR/ChildDev.Mobile/LevelUp.csproj"
MOBILE_DIR="$ROOT_DIR/ChildDev.Mobile"
CONFIG="${CONFIG:-Release}"
DEST="$ROOT_DIR/ChildDev.Api/wwwroot/downloads/LevelUp.apk"
APK_MIN_BYTES=$((1024 * 1024))   # 1 MB guard — a real APK is always larger

# Toolchain — override via env if your paths differ.
DOTNET="${DOTNET:-}"
if [[ -z "$DOTNET" ]]; then
  if [[ -x "$HOME/.dotnet/dotnet" ]]; then DOTNET="$HOME/.dotnet/dotnet"; else DOTNET="dotnet"; fi
fi
JAVA_HOME="${JAVA_HOME:-/usr/lib/jvm/java-21}"
ANDROID_SDK_ROOT="${ANDROID_SDK_ROOT:-$HOME/android-sdk}"
export JAVA_HOME ANDROID_SDK_ROOT ANDROID_HOME="$ANDROID_SDK_ROOT"

log_info()  { printf '[INFO] %s\n' "$*"; }
log_error() { printf '[ERROR] %s\n' "$*" >&2; }

# Debug APKs crash on sideload ("No assemblies found") unless assemblies are embedded.
# Release embeds by default. Only force it for Debug.
EMBED_ARG=()
[[ "$CONFIG" == "Debug" ]] && EMBED_ARG=(/p:EmbedAssembliesIntoApk=true)

log_info "dotnet:  $DOTNET"
log_info "config:  $CONFIG"
log_info "project: $PROJECT"

# ── clean ────────────────────────────────────────────────────────────────────
log_info "Cleaning bin/ obj/ (avoids stale incremental output) ..."
rm -rf "$MOBILE_DIR/bin" "$MOBILE_DIR/obj"

# ── build ────────────────────────────────────────────────────────────────────
log_info "Building Android APK ..."
"$DOTNET" build "$PROJECT" \
  -f net8.0-android \
  -c "$CONFIG" \
  /p:JavaSdkDirectory="$JAVA_HOME" \
  "${EMBED_ARG[@]}" \
  --nologo

# ── locate the signed APK ─────────────────────────────────────────────────────
OUT_DIR="$MOBILE_DIR/bin/$CONFIG/net8.0-android"
APK_BUILT="$OUT_DIR/levelup.securitasmachina.org-Signed.apk"
if [[ ! -f "$APK_BUILT" ]]; then
  # Fall back to the unsigned name if signing produced no -Signed variant.
  APK_BUILT="$OUT_DIR/levelup.securitasmachina.org.apk"
fi
if [[ ! -f "$APK_BUILT" ]]; then
  log_error "No APK found in $OUT_DIR after build."
  exit 1
fi

# ── verify size ───────────────────────────────────────────────────────────────
APK_BYTES=$(stat -c%s "$APK_BUILT")
if (( APK_BYTES < APK_MIN_BYTES )); then
  log_error "APK is suspiciously small ($APK_BYTES bytes < 1 MB). Refusing to stage."
  log_error "File: $APK_BUILT"
  exit 1
fi

# ── stage for deploy ──────────────────────────────────────────────────────────
mkdir -p "$(dirname "$DEST")"
cp -f "$APK_BUILT" "$DEST"
log_info "Built:  $APK_BUILT ($APK_BYTES bytes)"
log_info "Staged: $DEST"
log_info "Ready to deploy:  ./scripts/deploy2web.sh"

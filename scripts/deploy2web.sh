#!/usr/bin/env bash
# deploy2web.sh — deploy source + APK to the Hostwinds VPS (levelup.securitasmachina.org)
# Usage:
#   ./scripts/deploy2web.sh              # hot-deploy (default)
#   FORCE_REBUILD=1 ./scripts/deploy2web.sh   # full image rebuild
set -euo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

SSH_KEY="/home/jaxtrx/.ssh/hostWinds_id_rsa"
SSH_HOST="root@hwsrv-1313060.hostwindsdns.com"
REMOTE_DIR="/opt/childdev"
SECRETS_FILE="/home/jaxtrx/data/.secrets/childdev-prod.env"

APK_LOCAL="$ROOT_DIR/ChildDev.Api/wwwroot/downloads/LevelUp.apk"
APK_MIN_BYTES=$((1024 * 1024))   # 1 MB guard — a real APK is always larger

FORCE_REBUILD="${FORCE_REBUILD:-0}"
SKIP_TESTS="${SKIP_TESTS:-0}"

PROJECT_NAME="childdev"
SERVICE_NAME="childdev-api"
COMPOSE_FILE="$REMOTE_DIR/docker-compose.yml"

log_info()  { printf '[INFO] %s\n' "$*"; }
log_warn()  { printf '[WARN] %s\n' "$*" >&2; }
log_error() { printf '[ERROR] %s\n' "$*" >&2; }

ssh_run() {
  ssh -i "$SSH_KEY" -o StrictHostKeyChecking=no -o ConnectTimeout=30 "$SSH_HOST" "$@"
}

# ── pre-flight checks ────────────────────────────────────────────────────────

if [[ ! -f "$SECRETS_FILE" ]]; then
  log_error "Secrets file not found: $SECRETS_FILE"
  exit 1
fi

if [[ ! -f "$APK_LOCAL" ]]; then
  log_error "APK not found: $APK_LOCAL"
  log_error "Build the Android APK first, then re-run this script."
  exit 1
fi

APK_BYTES=$(stat -c%s "$APK_LOCAL")
if (( APK_BYTES < APK_MIN_BYTES )); then
  log_error "APK is suspiciously small ($APK_BYTES bytes < 1 MB). Refusing to deploy."
  log_error "File: $APK_LOCAL"
  exit 1
fi

log_info "APK OK: $APK_LOCAL ($APK_BYTES bytes)"

# ── tests ────────────────────────────────────────────────────────────────────

if [[ "$SKIP_TESTS" != "1" ]]; then
  log_info "Running API tests (including download endpoint) ..."
  dotnet test "$ROOT_DIR/ChildDev.Api.Tests/ChildDev.Api.Tests.csproj" \
    --no-restore -v minimal --logger "console;verbosity=normal"
  log_info "All tests passed."
fi

# ── sync source (without APK — APK is uploaded separately below) ─────────────

log_info "Syncing source to $SSH_HOST:$REMOTE_DIR ..."
rsync -az --delete \
  --exclude='.git' \
  --exclude='*/bin/' \
  --exclude='*/obj/' \
  --exclude='test-results/' \
  --exclude='*.apk' \
  -e "ssh -i $SSH_KEY -o StrictHostKeyChecking=no" \
  "$ROOT_DIR/" \
  "$SSH_HOST:$REMOTE_DIR/"

# ── secrets ──────────────────────────────────────────────────────────────────

log_info "Syncing secrets ..."
scp -i "$SSH_KEY" -o StrictHostKeyChecking=no -o ConnectTimeout=30 \
  "$SECRETS_FILE" \
  "$SSH_HOST:$REMOTE_DIR/.env"

# ── APK upload ───────────────────────────────────────────────────────────────
# The VPS docker-compose mounts /opt/childdev/downloads → /app/wwwroot/downloads
# so the APK is served directly without a container rebuild.

log_info "Uploading LevelUp.apk ($APK_BYTES bytes) ..."
ssh_run "mkdir -p $REMOTE_DIR/downloads"
scp -i "$SSH_KEY" -o StrictHostKeyChecking=no -o ConnectTimeout=30 \
  "$APK_LOCAL" \
  "$SSH_HOST:$REMOTE_DIR/downloads/LevelUp.apk"
log_info "APK uploaded → /opt/childdev/downloads/LevelUp.apk"

# ── rebuild / hot-deploy ─────────────────────────────────────────────────────

if [[ "$FORCE_REBUILD" == "1" ]]; then
  log_info "FORCE_REBUILD=1: full image rebuild + stack up"
  ssh_run "
    cd $REMOTE_DIR
    docker compose -p $PROJECT_NAME -f $COMPOSE_FILE build $SERVICE_NAME
    docker compose -p $PROJECT_NAME -f $COMPOSE_FILE up -d
  "
else
  log_info "Hot deploy: publish locally, copy to container, restart"
  PUBLISH_DIR="/tmp/childdev-prod-hotdeploy"
  rm -rf "$PUBLISH_DIR"
  dotnet publish "$ROOT_DIR/ChildDev.Api/ChildDev.Api.csproj" \
    -c Release -o "$PUBLISH_DIR" /p:UseAppHost=false --nologo -v minimal

  CONTAINER=$(ssh_run "docker ps --filter name=childdev-childdev-api --format '{{.Names}}' | head -1")
  if [[ -z "$CONTAINER" ]]; then
    log_warn "No running container — falling back to full rebuild"
    ssh_run "
      cd $REMOTE_DIR
      docker compose -p $PROJECT_NAME -f $COMPOSE_FILE build $SERVICE_NAME
      docker compose -p $PROJECT_NAME -f $COMPOSE_FILE up -d
    "
  else
    rsync -az --delete \
      -e "ssh -i $SSH_KEY -o StrictHostKeyChecking=no" \
      "$PUBLISH_DIR/" \
      "$SSH_HOST:/tmp/childdev-hotdeploy/"
    ssh_run "docker cp /tmp/childdev-hotdeploy/. $CONTAINER:/app/ && docker restart $CONTAINER"
  fi
  rm -rf "$PUBLISH_DIR"
fi

# ── status ───────────────────────────────────────────────────────────────────

log_info "Container status:"
ssh_run "docker ps --filter name=childdev-childdev-api --format 'table {{.Names}}\t{{.Status}}'"

printf '\n'
log_info "App:  https://levelup.securitasmachina.org"
log_info "APK:  https://levelup.securitasmachina.org/downloads/LevelUp.apk"

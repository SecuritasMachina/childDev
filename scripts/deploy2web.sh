#!/usr/bin/env bash
# deploy2web.sh — build + deploy the APK and web app to the Hostwinds VPS
#                 (levelup.securitasmachina.org)
# By default this clean-builds the Android APK (via build-apk.sh) AND builds the
# web app (dotnet publish on hot-deploy, or docker build on FORCE_REBUILD).
# Usage:
#   ./scripts/deploy2web.sh                      # build APK + web, hot-deploy (default)
#   FORCE_REBUILD=1 ./scripts/deploy2web.sh      # full web image rebuild
#   SKIP_APK_BUILD=1 ./scripts/deploy2web.sh     # reuse existing APK, don't rebuild it
#   APK_BUILD_CONFIG=Debug ./scripts/deploy2web.sh   # build APK in Debug (default Release)
#   SKIP_TESTS=1 ./scripts/deploy2web.sh         # skip the API test run
set -euo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

SSH_KEY="/home/jaxtrx/.ssh/hostWinds_id_rsa"
SSH_HOST="root@hwsrv-1313060.hostwindsdns.com"
REMOTE_DIR="/opt/childdev"
# APK lives outside REMOTE_DIR (the rsync/hot-deploy tree) so source syncs and
# `docker cp` into the API container can never touch it. Both the API and nginx
# download containers bind-mount this dir (see docker-compose.yml).
DOWNLOADS_DIR="/opt/downloads"
SECRETS_FILE="/home/jaxtrx/data/.secrets/childdev-prod.env"

APK_LOCAL="$ROOT_DIR/ChildDev.Api/wwwroot/downloads/LevelUp.apk"
APK_MIN_BYTES=$((1024 * 1024))   # 1 MB guard — a real APK is always larger

FORCE_REBUILD="${FORCE_REBUILD:-0}"
SKIP_TESTS="${SKIP_TESTS:-0}"
SKIP_APK_BUILD="${SKIP_APK_BUILD:-0}"
APK_BUILD_CONFIG="${APK_BUILD_CONFIG:-Release}"

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

# ── build APK (default) ──────────────────────────────────────────────────────
# Clean-builds the Android APK and stages it at $APK_LOCAL. Always rebuild by
# default so a stale incremental APK can't be shipped; SKIP_APK_BUILD=1 reuses
# whatever is already staged.
if [[ "$SKIP_APK_BUILD" != "1" ]]; then
  log_info "Building Android APK (CONFIG=$APK_BUILD_CONFIG) ..."
  CONFIG="$APK_BUILD_CONFIG" "$SCRIPT_DIR/build-apk.sh"
else
  log_warn "SKIP_APK_BUILD=1 — reusing existing APK, not rebuilding."
fi

if [[ ! -f "$APK_LOCAL" ]]; then
  log_error "APK not found: $APK_LOCAL"
  log_error "Run scripts/build-apk.sh first, or unset SKIP_APK_BUILD."
  exit 1
fi

APK_BYTES=$(stat -c%s "$APK_LOCAL")
if (( APK_BYTES < APK_MIN_BYTES )); then
  log_error "APK is suspiciously small ($APK_BYTES bytes < 1 MB). Refusing to deploy."
  log_error "File: $APK_LOCAL"
  exit 1
fi

log_info "APK OK: $APK_LOCAL ($APK_BYTES bytes)"

# ── regression tests (gate) ──────────────────────────────────────────────────
# Runs the API and mobile regression suites. On ANY failure the operator is
# alerted and prompted whether to continue; the default — including a
# non-interactive shell (no TTY / EOF) — is NO, which aborts the deploy.
# SKIP_TESTS=1 bypasses the gate entirely (not recommended).

# Each suite is run with failures captured (so `set -e` doesn't abort before the prompt).
run_regression_tests() {
  local rc=0

  log_info "Running API regression tests (ChildDev.Api.Tests) ..."
  if ! dotnet test "$ROOT_DIR/ChildDev.Api.Tests/ChildDev.Api.Tests.csproj" \
        --no-restore -v minimal --logger "console;verbosity=normal"; then
    log_error "API regression suite FAILED."
    rc=1
  fi

  log_info "Running mobile regression tests (ChildDev.Mobile.Tests) ..."
  if ! MSBuildEnableWorkloadResolver=false dotnet test \
        "$ROOT_DIR/ChildDev.Mobile.Tests/ChildDev.Mobile.Tests.csproj" \
        /p:SkipMauiTargets=true -v minimal --logger "console;verbosity=normal"; then
    log_error "Mobile regression suite FAILED."
    rc=1
  fi

  return $rc
}

if [[ "$SKIP_TESTS" == "1" ]]; then
  log_warn "SKIP_TESTS=1 — skipping regression tests (NOT recommended)."
elif run_regression_tests; then
  log_info "All regression tests passed."
else
  log_error "=================================================================="
  log_error "REGRESSION TESTS FAILED — see output above."
  log_error "Deploying now would push code that does not pass its own tests."
  log_error "=================================================================="
  reply=""
  if [[ -t 0 ]]; then
    read -r -p "$(printf '[ERROR] Continue deploying despite FAILED tests? [y/N] ')" reply || reply=""
  else
    log_error "Non-interactive shell — cannot prompt; defaulting to NO."
  fi
  case "${reply,,}" in
    y|yes)
      log_warn "Operator override: proceeding with deploy despite FAILED regression tests." ;;
    *)
      log_error "Aborting deploy due to failed regression tests."
      exit 1 ;;
  esac
fi

# ── sync source (without APK — APK is uploaded separately below) ─────────────

log_info "Syncing source to $SSH_HOST:$REMOTE_DIR ..."
rsync -avz --delete --progress --stats \
  --exclude='.git/' \
  --exclude='bin/' \
  --exclude='obj/' \
  --exclude='node_modules/' \
  --exclude='test-results/' \
  --exclude='downloads/' \
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
# Uploaded to $DOWNLOADS_DIR, which both containers bind-mount, so the APK is
# served directly without a container rebuild.

log_info "Uploading LevelUp.apk ($APK_BYTES bytes) ..."
ssh_run "mkdir -p $DOWNLOADS_DIR"
scp -i "$SSH_KEY" -o StrictHostKeyChecking=no -o ConnectTimeout=30 \
  "$APK_LOCAL" \
  "$SSH_HOST:$DOWNLOADS_DIR/LevelUp.apk"
log_info "APK uploaded → $DOWNLOADS_DIR/LevelUp.apk"

# Upload downloads index page (excluded from main rsync)
INDEX_LOCAL="$ROOT_DIR/ChildDev.Api/wwwroot/downloads/index.html"
if [[ -f "$INDEX_LOCAL" ]]; then
  scp -i "$SSH_KEY" -o StrictHostKeyChecking=no -o ConnectTimeout=30 \
    "$INDEX_LOCAL" \
    "$SSH_HOST:$DOWNLOADS_DIR/index.html"
  log_info "Downloads index page uploaded → $DOWNLOADS_DIR/index.html"
fi

# ── restart downloads container to re-bind the volume mount ──────────────────
# rsync --delete can recreate the downloads dir (new inode); the running nginx
# container must be restarted so Docker re-establishes the bind mount.
log_info "Restarting downloads container to refresh volume bind ..."
ssh_run "docker restart childdev-downloads-1"

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
  BUILD_TS="$(TZ='America/New_York' date +'%Y-%m-%d %I:%M %p ET')"
  sed -i "s|public const string BuildTimestamp = .*|public const string BuildTimestamp = \"$BUILD_TS\";|" \
    "$ROOT_DIR/ChildDev.Api/BuildInfo.cs"
  log_info "Build timestamp stamped: $BUILD_TS"
  PUBLISH_DIR="$(mktemp -d /tmp/childdev-hotdeploy-XXXXXX)"
  trap 'rm -rf "$PUBLISH_DIR" 2>/dev/null || true' EXIT
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
    rsync -avz --delete --progress --stats \
      --exclude='wwwroot/downloads/LevelUp.apk' \
      -e "ssh -i $SSH_KEY -o StrictHostKeyChecking=no" \
      "$PUBLISH_DIR/" \
      "$SSH_HOST:/tmp/childdev-hotdeploy/"
    # The APK is excluded from the rsync above (it's uploaded separately via scp to
    # the bind-mounted downloads dir). But --delete does NOT remove excluded files,
    # so a stale APK from an earlier deploy lingers in this persistent staging dir —
    # and the docker cp below would copy it back over the freshly-uploaded one. Drop
    # it first so docker cp never carries an APK and the scp'd file stays authoritative.
    ssh_run "rm -f /tmp/childdev-hotdeploy/wwwroot/downloads/LevelUp.apk"
    ssh_run "docker cp /tmp/childdev-hotdeploy/. $CONTAINER:/app/ && docker restart $CONTAINER"
  fi
fi

# ── status ───────────────────────────────────────────────────────────────────

log_info "Container status:"
ssh_run "docker ps --filter name=childdev-childdev-api --format 'table {{.Names}}\t{{.Status}}'"

printf '\n'
log_info "App:  https://levelup.securitasmachina.org"
log_info "APK:  https://levelup.securitasmachina.org/downloads/LevelUp.apk"

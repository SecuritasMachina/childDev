#!/usr/bin/env bash
set -euo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

COMPOSE_FILE="${COMPOSE_FILE:-$ROOT_DIR/docker-compose.yml}"
PROJECT_NAME="${PROJECT_NAME:-childdev}"
SERVICE_NAME="childdev-api"
CONTAINER_NAME="childdev-api"
PUBLISH_DIR="${PUBLISH_DIR:-/tmp/childdev-prod-hotdeploy}"
SECRETS_FILE="/home/jaxtrx/data/.secrets/childdev-prod.env"

# --- Deployment mode flags ---
# Default: hot deploy (dotnet publish on host, docker cp into running container, restart).
# FORCE_REBUILD=1  — full Docker image rebuild (use when Dockerfile or base image changed).
# SKIP_BUILD=1     — restart the container only, no compile step.
FORCE_REBUILD="${FORCE_REBUILD:-0}"
SKIP_BUILD="${SKIP_BUILD:-0}"
NO_CACHE="${NO_CACHE:-0}"

log_info()  { printf '[INFO] %s\n' "$*"; }
log_warn()  { printf '[WARN] %s\n' "$*" >&2; }
log_error() { printf '[ERROR] %s\n' "$*" >&2; }

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    log_error "Missing required command: $1"
    exit 1
  fi
}

container_running() {
  docker container inspect "$CONTAINER_NAME" >/dev/null 2>&1
}

clear_container_logs() {
  local log_path
  if ! docker container inspect "$1" >/dev/null 2>&1; then return 0; fi
  log_path="$(docker inspect --format='{{.LogPath}}' "$1" 2>/dev/null || true)"
  if [[ -z "$log_path" || ! -f "$log_path" ]]; then return 0; fi
  log_info "Clearing Docker logs for $1"
  truncate -s 0 "$log_path" 2>/dev/null || sudo truncate -s 0 "$log_path"
}

remove_stale_container() {
  if ! docker container inspect "$CONTAINER_NAME" >/dev/null 2>&1; then return 0; fi
  log_warn "Removing stale container $CONTAINER_NAME."
  docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || {
    log_error "Failed to remove stale container."
    exit 1
  }
}

compose_cmd() {
  docker compose -p "$PROJECT_NAME" -f "$COMPOSE_FILE" --env-file "$SECRETS_FILE" "$@"
}

do_hot_deploy() {
  require_command dotnet

  clear_container_logs "$CONTAINER_NAME"

  log_info "Hot deploy: publishing to $PUBLISH_DIR ..."
  if [[ -d "$PUBLISH_DIR" ]]; then
    chmod -R u+w "$PUBLISH_DIR" 2>/dev/null || sudo rm -rf "$PUBLISH_DIR"
  fi
  rm -rf "$PUBLISH_DIR"

  dotnet publish "$ROOT_DIR/ChildDev.Api/ChildDev.Api.csproj" -c Release -o "$PUBLISH_DIR" \
    /p:UseAppHost=false --nologo -v minimal

  log_info "Hot deploy: copying artifacts into $CONTAINER_NAME ..."
  docker cp "$PUBLISH_DIR/." "$CONTAINER_NAME:/app/"

  log_info "Hot deploy: restarting $CONTAINER_NAME ..."
  docker restart "$CONTAINER_NAME"
}

do_full_rebuild() {
  log_info "Full rebuild: building Docker image for $SERVICE_NAME ..."

  build_args=()
  [[ "$NO_CACHE" == "1" ]] && build_args+=(--no-cache)

  compose_cmd build "${build_args[@]}" "$SERVICE_NAME"

  log_info "Starting container ..."
  remove_stale_container
  compose_cmd up -d --no-deps "$SERVICE_NAME"
}

check_running() {
  if docker ps --format '{{.Names}}' | grep -qx "$CONTAINER_NAME"; then
    log_info "$CONTAINER_NAME is running."
  else
    log_warn "$CONTAINER_NAME not detected in docker ps. Check:"
    log_warn "  docker compose -p $PROJECT_NAME -f $COMPOSE_FILE ps"
  fi
}

main() {
  if [[ ! -f "$COMPOSE_FILE" ]]; then
    log_error "Compose file not found: $COMPOSE_FILE"
    exit 1
  fi

  if [[ ! -f "$SECRETS_FILE" ]]; then
    log_error "Secrets file not found: $SECRETS_FILE"
    exit 1
  fi

  require_command docker
  if ! docker compose version >/dev/null 2>&1; then
    log_error "docker compose plugin is required but was not found."
    exit 1
  fi

  log_info "Deploying $SERVICE_NAME (project: $PROJECT_NAME)"

  if [[ "$SKIP_BUILD" == "1" ]]; then
    log_info "SKIP_BUILD=1: restarting $CONTAINER_NAME without recompiling."
    clear_container_logs "$CONTAINER_NAME"
    docker restart "$CONTAINER_NAME"

  elif [[ "$FORCE_REBUILD" == "1" ]] || ! container_running; then
    if [[ "$FORCE_REBUILD" == "1" ]]; then
      log_info "FORCE_REBUILD=1: running full Docker image rebuild."
    else
      log_info "Container $CONTAINER_NAME not found — running full Docker build to create it."
    fi
    do_full_rebuild

  else
    log_info "Hot deploy mode (container exists). Use FORCE_REBUILD=1 to rebuild the Docker image."
    do_hot_deploy
  fi

  printf '\n'
  check_running
  printf '\n'
  log_info "Production URL:            http://childdev.homeserver.havranek.com"
  log_info "Logs:                      docker logs -f $CONTAINER_NAME"
  printf '\n'
  log_info "Hot redeploy (default):    ./scripts/deploy_prod.sh"
  log_info "Restart only (no compile): SKIP_BUILD=1 ./scripts/deploy_prod.sh"
  log_info "Full image rebuild:        FORCE_REBUILD=1 ./scripts/deploy_prod.sh"
  log_info "Cold rebuild (no cache):   FORCE_REBUILD=1 NO_CACHE=1 ./scripts/deploy_prod.sh"
}

main "$@"

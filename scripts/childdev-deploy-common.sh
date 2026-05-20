#!/usr/bin/env bash
set -euo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
WORKSPACE_ROOT="$(cd "$ROOT_DIR/.." && pwd)"
SHARED_DOCKER_DIR_DEFAULT="$WORKSPACE_ROOT/docker"

CHILDDEV_DIR_DEFAULT="$ROOT_DIR"
COMPOSE_FILE_DEFAULT="$SHARED_DOCKER_DIR_DEFAULT/docker-compose-childdev.yml"
DEV_HOST_DEFAULT="dev-childdev.homeserver.havranek.com"
DEV_SMOKE_URL_DEFAULT="http://127.0.0.1"
DEV_HEALTH_PATH_DEFAULT="/api/health"

CHILDDEV_DIR="$CHILDDEV_DIR_DEFAULT"
COMPOSE_FILE="$COMPOSE_FILE_DEFAULT"
COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-childdev}"
DEV_HOST="$DEV_HOST_DEFAULT"
DEV_SMOKE_URL="$DEV_SMOKE_URL_DEFAULT"
DEV_HEALTH_PATH="$DEV_HEALTH_PATH_DEFAULT"
GIT_REMOTE="origin"
DEPLOY_REF=""
ASSUME_YES=true
ALLOW_DIRTY=false
RUN_SMOKE_TESTS=true

API_DEV_SERVICE="childdev-api-dev"
API_DEV_CONTAINER="childdev-api-dev"
DB_DEV_SERVICE="childdev-db-dev"
DB_DEV_CONTAINER="childdev-db-dev"

TEMP_FILES=()

log_info() {
  printf '[INFO] %s\n' "$*"
}

log_warn() {
  printf '[WARN] %s\n' "$*" >&2
}

log_error() {
  printf '[ERROR] %s\n' "$*" >&2
}

cleanup() {
  local file_path
  for file_path in "${TEMP_FILES[@]}"; do
    if [[ -n "$file_path" && -f "$file_path" ]]; then
      rm -f "$file_path"
    fi
  done
  return 0
}

trap cleanup EXIT

require_command() {
  local command_name="$1"
  if ! command -v "$command_name" >/dev/null 2>&1; then
    log_error "Missing required command: $command_name"
    exit 1
  fi
}

require_file() {
  local file_path="$1"
  if [[ ! -f "$file_path" ]]; then
    log_error "Required file not found: $file_path"
    exit 1
  fi
}

require_dir() {
  local dir_path="$1"
  if [[ ! -d "$dir_path" ]]; then
    log_error "Required directory not found: $dir_path"
    exit 1
  fi
}

compose_cmd() {
  docker compose -p "$COMPOSE_PROJECT_NAME" -f "$COMPOSE_FILE" "$@"
}

confirm_or_exit() {
  local prompt="$1"
  local reply

  if [[ "$ASSUME_YES" == true ]]; then
    return 0
  fi

  printf '%s Type yes to continue: ' "$prompt"
  read -r reply
  if [[ "$reply" != "yes" ]]; then
    log_warn "Deployment aborted by user."
    exit 1
  fi
}

assert_repo_clean_if_required() {
  if [[ "$ALLOW_DIRTY" == true ]]; then
    log_warn "Proceeding with uncommitted changes because --allow-dirty is set."
    return 0
  fi

  if [[ -n "$(git -C "$CHILDDEV_DIR" status --porcelain)" ]]; then
    log_error "Uncommitted changes detected in $CHILDDEV_DIR"
    log_error "Commit/stash changes first, or rerun with --allow-dirty"
    exit 1
  fi
}

resolve_default_ref() {
  if git -C "$CHILDDEV_DIR" ls-remote --exit-code --heads "$GIT_REMOTE" main >/dev/null 2>&1; then
    printf 'main\n'
    return 0
  fi

  if git -C "$CHILDDEV_DIR" ls-remote --exit-code --heads "$GIT_REMOTE" master >/dev/null 2>&1; then
    printf 'master\n'
    return 0
  fi

  log_error "Could not find $GIT_REMOTE/main or $GIT_REMOTE/master"
  exit 1
}

has_remote() {
  git -C "$CHILDDEV_DIR" remote get-url "$GIT_REMOTE" >/dev/null 2>&1
}

checkout_requested_ref() {
  local requested_ref="$1"
  local default_branch
  local commit_sha

  if ! has_remote; then
    if [[ -n "$requested_ref" ]]; then
      log_error "--ref requires a configured git remote; none found for '$GIT_REMOTE'"
      exit 1
    fi
    commit_sha="$(git -C "$CHILDDEV_DIR" rev-parse --short HEAD)"
    log_warn "No git remote '$GIT_REMOTE' configured — building from current commit: $commit_sha"
    return 0
  fi

  log_info "Fetching remote refs from $GIT_REMOTE"
  git -C "$CHILDDEV_DIR" fetch "$GIT_REMOTE" --prune --tags

  if [[ -z "$requested_ref" ]]; then
    default_branch="$(resolve_default_ref)"
    log_info "No --ref provided; using latest $GIT_REMOTE/$default_branch"
    git -C "$CHILDDEV_DIR" checkout -B "$default_branch" "$GIT_REMOTE/$default_branch"
  else
    log_info "Checking out requested ref: $requested_ref"

    if git -C "$CHILDDEV_DIR" show-ref --verify --quiet "refs/heads/$requested_ref"; then
      git -C "$CHILDDEV_DIR" checkout "$requested_ref"
      if git -C "$CHILDDEV_DIR" ls-remote --exit-code --heads "$GIT_REMOTE" "$requested_ref" >/dev/null 2>&1; then
        git -C "$CHILDDEV_DIR" pull --ff-only "$GIT_REMOTE" "$requested_ref"
      fi
    elif git -C "$CHILDDEV_DIR" ls-remote --exit-code --heads "$GIT_REMOTE" "$requested_ref" >/dev/null 2>&1; then
      git -C "$CHILDDEV_DIR" checkout -B "$requested_ref" "$GIT_REMOTE/$requested_ref"
    elif git -C "$CHILDDEV_DIR" rev-parse --verify --quiet "$requested_ref^{commit}" >/dev/null; then
      git -C "$CHILDDEV_DIR" checkout --detach "$requested_ref"
    else
      log_error "Requested ref not found as local branch, remote branch, tag, or commit: $requested_ref"
      exit 1
    fi
  fi

  commit_sha="$(git -C "$CHILDDEV_DIR" rev-parse --short HEAD)"
  log_info "Using source commit: $commit_sha"
}

build_dev_image() {
  log_info "Building image from service $API_DEV_SERVICE"
  compose_cmd build "$API_DEV_SERVICE"
  log_info "Build complete"
}

wait_for_container_health() {
  local container_name="$1"
  local max_wait_seconds="${2:-120}"
  local waited=0
  local health_state=""

  while (( waited < max_wait_seconds )); do
    health_state="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container_name" 2>/dev/null || true)"
    if [[ "$health_state" == "healthy" || "$health_state" == "running" ]]; then
      return 0
    fi
    sleep 2
    waited=$((waited + 2))
  done

  log_error "Container did not become healthy in time: $container_name"
  return 1
}

deploy_dev_stack() {
  log_info "Deploying dev services: $DB_DEV_SERVICE, $API_DEV_SERVICE"
  compose_cmd up -d "$DB_DEV_SERVICE"
  wait_for_container_health "$DB_DEV_CONTAINER"
  compose_cmd up -d --no-deps --force-recreate "$API_DEV_SERVICE"
  log_info "Deployment command finished for $API_DEV_SERVICE"
}

run_dev_smoke_test() {
  local url="$DEV_SMOKE_URL$DEV_HEALTH_PATH"
  local max_wait=60
  local waited=0
  local http_code=""

  if [[ "$RUN_SMOKE_TESTS" != true ]]; then
    log_warn "Skipping smoke test because --skip-smoke was provided."
    return 0
  fi

  log_info "Smoke test: waiting for $url (Host: $DEV_HOST)"

  while (( waited < max_wait )); do
    http_code="$(curl -s -o /dev/null -w '%{http_code}' \
      -H "Host: $DEV_HOST" \
      --max-time 5 \
      "$url" 2>/dev/null || true)"

    if [[ "$http_code" == "200" ]]; then
      log_info "Health check passed (HTTP $http_code)"
      return 0
    fi

    sleep 3
    waited=$((waited + 3))
  done

  log_error "Health check failed after ${max_wait}s — last HTTP status: ${http_code:-none}"
  return 1
}

print_dev_summary() {
  local api_status
  local db_status

  api_status="$(docker inspect -f '{{.State.Status}}' "$API_DEV_CONTAINER" 2>/dev/null || echo 'not-found')"
  db_status="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$DB_DEV_CONTAINER" 2>/dev/null || echo 'not-found')"

  log_info "Deployment status summary"
  log_info "  $API_DEV_CONTAINER: $api_status"
  log_info "  $DB_DEV_CONTAINER: $db_status"
  log_info "  URL: http://$DEV_HOST"
  log_info "Done"
}

validate_repo_and_common_files() {
  require_dir "$CHILDDEV_DIR"
  require_file "$COMPOSE_FILE"

  if [[ ! -d "$CHILDDEV_DIR/.git" ]]; then
    log_error "Not a git repo: $CHILDDEV_DIR"
    exit 1
  fi
}

require_common_commands() {
  require_command git
  require_command docker
  require_command curl
  require_command date
}

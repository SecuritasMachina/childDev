#!/usr/bin/env bash
set -euo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=childdev-deploy-common.sh
source "$SCRIPT_DIR/childdev-deploy-common.sh"

# --- Deployment mode flags ---
# Default: hot deploy (publish on host, docker cp into running container, restart).
# FORCE_REBUILD=1  — full Docker image rebuild (use when Dockerfile/base image changed).
# SKIP_BUILD=1     — just restart the container, no compile step at all.
FORCE_REBUILD="${FORCE_REBUILD:-0}"
SKIP_BUILD="${SKIP_BUILD:-0}"
PUBLISH_DIR="${PUBLISH_DIR:-/tmp/childdev-hotdeploy}"

usage() {
  cat <<EOF
Usage: $(basename "$0") [options]

Deploy the ChildDev dev stack (childdev-api-dev + childdev-db-dev).

Options:
  --ref <commit|tag|branch>   Deploy from a specific git ref (requires FORCE_REBUILD=1).
                              Default: latest from origin/main, or origin/master.
  --yes                       Skip interactive confirmation prompt.
  --allow-dirty               Allow deployment with uncommitted repo changes.
  --skip-smoke                Skip health check after deployment.
  --childdev-dir <path>       ChildDev repository path.
  --compose-file <path>       Compose file path.
  --dev-host <hostname>       Host header for smoke test.
  --dev-url <url>             Base URL for smoke test. Default: $DEV_SMOKE_URL_DEFAULT
  -h, --help                  Show this help message.

Environment variables:
  FORCE_REBUILD=1             Full Docker image rebuild (default: hot deploy).
  SKIP_BUILD=1                Restart container only, no compile step.
  NO_CACHE=1                  Disable Docker layer cache (requires FORCE_REBUILD=1).
EOF
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --ref)
        [[ $# -ge 2 ]] || { log_error "--ref requires a value"; exit 1; }
        DEPLOY_REF="$2"
        shift 2
        ;;
      --yes)
        ASSUME_YES=true
        shift
        ;;
      --allow-dirty)
        ALLOW_DIRTY=true
        shift
        ;;
      --skip-smoke)
        RUN_SMOKE_TESTS=false
        shift
        ;;
      --childdev-dir)
        [[ $# -ge 2 ]] || { log_error "--childdev-dir requires a value"; exit 1; }
        CHILDDEV_DIR="$2"
        shift 2
        ;;
      --compose-file)
        [[ $# -ge 2 ]] || { log_error "--compose-file requires a value"; exit 1; }
        COMPOSE_FILE="$2"
        shift 2
        ;;
      --dev-host)
        [[ $# -ge 2 ]] || { log_error "--dev-host requires a value"; exit 1; }
        DEV_HOST="$2"
        shift 2
        ;;
      --dev-url)
        [[ $# -ge 2 ]] || { log_error "--dev-url requires a value"; exit 1; }
        DEV_SMOKE_URL="$2"
        shift 2
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        log_error "Unknown argument: $1"
        usage
        exit 1
        ;;
    esac
  done
}

do_hot_deploy() {
  require_command dotnet
  log_info "Hot deploy: publishing to $PUBLISH_DIR ..."
  if ! rm -rf "$PUBLISH_DIR" 2>/dev/null; then
    log_warn "$PUBLISH_DIR has root-owned files (from a prior privileged run). Removing via sudo."
    sudo rm -rf "$PUBLISH_DIR"
  fi
  dotnet publish "$CHILDDEV_DIR/ChildDev.Api/ChildDev.Api.csproj" \
    -c Release -o "$PUBLISH_DIR" /p:UseAppHost=false --nologo -v minimal
  log_info "Hot deploy: copying artifacts into $API_DEV_CONTAINER ..."
  docker cp "$PUBLISH_DIR/." "$API_DEV_CONTAINER:/app/"
  log_info "Hot deploy: restarting $API_DEV_CONTAINER ..."
  docker restart "$API_DEV_CONTAINER"
}

main() {
  parse_args "$@"

  require_common_commands
  validate_repo_and_common_files

  log_info "Starting ChildDev dev deployment"
  log_info "ChildDev repo: $CHILDDEV_DIR"
  log_info "Compose file: $COMPOSE_FILE"
  log_info "Dev host: $DEV_HOST"

  if [[ "$SKIP_BUILD" == "1" ]]; then
    log_info "SKIP_BUILD=1: restarting $API_DEV_CONTAINER without recompiling."
    docker restart "$API_DEV_CONTAINER"
    run_dev_smoke_test
    print_dev_summary
    printf '\n'
    log_info "Hot redeploy (default):     ./scripts/deploy-dev.sh"
    log_info "Restart only (no compile):  SKIP_BUILD=1 ./scripts/deploy-dev.sh"
    log_info "Full image rebuild:         FORCE_REBUILD=1 ./scripts/deploy-dev.sh"
    log_info "Cold rebuild (no cache):    FORCE_REBUILD=1 NO_CACHE=1 ./scripts/deploy-dev.sh"
    return
  fi

  if [[ "$FORCE_REBUILD" == "1" ]] || ! docker container inspect "$API_DEV_CONTAINER" >/dev/null 2>&1; then
    if [[ "$FORCE_REBUILD" == "1" ]]; then
      log_info "FORCE_REBUILD=1: running full Docker image rebuild."
    else
      log_info "Container $API_DEV_CONTAINER not found; running full Docker build to create it."
    fi
    assert_repo_clean_if_required
    checkout_requested_ref "$DEPLOY_REF"
    build_dev_image
    deploy_dev_stack
  else
    log_info "Hot deploy mode (container exists). Use FORCE_REBUILD=1 to rebuild the Docker image."
    do_hot_deploy
  fi

  run_dev_smoke_test
  print_dev_summary
  printf '\n'
  log_info "Hot redeploy (default):     ./scripts/deploy-dev.sh"
  log_info "Restart only (no compile):  SKIP_BUILD=1 ./scripts/deploy-dev.sh"
  log_info "Full image rebuild:         FORCE_REBUILD=1 ./scripts/deploy-dev.sh"
  log_info "Cold rebuild (no cache):    FORCE_REBUILD=1 NO_CACHE=1 ./scripts/deploy-dev.sh"
}

main "$@"

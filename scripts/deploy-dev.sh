#!/usr/bin/env bash
set -euo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=childdev-deploy-common.sh
source "$SCRIPT_DIR/childdev-deploy-common.sh"

usage() {
  cat <<EOF
Usage: $(basename "$0") [options]

Deploy the ChildDev dev stack (childdev-api-dev + childdev-db-dev).

Options:
  --ref <commit|tag|branch>   Deploy from a specific git ref.
                              Default: latest from origin/main, or origin/master.
  --yes                       Skip interactive confirmation prompt.
  --allow-dirty               Allow deployment with uncommitted repo changes.
  --skip-smoke                Skip health check after deployment.
  --childdev-dir <path>       ChildDev repository path.
  --compose-file <path>       Compose file path.
  --dev-host <hostname>       Host header for smoke test.
  --dev-url <url>             Base URL for smoke test. Default: $DEV_SMOKE_URL_DEFAULT
  -h, --help                  Show this help message.
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

main() {
  parse_args "$@"

  require_common_commands
  validate_repo_and_common_files

  log_info "Starting ChildDev dev deployment"
  log_info "ChildDev repo: $CHILDDEV_DIR"
  log_info "Compose file: $COMPOSE_FILE"
  log_info "Dev host: $DEV_HOST"

  confirm_or_exit "Proceed with ChildDev dev deployment?"
  assert_repo_clean_if_required
  checkout_requested_ref "$DEPLOY_REF"
  build_dev_image
  deploy_dev_stack
  run_dev_smoke_test
  print_dev_summary
}

main "$@"

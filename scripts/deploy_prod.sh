#!/usr/bin/env bash
set -euo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

# Remote server
SSH_KEY="/home/jaxtrx/.ssh/hostWinds_id_rsa"
SSH_HOST="root@hwsrv-1313060.hostwindsdns.com"
REMOTE_DIR="/opt/childdev"
SECRETS_FILE="/home/jaxtrx/data/.secrets/childdev-prod.env"
SSH_SECRETS_FILE="/home/jaxtrx/data/.secrets/hostwinds-ssh.env"

COMPOSE_FILE="$REMOTE_DIR/docker-compose.yml"
PROJECT_NAME="childdev"
SERVICE_NAME="childdev-api"

FORCE_REBUILD="${FORCE_REBUILD:-0}"
NO_CACHE="${NO_CACHE:-0}"

log_info()  { printf '[INFO] %s\n' "$*"; }
log_warn()  { printf '[WARN] %s\n' "$*" >&2; }
log_error() { printf '[ERROR] %s\n' "$*" >&2; }

ssh_run() {
  ssh -i "$SSH_KEY" -o StrictHostKeyChecking=no -o ConnectTimeout=30 "$SSH_HOST" "$@"
}

main() {
  if [[ ! -f "$SECRETS_FILE" ]]; then
    log_error "Secrets file not found: $SECRETS_FILE"
    exit 1
  fi

  log_info "Syncing source to $SSH_HOST:$REMOTE_DIR ..."
  rsync -az --delete \
    --exclude='.git' \
    --exclude='*/bin/' \
    --exclude='*/obj/' \
    --exclude='test-results/' \
    --exclude='playwright/test-results/' \
    --exclude='childDev/node_modules/' \
    --exclude='*.apk' \
    -e "ssh -i $SSH_KEY -o StrictHostKeyChecking=no" \
    "$ROOT_DIR/" \
    "$SSH_HOST:$REMOTE_DIR/"

  log_info "Syncing secrets ..."
  scp -i "$SSH_KEY" -o StrictHostKeyChecking=no \
    "$SECRETS_FILE" \
    "$SSH_HOST:$REMOTE_DIR/.env"

  log_info "Building and deploying on remote ..."

  build_args=""
  [[ "$NO_CACHE" == "1" ]] && build_args="--no-cache"

  if [[ "$FORCE_REBUILD" == "1" ]]; then
    log_info "FORCE_REBUILD=1: full image rebuild + stack up"
    ssh_run "
      cd $REMOTE_DIR
      docker compose -p $PROJECT_NAME -f $COMPOSE_FILE build $build_args $SERVICE_NAME
      docker compose -p $PROJECT_NAME -f $COMPOSE_FILE up -d
    "
  else
    log_info "Hot deploy: publish locally, copy to remote container, restart"
    PUBLISH_DIR="/tmp/childdev-prod-hotdeploy"
    rm -rf "$PUBLISH_DIR"
    dotnet publish "$ROOT_DIR/ChildDev.Api/ChildDev.Api.csproj" \
      -c Release -o "$PUBLISH_DIR" /p:UseAppHost=false --nologo -v minimal

    log_info "Copying artifacts to remote container ..."
    # Get running container name
    CONTAINER=$(ssh_run "docker ps --filter name=childdev-childdev-api --format '{{.Names}}' | head -1")
    if [[ -z "$CONTAINER" ]]; then
      log_warn "No running container found — falling back to full rebuild"
      ssh_run "
        cd $REMOTE_DIR
        docker compose -p $PROJECT_NAME -f $COMPOSE_FILE build $SERVICE_NAME
        docker compose -p $PROJECT_NAME -f $COMPOSE_FILE up -d
      "
    else
      # Copy published output to remote then docker cp into container
      rsync -az --delete \
        -e "ssh -i $SSH_KEY -o StrictHostKeyChecking=no" \
        "$PUBLISH_DIR/" \
        "$SSH_HOST:/tmp/childdev-hotdeploy/"
      ssh_run "docker cp /tmp/childdev-hotdeploy/. $CONTAINER:/app/ && docker restart $CONTAINER"
    fi
    rm -rf "$PUBLISH_DIR"
  fi

  log_info "Checking remote container status ..."
  ssh_run "docker ps --filter name=childdev-childdev-api --format 'table {{.Names}}\t{{.Status}}'"

  printf '\n'
  log_info "Production URL:   https://levelup.havranek.com"
  log_info "Remote logs:      ssh -i $SSH_KEY $SSH_HOST 'docker logs -f \$(docker ps --filter name=childdev-childdev-api -q)'"
  printf '\n'
  log_info "Hot redeploy (default):  ./scripts/deploy_prod.sh"
  log_info "Full image rebuild:      FORCE_REBUILD=1 ./scripts/deploy_prod.sh"
  log_info "Cold rebuild (no cache): FORCE_REBUILD=1 NO_CACHE=1 ./scripts/deploy_prod.sh"
}

main "$@"

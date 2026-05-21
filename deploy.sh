#!/usr/bin/env bash
# deploy.sh — push code + optional APK to the Hostwinds server and restart the app
set -euo pipefail

SERVER="root@hwsrv-1313060.hostwindsdns.com"
SSH_KEY="/home/jaxtrx/.ssh/hostWinds_id_rsa"
REMOTE_DIR="/opt/childdev"
APK_SRC="${1:-}"   # optional: path to a built APK file  e.g.  ./deploy.sh ./myapp.apk

echo "==> Syncing source files..."
rsync -az --delete \
  --exclude='.git/' \
  --exclude='bin/' \
  --exclude='obj/' \
  --exclude='*.env' \
  --exclude='node_modules/' \
  --exclude='*.apk' \
  -e "ssh -o StrictHostKeyChecking=no -i $SSH_KEY" \
  /mnt/8TB_HDD_DATA/shared/src/childDev/ "$SERVER:$REMOTE_DIR/"

if [[ -n "$APK_SRC" ]]; then
  if [[ ! -f "$APK_SRC" ]]; then
    echo "ERROR: APK file not found: $APK_SRC" >&2
    exit 1
  fi
  echo "==> Uploading APK: $APK_SRC"
  scp -o StrictHostKeyChecking=no -i "$SSH_KEY" "$APK_SRC" \
    "$SERVER:$REMOTE_DIR/ChildDev.Api/wwwroot/downloads/ChildDev.apk"
fi

echo "==> Rebuilding and restarting app container..."
ssh -o StrictHostKeyChecking=no -i "$SSH_KEY" "$SERVER" \
  "cd $REMOTE_DIR && docker compose build childdev-api && docker compose up -d --no-deps childdev-api"

echo "==> Deploy complete."
echo "    App: https://childdev.havranek.com"
if [[ -n "$APK_SRC" ]]; then
  echo "    APK: https://childdev.havranek.com/downloads/ChildDev.apk"
fi

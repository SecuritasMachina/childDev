#!/usr/bin/env bash
# Provisions a fresh Ubuntu 22.04 VM with the full Android test stack.
# Run this inside the VM via SSH after first boot.
# Usage: bash provision-android-vm.sh
set -euo pipefail

ANDROID_SDK_ROOT="$HOME/android-sdk"
JAVA_HOME_PATH="/usr/lib/jvm/java-17-openjdk-amd64"
DOTNET_INSTALL_DIR="/usr/share/dotnet"

log() { echo "[$(date '+%H:%M:%S')] $*"; }

log "=== Step 1: System packages ==="
sudo apt-get update -qq
sudo apt-get install -y \
    openjdk-17-jdk \
    wget curl unzip \
    libgl1-mesa-glx libgles2-mesa \
    mesa-vulkan-drivers vulkan-tools libvulkan1 \
    libpulse0 \
    libx11-6 libxext6 libxrender1 libxrandr2 libxfixes3 libxi6 \
    xvfb x11-utils \
    ca-certificates apt-transport-https gnupg \
    openssh-server ufw

log "=== Step 2: .NET 8 SDK ==="
wget -q https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O /tmp/ms.deb
sudo dpkg -i /tmp/ms.deb
rm /tmp/ms.deb
sudo apt-get update -qq
sudo apt-get install -y dotnet-sdk-8.0

log "=== Step 3: Android command-line tools ==="
mkdir -p "$ANDROID_SDK_ROOT/cmdline-tools"
wget -q https://dl.google.com/android/repository/commandlinetools-linux-11076708_latest.zip -O /tmp/cmdtools.zip
unzip -q /tmp/cmdtools.zip -d /tmp/cmdtools
mv /tmp/cmdtools/cmdline-tools "$ANDROID_SDK_ROOT/cmdline-tools/latest"
rm -rf /tmp/cmdtools /tmp/cmdtools.zip

export PATH="$ANDROID_SDK_ROOT/cmdline-tools/latest/bin:$ANDROID_SDK_ROOT/platform-tools:$ANDROID_SDK_ROOT/emulator:$PATH"
export ANDROID_SDK_ROOT ANDROID_HOME="$ANDROID_SDK_ROOT" JAVA_HOME="$JAVA_HOME_PATH"

log "=== Step 4: Android SDK packages ==="
yes | sdkmanager --licenses > /dev/null 2>&1 || true
sdkmanager \
    "platform-tools" \
    "emulator" \
    "platforms;android-34" \
    "platforms;android-29" \
    "system-images;android-29;google_apis;x86" \
    "build-tools;34.0.0"

log "=== Step 5: Create AVD ==="
echo 'no' | avdmanager create avd \
    --name childdev_test \
    --package 'system-images;android-29;google_apis;x86' \
    --device 'Nexus 5' \
    --sdcard 512M \
    --force

log "=== Step 6: MAUI Android workload ==="
sudo dotnet workload install maui-android --skip-sign-check

log "=== Step 7: Write environment profile ==="
cat >> "$HOME/.bashrc" << 'PROFILE'
export ANDROID_SDK_ROOT="$HOME/android-sdk"
export ANDROID_HOME="$HOME/android-sdk"
export JAVA_HOME="/usr/lib/jvm/java-17-openjdk-amd64"
export PATH="$JAVA_HOME/bin:$ANDROID_SDK_ROOT/cmdline-tools/latest/bin:$ANDROID_SDK_ROOT/platform-tools:$ANDROID_SDK_ROOT/emulator:$PATH"
PROFILE

log ""
log "=========================================="
log "  Provisioning complete!"
log "  Run: bash /src/scripts/run-mobile-tests-vm.sh"
log "=========================================="

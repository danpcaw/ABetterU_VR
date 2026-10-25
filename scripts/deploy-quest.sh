#!/usr/bin/env bash
# Sideload an APK to the connected Quest and launch it.
# Usage: ./scripts/deploy-quest.sh path/to/build.apk [package.name]
set -euo pipefail

APK="${1:?usage: deploy-quest.sh path/to/build.apk [package.name]}"
PKG="${2:-}"

if ! command -v adb >/dev/null; then
  echo "adb not found. Install Android platform-tools (or use Unity's bundled copy)." >&2
  exit 1
fi

if ! adb devices | grep -qw device; then
  echo "No Quest detected. Enable developer mode, connect USB, accept the headset prompt." >&2
  exit 1
fi

adb install -r "$APK"

if [ -n "$PKG" ]; then
  adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1
fi

#!/usr/bin/env bash
# Downloads the Godot .NET (mono) editor binary used for headless tests/screenshots.
# Usage: tools/ci/get-godot.sh [install_dir]   -> prints the path of the godot executable.
set -euo pipefail

GODOT_VERSION="${GODOT_VERSION:-4.7.2}"
GODOT_CHANNEL="${GODOT_CHANNEL:-stable}"
INSTALL_DIR="${1:-${GODOT_INSTALL_DIR:-$HOME/.cache/strata-godot}}"
NAME="Godot_v${GODOT_VERSION}-${GODOT_CHANNEL}_mono_linux_x86_64"
URL="https://github.com/godotengine/godot-builds/releases/download/${GODOT_VERSION}-${GODOT_CHANNEL}/${NAME}.zip"
BIN="${INSTALL_DIR}/${NAME}/Godot_v${GODOT_VERSION}-${GODOT_CHANNEL}_mono_linux.x86_64"

if [[ ! -x "${BIN}" ]]; then
  mkdir -p "${INSTALL_DIR}"
  echo "Downloading ${URL}" >&2
  curl -fsSL --retry 4 --retry-delay 2 -o "${INSTALL_DIR}/${NAME}.zip" "${URL}"
  (cd "${INSTALL_DIR}" && unzip -q -o "${NAME}.zip" && rm -f "${NAME}.zip")
  chmod +x "${BIN}"
fi

echo "${BIN}"

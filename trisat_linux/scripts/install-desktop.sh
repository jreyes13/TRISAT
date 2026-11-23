#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LINUX_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
INSTALL_DIR="${HOME}/.local/opt/trisat"
BIN_DIR="${HOME}/.local/bin"
APP_DIR="${HOME}/.local/share/applications"
ICON_DIR="${HOME}/.local/share/icons/hicolor/256x256/apps"

ARCH="$(uname -m)"
case "${ARCH}" in
  x86_64)  RID="linux-x64" ;;
  aarch64) RID="linux-arm64" ;;
  *)
    echo "Unsupported architecture: ${ARCH} (expected x86_64 or aarch64)" >&2
    exit 1
    ;;
esac

echo "[TRISAT] Publishing native self-contained Linux binary (${RID})..."
dotnet publish "${LINUX_DIR}/Trisat.Linux/Trisat.Linux.csproj" \
  -c Release \
  -r "${RID}" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "${INSTALL_DIR}"

mkdir -p "${BIN_DIR}" "${APP_DIR}" "${ICON_DIR}"
ln -sf "${INSTALL_DIR}/TRISAT" "${BIN_DIR}/trisat"
cp "${LINUX_DIR}/Trisat.Linux/Assets/trisat_logo.png" "${ICON_DIR}/trisat.png"

cat > "${APP_DIR}/trisat.desktop" <<EOF
[Desktop Entry]
Name=TRISAT
Comment=Authenticated Earth Intelligence — Galileo OSNMA & Multi-Constellation GNSS Cockpit
Exec=${INSTALL_DIR}/TRISAT
Icon=${ICON_DIR}/trisat.png
Terminal=false
Type=Application
Categories=Science;Education;Network;Utility;
StartupWMClass=TRISAT
EOF

chmod +x "${INSTALL_DIR}/TRISAT" "${APP_DIR}/trisat.desktop"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "${APP_DIR}" || true

echo ""
echo "[TRISAT] Native Linux installation complete!"
echo "  Binary:   ${INSTALL_DIR}/TRISAT (symlinked at ${BIN_DIR}/trisat)"
echo "  Launcher: ${APP_DIR}/trisat.desktop"
echo ""
echo "Optional: for non-root USB access to u-blox ZED-F9P / SXblue / ESP32 / HackRF:"
echo "  sudo cp ${SCRIPT_DIR}/99-trisat-gnss.rules /etc/udev/rules.d/"
echo "  sudo udevadm control --reload-rules && sudo udevadm trigger"
echo "  sudo usermod -aG dialout \$USER"

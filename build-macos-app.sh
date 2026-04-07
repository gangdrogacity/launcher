#!/usr/bin/env bash
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "$0")" && pwd)"
APP_NAME="GangDrogaCity Launcher"
APP_PATH="${PROJECT_DIR}/${APP_NAME}.app"
PLIST_PATH="${APP_PATH}/Contents/Info.plist"
MACOS_DIR="${APP_PATH}/Contents/MacOS"
RES_DIR="${APP_PATH}/Contents/Resources"
EXEC_PATH="${MACOS_DIR}/launcher-wrapper"
BUNDLED_LAUNCHER="${RES_DIR}/launcher.sh"

rm -rf "${APP_PATH}"
mkdir -p "${MACOS_DIR}" "${RES_DIR}"

cat > "${PLIST_PATH}" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleExecutable</key>
  <string>launcher-wrapper</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>CFBundleIdentifier</key>
  <string>it.gangdrogacity.launcher.bashapp</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>GangDrogaCity Launcher</string>
  <key>CFBundleDisplayName</key>
  <string>GangDrogaCity Launcher</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0.0</string>
  <key>CFBundleVersion</key>
  <string>1</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
EOF

cat > "${EXEC_PATH}" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

APP_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
LAUNCHER_SH="${APP_DIR}/Contents/Resources/launcher.sh"

if [[ ! -f "${LAUNCHER_SH}" ]]; then
  osascript -e 'display dialog "launcher.sh non trovato dentro la .app" buttons {"OK"} default button "OK" with icon caution'
  exit 1
fi

osascript <<APPLESCRIPT
tell application "Terminal"
  activate
  do script "chmod +x " & quoted form of "${LAUNCHER_SH}" & " && " & quoted form of "${LAUNCHER_SH}"
end tell
APPLESCRIPT
EOF

chmod +x "${EXEC_PATH}"

# Include lo script launcher dentro il bundle .app
cp -f "${PROJECT_DIR}/launcher.sh" "${BUNDLED_LAUNCHER}"
chmod +x "${BUNDLED_LAUNCHER}"

# Prova a generare AppIcon.icns partendo dalla stessa icona della WinForms app.
WIN_ICON_ICO="${PROJECT_DIR}/launcher.ico"
ICONSET_DIR="${PROJECT_DIR}/.appicon.iconset"
TEMP_ICON_PNG="${PROJECT_DIR}/.appicon_source.png"

if [[ -f "${WIN_ICON_ICO}" ]]; then
  rm -rf "${ICONSET_DIR}" "${TEMP_ICON_PNG}" "${PROJECT_DIR}/.AppIcon.icns"
  mkdir -p "${ICONSET_DIR}"

  if sips -s format png "${WIN_ICON_ICO}" --out "${TEMP_ICON_PNG}" >/dev/null 2>&1; then
    sips -z 16 16     "${TEMP_ICON_PNG}" --out "${ICONSET_DIR}/icon_16x16.png" >/dev/null
    sips -z 32 32     "${TEMP_ICON_PNG}" --out "${ICONSET_DIR}/icon_16x16@2x.png" >/dev/null
    sips -z 32 32     "${TEMP_ICON_PNG}" --out "${ICONSET_DIR}/icon_32x32.png" >/dev/null
    sips -z 64 64     "${TEMP_ICON_PNG}" --out "${ICONSET_DIR}/icon_32x32@2x.png" >/dev/null
    sips -z 128 128   "${TEMP_ICON_PNG}" --out "${ICONSET_DIR}/icon_128x128.png" >/dev/null
    sips -z 256 256   "${TEMP_ICON_PNG}" --out "${ICONSET_DIR}/icon_128x128@2x.png" >/dev/null
    sips -z 256 256   "${TEMP_ICON_PNG}" --out "${ICONSET_DIR}/icon_256x256.png" >/dev/null
    sips -z 512 512   "${TEMP_ICON_PNG}" --out "${ICONSET_DIR}/icon_256x256@2x.png" >/dev/null
    sips -z 512 512   "${TEMP_ICON_PNG}" --out "${ICONSET_DIR}/icon_512x512.png" >/dev/null
    sips -z 1024 1024 "${TEMP_ICON_PNG}" --out "${ICONSET_DIR}/icon_512x512@2x.png" >/dev/null

    if iconutil -c icns "${ICONSET_DIR}" -o "${RES_DIR}/AppIcon.icns" >/dev/null 2>&1; then
      echo "Icona macOS generata da launcher.ico"
    fi
  fi

  rm -rf "${ICONSET_DIR}" "${TEMP_ICON_PNG}" "${PROJECT_DIR}/.AppIcon.icns"
fi

# Fallback: se non è stata generata, usa AppIcon.icns già presente in macos/.
if [[ ! -f "${RES_DIR}/AppIcon.icns" ]] && [[ -f "${PROJECT_DIR}/macos/AppIcon.icns" ]]; then
  cp -f "${PROJECT_DIR}/macos/AppIcon.icns" "${RES_DIR}/AppIcon.icns"
fi

echo "APPL????" > "${APP_PATH}/Contents/PkgInfo"

plutil -lint "${PLIST_PATH}" >/dev/null

echo "App creata: ${APP_PATH}"

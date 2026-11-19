#!/usr/bin/env bash
# Builds TaxWise.app for Apple Silicon and Intel Macs, pointed at the deployed API, and wraps it in a .dmg.
# Usage: scripts/package-mac.sh [https://<app-name>.azurewebsites.net]
# With no URL it uses the ApiBaseUrl already in src/TaxWise.App/appsettings.Release.json.
set -euo pipefail

cd "$(dirname "$0")/.."
APP_DIR=src/TaxWise.App
RELEASE_SETTINGS=$APP_DIR/appsettings.Release.json
OUT=publish/mac

if [[ $# -ge 1 ]]; then
  printf '{ "ApiBaseUrl": "%s" }\n' "${1%/}" > "$RELEASE_SETTINGS"
fi
if [[ ! -f $RELEASE_SETTINGS ]]; then
  echo "Pass the deployed API URL, e.g. scripts/package-mac.sh https://taxwise-api.azurewebsites.net" >&2
  exit 1
fi
API_URL=$(sed -n 's/.*"ApiBaseUrl": *"\([^"]*\)".*/\1/p' "$RELEASE_SETTINGS")
echo "Packaging against $API_URL"
curl -fsS --max-time 60 "$API_URL/health" > /dev/null || echo "Warning: $API_URL/health didn't answer. The App will show a red dot until the API is up." >&2

# The build doesn't always re-sign when only the entitlements change (DECISIONS #19), so start clean.
rm -rf "$APP_DIR/bin/Release" "$APP_DIR/obj/Release" "$OUT"
dotnet publish "$APP_DIR" -f net10.0-maccatalyst -c Release \
  -p:RuntimeIdentifiers='"maccatalyst-arm64;maccatalyst-x64"' -p:CreatePackage=false -p:ValidateXcodeVersion=false

# The universal, signed bundle; the per-architecture ones in maccatalyst-*/ have no entitlements.
APP=$APP_DIR/bin/Release/net10.0-maccatalyst/TaxWise.app
[[ -d $APP ]] || { echo "No $APP after publish" >&2; exit 1; }

mkdir -p "$OUT/dmg"
cp -R "$APP" "$OUT/dmg/TaxWise.app"
ln -s /Applications "$OUT/dmg/Applications"
hdiutil create -volname TaxWise -srcfolder "$OUT/dmg" -ov -format UDZO "$OUT/TaxWise.dmg" > /dev/null
rm -rf "$OUT/dmg"

echo "Done: $OUT/TaxWise.dmg"

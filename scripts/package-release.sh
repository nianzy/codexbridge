#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPOSITORY_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT="$REPOSITORY_ROOT/apps/macos/CodexBridgeApp.xcodeproj"
OUTPUT_DIR="${CODEX_BRIDGE_OUTPUT_DIR:-$REPOSITORY_ROOT/tmp/dist}"
BUILD_ROOT="$OUTPUT_DIR/build"
APP_PATH="$BUILD_ROOT/Release/Codex Bridge.app"
STAGING_DIR="$OUTPUT_DIR/.dmg-staging"
LEGACY_APP_PATH="$BUILD_ROOT/Release/Sidely.app"
LEGACY_ROOT_APP_PATH="$OUTPUT_DIR/Sidely.app"
LEGACY_DMG_PATH="$OUTPUT_DIR/Sidely.dmg"
SIGNING_IDENTITY="${CODEX_BRIDGE_SIGNING_IDENTITY:-}"
DEVELOPMENT_TEAM_VALUE="${CODEX_BRIDGE_DEVELOPMENT_TEAM:-}"
RELEASE_CHANNEL="${CODEX_BRIDGE_RELEASE_CHANNEL:-beta}"

mkdir -p "$OUTPUT_DIR"

cleanup() {
  rm -rf "$STAGING_DIR"
}
trap cleanup EXIT

BUILD_SETTINGS=(
  -project "$PROJECT"
  -scheme CodexBridge
  -configuration Release
  -destination "platform=macOS"
  -derivedDataPath "$BUILD_ROOT/DerivedData"
  CONFIGURATION_BUILD_DIR="$BUILD_ROOT/Release"
  CODE_SIGN_INJECT_BASE_ENTITLEMENTS=NO
)

if [[ -n "$SIGNING_IDENTITY" ]]; then
  BUILD_SETTINGS+=(CODE_SIGN_STYLE=Manual CODE_SIGN_IDENTITY="$SIGNING_IDENTITY")
  if [[ -n "$DEVELOPMENT_TEAM_VALUE" ]]; then
    BUILD_SETTINGS+=(DEVELOPMENT_TEAM="$DEVELOPMENT_TEAM_VALUE")
  fi
elif [[ "${CODEX_BRIDGE_ALLOW_ADHOC:-0}" == "1" ]]; then
  printf '%s\n' '注意：临时签名只适合测试；每次重建后可能需要重新授权辅助功能。' >&2
  BUILD_SETTINGS+=(CODE_SIGN_IDENTITY=-)
else
  printf '%s\n' '请设置 CODEX_BRIDGE_SIGNING_IDENTITY 为固定的签名证书；仅临时测试可设置 CODEX_BRIDGE_ALLOW_ADHOC=1。' >&2
  exit 1
fi

xcodebuild "${BUILD_SETTINGS[@]}" clean build

test -x "$APP_PATH/Contents/MacOS/Codex Bridge"
test -x "$APP_PATH/Contents/Helpers/CodexBridgeNativeHost"
test -f "$APP_PATH/Contents/Resources/extension/manifest.json"
codesign --verify --deep --strict --verbose=2 "$APP_PATH"

APP_VERSION="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$APP_PATH/Contents/Info.plist")"
APP_BUILD="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' "$APP_PATH/Contents/Info.plist")"
APP_ARCHS="$(lipo -archs "$APP_PATH/Contents/MacOS/Codex Bridge")"
if [[ "$APP_ARCHS" == *arm64* && "$APP_ARCHS" == *x86_64* ]]; then
  ARCH_LABEL="universal"
else
  ARCH_LABEL="${APP_ARCHS// /-}"
fi

ARTIFACT_BASENAME="Codex-Bridge-${APP_VERSION}-${RELEASE_CHANNEL}-${ARCH_LABEL}"
DMG_PATH="$OUTPUT_DIR/$ARTIFACT_BASENAME.dmg"
CHECKSUM_PATH="$DMG_PATH.sha256"

rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR"
ditto "$APP_PATH" "$STAGING_DIR/Codex Bridge.app"
ln -s /Applications "$STAGING_DIR/Applications"

hdiutil create \
  -volname "Codex Bridge Beta" \
  -srcfolder "$STAGING_DIR" \
  -ov \
  -format UDZO \
  "$DMG_PATH"

hdiutil verify "$DMG_PATH"

if [[ -n "${CODEX_BRIDGE_NOTARY_PROFILE:-}" ]]; then
  xcrun notarytool submit "$DMG_PATH" --keychain-profile "$CODEX_BRIDGE_NOTARY_PROFILE" --wait
  xcrun stapler staple "$DMG_PATH"
  xcrun stapler validate "$DMG_PATH"
fi

(
  cd "$OUTPUT_DIR"
  shasum -a 256 "$(basename "$DMG_PATH")" > "$(basename "$CHECKSUM_PATH")"
)

# 改名后的成功构建不再保留会让用户误装的旧品牌产物。
rm -rf "$LEGACY_APP_PATH" "$LEGACY_ROOT_APP_PATH"
rm -f "$LEGACY_DMG_PATH" "$OUTPUT_DIR/Codex Bridge.dmg"

printf 'Codex Bridge %s (%s)\nApp: %s\nDMG: %s\nSHA-256: %s\n' \
  "$APP_VERSION" "$APP_BUILD" "$APP_PATH" "$DMG_PATH" "$CHECKSUM_PATH"

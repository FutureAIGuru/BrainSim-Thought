#!/bin/bash
# Compiles the UKS C# library (../UKS) with .NET NativeAOT into a single static
# archive per Apple platform, which the BrainSimThought Xcode target links directly:
#
#   UKSNative/out/iphoneos/libUKSNative.a          (device, arm64)
#   UKSNative/out/iphonesimulator/libUKSNative.a   (simulator, arm64)
#
# Usage: ./build-uks.sh [iphoneos|iphonesimulator ...]   (default: both)
# Requires the .NET 10 SDK and Xcode.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
PROJ="$HERE/UKSNative"
DOTNET="$(command -v dotnet || echo /usr/local/share/dotnet/dotnet)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

platforms=("$@")
[ ${#platforms[@]} -eq 0 ] && platforms=(iphoneos iphonesimulator)

for platform in "${platforms[@]}"; do
  case "$platform" in
    iphoneos)        rid=ios-arm64 ;;
    iphonesimulator) rid=iossimulator-arm64 ;;
    *) echo "unknown platform: $platform" >&2; exit 1 ;;
  esac

  pub="$PROJ/out/pub-$rid"
  dest="$PROJ/out/$platform"
  echo "==> Publishing UKS for $rid"
  "$DOTNET" publish "$PROJ/UKSNative.csproj" -c Release -r "$rid" -o "$pub" -nologo -clp:ErrorsOnly

  # Fold the managed code archive and every NativeAOT runtime archive into one library.
  # The runtime bootstrapper (libbootstrapperdll.o) defines no external symbols -- it
  # only registers a static initializer that starts the runtime -- so the linker would
  # never pull it out of an archive. It is shipped as a loose object (UKSBootstrap.o)
  # that the Xcode target lists directly in OTHER_LDFLAGS.
  mkdir -p "$dest"
  inputs=("$pub/UKSNative.a")
  while IFS= read -r lib; do
    case "$lib" in
      "") ;;
      *.o) cp "$lib" "$dest/UKSBootstrap.o" ;;
      *) inputs+=("$lib") ;;
    esac
  done < "$pub/link-archives.txt"

  xcrun libtool -static -no_warning_for_no_symbols -o "$dest/libUKSNative.a" "${inputs[@]}"

  # Linker flags the app needs in addition to -lUKSNative (informational; the
  # Xcode target's OTHER_LDFLAGS already carries them).
  {
    sed 's/^/-l/' "$pub/link-system-libs.txt"
    sed 's/^/-framework /' "$pub/link-frameworks.txt"
  } | grep -v -e '^-l$' -e '^-framework $' > "$dest/linker-flags.txt"

  echo "==> $dest/libUKSNative.a ($(du -h "$dest/libUKSNative.a" | cut -f1))"
done

#!/usr/bin/env bash
set -euo pipefail
# Usage: bash Publish.sh [LinuxAot|LinuxJit|MacOSAot|MacOSJit]
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
workspace="$(cd -- "$script_dir/../../.." && pwd)"
case "$(uname -s)" in
    Linux) default_profile=LinuxAot; platform=Linux ;;
    Darwin) default_profile=MacOSAot; platform=MacOS ;;
    *) echo 'Use Publish.bat on Windows; Publish.sh supports Linux and macOS.' >&2; exit 2 ;;
esac
profile="${1:-$default_profile}"
if (( $# > 1 )); then
    echo 'Usage: bash Publish.sh [LinuxAot|LinuxJit|MacOSAot|MacOSJit]' >&2
    exit 2
fi
case "$profile" in
    "${platform}Aot"|"${platform}Jit") ;;
    *) echo "Unsupported profile for this host: $profile" >&2; exit 2 ;;
esac
if [[ ! -f "$script_dir/$profile.pubxml" ]]; then
    echo "Publish profile not found: $script_dir/$profile.pubxml" >&2
    exit 2
fi
cd -- "$workspace"
# RID, build mode and PublishDir come exclusively from the existing pubxml.
dotnet publish OneLauncher.Desktop/OneLauncher.Desktop.csproj \
    "-p:PublishProfile=$profile" --artifacts-path "build/output/$profile" \
    --disable-build-servers

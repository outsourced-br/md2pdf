#!/usr/bin/env bash
set -euo pipefail

source_root=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
dotnet_command=${DOTNET_COMMAND:-}
if [[ -z "$dotnet_command" ]]; then
  if command -v dotnet >/dev/null 2>&1; then
    dotnet_command=$(command -v dotnet)
  elif [[ -x /opt/dotnet/dotnet ]]; then
    dotnet_command=/opt/dotnet/dotnet
  else
    echo 'The .NET 10 SDK was not found.' >&2
    exit 2
  fi
fi

# VSTest's loopback transport can hang when a repository lives on a WSL DrvFS
# mount. Stage source on the Linux filesystem so this verifies Linux behavior
# rather than the Windows/WSL transport bridge.
stage=$(mktemp -d /tmp/md2pdf-verify.XXXXXX)
loopback_rule_added=false
loopback_rule=(priority 0 to 127.0.0.0/8 lookup local)
cleanup() {
  if [[ "$loopback_rule_added" == true ]]; then
    ip rule del "${loopback_rule[@]}" 2>/dev/null || true
    ip route flush cache
  fi
  case "$stage" in
    /tmp/md2pdf-verify.*) rm -rf -- "$stage" ;;
    *) echo "Refusing to remove unexpected staging path: $stage" >&2 ;;
  esac
}
trap cleanup EXIT HUP INT TERM

if ! ip route get 127.0.0.1 | grep -Eq 'local 127\.0\.0\.1 .* dev lo'; then
  if [[ $(id -u) -ne 0 ]]; then
    echo 'WSL loopback is misrouted; rerun as root for a temporary loopback rule.' >&2
    exit 3
  fi
  if ip rule show | grep -Fq 'to 127.0.0.0/8 lookup local'; then
    echo 'A loopback override already exists; refusing to add a duplicate.' >&2
    exit 3
  fi
  ip rule add "${loopback_rule[@]}"
  ip route flush cache
  loopback_rule_added=true
fi

tar \
  --exclude=.git \
  --exclude=artifacts \
  --exclude=dist \
  --exclude=tmp \
  --exclude='*/bin' \
  --exclude='*/obj' \
  -C "$source_root" -cf - . |
  tar -C "$stage" -xf -

cd "$stage"
"$dotnet_command" test Md2Pdf.slnx -c Release
"$dotnet_command" publish src/Md2Pdf.Cli/Md2Pdf.Cli.csproj \
  -c Release -r linux-x64 -o "$source_root/artifacts/publish/linux-x64"

binary="$source_root/artifacts/publish/linux-x64/md2pdf"
file "$binary" | grep -E 'ELF 64-bit.*x86-64'
if strings "$binary" | grep -Eq 'libcoreclr\.so|hostfxr\.so'; then
  echo 'Managed-runtime loader marker found.' >&2
  exit 1
fi
if ldd "$binary" | grep -Eq 'coreclr|hostfxr'; then
  echo 'Native binary links to a managed runtime.' >&2
  exit 1
fi
[[ "$("$binary" --version)" == '0.1.0' ]]

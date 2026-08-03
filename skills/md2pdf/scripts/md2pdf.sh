#!/usr/bin/env sh
set -eu

skill_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cli="$skill_root/bin/linux-x64/md2pdf"

if [ ! -x "$cli" ]; then
  printf 'Bundled MD2PDF executable not found or not executable: %s\n' "$cli" >&2
  exit 5
fi

has_json=false
for argument in "$@"; do
  if [ "$argument" = "--json" ]; then
    has_json=true
    break
  fi
done

if [ "$has_json" = true ]; then
  exec "$cli" "$@"
fi
exec "$cli" "$@" --json

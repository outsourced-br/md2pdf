#!/usr/bin/env sh
set -eu

version=0.1.0
repository=outsourced-br/md2pdf
install_claude=false
install_codex=false
install_cli=false
source_directory=
has_target=false
noninteractive=false

case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*)
    windows_installer=$(mktemp "${TMPDIR:-/tmp}/md2pdf-install.XXXXXX.ps1")
    cleanup_windows_installer() {
      rm -f -- "$windows_installer"
    }
    trap cleanup_windows_installer EXIT HUP INT TERM
    curl -fsSL \
      'https://raw.githubusercontent.com/outsourced-br/md2pdf/main/install.ps1' \
      -o "$windows_installer"
    windows_installer_native=$(cygpath -w "$windows_installer")
    powershell.exe -NoProfile -ExecutionPolicy Bypass \
      -File "$windows_installer_native" "$@"
    exit $?
    ;;
  Darwin)
    printf 'MD2PDF v0.1 does not yet support macOS.\n' >&2
    exit 2
    ;;
  Linux) ;;
  *)
    printf 'Unsupported operating system: %s\n' "$(uname -s)" >&2
    exit 2
    ;;
esac

if [ "$(uname -m)" != "x86_64" ]; then
  printf 'MD2PDF v0.1 supports only Linux x64.\n' >&2
  exit 2
fi

while [ "$#" -gt 0 ]; do
  case "$1" in
    --all)
      install_claude=true
      install_codex=true
      install_cli=true
      has_target=true
      ;;
    --claude)
      install_claude=true
      has_target=true
      ;;
    --codex)
      install_codex=true
      has_target=true
      ;;
    --cli)
      install_cli=true
      has_target=true
      ;;
    --explorer)
      printf 'Explorer integration is available only on Windows.\n' >&2
      exit 2
      ;;
    --source)
      shift
      [ "$#" -gt 0 ] || {
        printf '%s\n' '--source requires a directory.' >&2
        exit 2
      }
      source_directory=$1
      ;;
    --version)
      shift
      [ "$#" -gt 0 ] || {
        printf '%s\n' '--version requires a value.' >&2
        exit 2
      }
      version=$1
      ;;
    --yes) noninteractive=true ;;
    *)
      printf 'Unknown installer option: %s\n' "$1" >&2
      exit 2
      ;;
  esac
  shift
done

printf '\nMD2PDF installer\n'
printf 'Version %s | Linux x64 | per-user install\n\n' "$version"

if [ "$has_target" = false ] && [ "$noninteractive" = true ]; then
  install_claude=true
  install_codex=true
  install_cli=true
  has_target=true
fi

if [ "$has_target" = false ]; then
  printf 'Choose one or more targets (comma-separated):\n'
  printf '  1) Claude skill\n'
  printf '  2) Codex skill\n'
  printf '  3) CLI in ~/.local/bin\n'
  printf 'Selection [1,2,3]: '
  if [ -r /dev/tty ]; then
    IFS= read -r answer </dev/tty || answer=
  else
    answer=
  fi
  [ -n "$answer" ] || answer=1,2,3
  old_ifs=$IFS
  IFS=', '
  set -- $answer
  IFS=$old_ifs
  for choice in "$@"; do
    case "$choice" in
      1) install_claude=true ;;
      2) install_codex=true ;;
      3) install_cli=true ;;
      *)
        printf 'Unknown selection: %s\n' "$choice" >&2
        exit 2
        ;;
    esac
  done
fi

temporary=$(mktemp -d "${TMPDIR:-/tmp}/md2pdf-install.XXXXXX")
cleanup() {
  rm -rf -- "$temporary"
}
trap cleanup EXIT HUP INT TERM

get_asset() {
  asset_name=$1
  destination=$2
  if [ -n "$source_directory" ]; then
    [ -f "$source_directory/$asset_name" ] || {
      printf 'Installer asset not found: %s\n' "$source_directory/$asset_name" >&2
      exit 5
    }
    cp -- "$source_directory/$asset_name" "$destination"
  else
    printf 'Downloading %s...\n' "$asset_name"
    curl -fL --retry 3 \
      "https://github.com/$repository/releases/download/v$version/$asset_name" \
      -o "$destination"
  fi
}

assert_hash() {
  asset=$1
  checksums=$2
  asset_name=$(basename -- "$asset")
  expected=$(awk -v name="$asset_name" '
    length($1) == 64 && $1 ~ /^[0-9a-fA-F]+$/ {
      file=$2
      sub(/^\*/, "", file)
      sub(/\r$/, "", file)
      if (file == name) { print tolower($1); exit }
    }' "$checksums")
  [ -n "$expected" ] || {
    printf 'SHA256SUMS has no entry for %s.\n' "$asset_name" >&2
    exit 5
  }
  actual=$(sha256sum "$asset" | awk '{ print tolower($1) }')
  [ "$actual" = "$expected" ] || {
    printf 'SHA-256 mismatch for %s.\n' "$asset_name" >&2
    exit 5
  }
}

replace_directory() {
  source=$1
  destination=$2
  parent=$(dirname -- "$destination")
  mkdir -p -- "$parent"
  stage="$parent/.md2pdf-stage-$$"
  backup="$parent/.md2pdf-backup-$$"
  rm -rf -- "$stage" "$backup"
  cp -R -- "$source" "$stage"
  if [ -e "$destination" ]; then
    mv -- "$destination" "$backup"
  fi
  if mv -- "$stage" "$destination"; then
    rm -rf -- "$backup"
  else
    rm -rf -- "$destination" "$stage"
    if [ -e "$backup" ]; then
      mv -- "$backup" "$destination"
    fi
    return 1
  fi
}

checksums="$temporary/SHA256SUMS"
get_asset SHA256SUMS "$checksums"

skill_root=
if [ "$install_claude" = true ] || [ "$install_codex" = true ]; then
  skill_name="md2pdf-skill-$version.zip"
  skill_archive="$temporary/$skill_name"
  get_asset "$skill_name" "$skill_archive"
  assert_hash "$skill_archive" "$checksums"
  mkdir -p "$temporary/skill"
  if command -v unzip >/dev/null 2>&1; then
    unzip -q "$skill_archive" -d "$temporary/skill"
  elif command -v python3 >/dev/null 2>&1; then
    python3 -m zipfile -e "$skill_archive" "$temporary/skill"
  else
    printf 'The skill install requires unzip or Python 3.\n' >&2
    exit 5
  fi
  skill_root="$temporary/skill/md2pdf"
  [ -f "$skill_root/SKILL.md" ] || {
    printf 'Skill package is missing SKILL.md.\n' >&2
    exit 5
  }
fi

cli_root=
if [ "$install_cli" = true ]; then
  cli_name="md2pdf-$version-linux-x64.tar.gz"
  cli_archive="$temporary/$cli_name"
  get_asset "$cli_name" "$cli_archive"
  assert_hash "$cli_archive" "$checksums"
  cli_root="$temporary/cli"
  mkdir -p "$cli_root"
  tar -xzf "$cli_archive" -C "$cli_root"
  [ -x "$cli_root/md2pdf" ] || {
    printf 'Linux release archive is missing executable md2pdf.\n' >&2
    exit 5
  }
fi

if [ "$install_claude" = true ]; then
  claude_home=${CLAUDE_CONFIG_DIR:-"$HOME/.claude"}
  replace_directory "$skill_root" "$claude_home/skills/md2pdf"
  chmod 0755 \
    "$claude_home/skills/md2pdf/scripts/md2pdf.sh" \
    "$claude_home/skills/md2pdf/bin/linux-x64/md2pdf"
  printf 'Installed skill: %s\n' "$claude_home/skills/md2pdf"
fi

if [ "$install_codex" = true ]; then
  codex_home=${CODEX_HOME:-"$HOME/.codex"}
  replace_directory "$skill_root" "$codex_home/skills/md2pdf"
  chmod 0755 \
    "$codex_home/skills/md2pdf/scripts/md2pdf.sh" \
    "$codex_home/skills/md2pdf/bin/linux-x64/md2pdf"
  printf 'Installed skill: %s\n' "$codex_home/skills/md2pdf"
fi

if [ "$install_cli" = true ]; then
  mkdir -p "$HOME/.local/bin"
  cli_stage="$HOME/.local/bin/.md2pdf-stage-$$"
  cp -- "$cli_root/md2pdf" "$cli_stage"
  chmod 0755 "$cli_stage"
  staged_version=$(env -u DOTNET_ROOT "$cli_stage" --version)
  [ "$staged_version" = "$version" ] || {
    rm -f -- "$cli_stage"
    printf 'The staged Linux CLI failed its version probe.\n' >&2
    exit 5
  }
  mv -f -- "$cli_stage" "$HOME/.local/bin/md2pdf"
  printf 'Installed CLI: %s\n' "$HOME/.local/bin/md2pdf"

  case ":$PATH:" in
    *":$HOME/.local/bin:"*) ;;
    *)
      profile="$HOME/.profile"
      path_line='export PATH="$HOME/.local/bin:$PATH"'
      if [ ! -f "$profile" ] || ! grep -Fqx "$path_line" "$profile"; then
        printf '\n%s\n' "$path_line" >> "$profile"
      fi
      PATH="$HOME/.local/bin:$PATH"
      export PATH
      printf 'Added ~/.local/bin to PATH in ~/.profile.\n'
      ;;
  esac

  if ! "$HOME/.local/bin/md2pdf" doctor; then
    printf '%s\n' \
      'MD2PDF is installed, but no usable browser completed the print probe.' \
      'Install Chrome, Chromium, Edge, or Brave, or explicitly run: md2pdf browser install' >&2
  fi
fi

printf '\nMD2PDF installation complete.\n'
printf 'Conversion never downloads a browser. Use browser install only when you choose to.\n'

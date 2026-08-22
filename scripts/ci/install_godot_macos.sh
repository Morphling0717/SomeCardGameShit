#!/usr/bin/env bash
set -euo pipefail

version="4.7.2"
release_base="https://github.com/godotengine/godot-builds/releases/download/${version}-stable"
editor_archive="Godot_v${version}-stable_mono_macos.universal.zip"
template_archive="Godot_v${version}-stable_mono_export_templates.tpz"
editor_sha512="0862c53d7158c7a67f745e2e46f90b68cf5343cbe8b95d6d4333c469e42ca104af9c121d1746a50e5d221a99d09d82ef7016495f8e0d09255842884ed0502795"
template_sha512="bb5c41d72370ed743660361f6228006f808ab04ca33abdc545d740b044f3fe057f32ae8cb7873a1bc86ddcd82ae683b9f6dfdfe4179852f2c0f1acde2ff6bd5a"

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
tool_root="${1:-${root}/build/godot-toolchain/macos}"
template_root="${2:-${HOME}/Library/Application Support/Godot/export_templates/4.7.2.stable.mono}"
temporary_base="${RUNNER_TEMP:-${TMPDIR:-/tmp}}"
temporary_directory="$(mktemp -d "${temporary_base%/}/scgs-godot.XXXXXX")"
trap 'rm -rf -- "$temporary_directory"' EXIT

download_verified() {
  local name="$1"
  local expected="$2"
  local destination="${temporary_directory}/${name}"
  curl --fail --location --retry 3 --output "$destination" "${release_base}/${name}"
  local actual
  actual="$(shasum -a 512 "$destination" | awk '{print $1}')"
  if [[ "$actual" != "$expected" ]]; then
    echo "SHA-512 mismatch for ${name}: expected ${expected}, found ${actual}" >&2
    return 1
  fi
  printf '%s\n' "$destination"
}

templates_valid() {
  [[ -f "${template_root}/version.txt" ]] || return 1
  [[ "$(tr -d '\r\n' < "${template_root}/version.txt")" == "4.7.2.stable.mono" ]] || return 1
  [[ -f "${template_root}/windows_release_x86_64.exe" ]] || return 1
  [[ -f "${template_root}/macos.zip" ]] || return 1
  [[ -f "${template_root}/icudt_godot.dat" ]] || return 1
}

mkdir -p "$tool_root"
editor="$(find "$tool_root" -type f -path '*/Contents/MacOS/Godot' -print -quit)"
if [[ -z "$editor" ]]; then
  editor_zip="$(download_verified "$editor_archive" "$editor_sha512")"
  ditto -x -k "$editor_zip" "$tool_root"
  editor="$(find "$tool_root" -type f -path '*/Contents/MacOS/Godot' -print -quit)"
fi
if [[ -z "$editor" || ! -x "$editor" ]]; then
  echo "Godot .NET editor executable was not found under ${tool_root}" >&2
  exit 1
fi
if [[ ! -d "$(dirname "$(dirname "$editor")")/Resources/GodotSharp" ]]; then
  echo "The cached Godot .NET editor is missing its GodotSharp directory" >&2
  exit 1
fi

if ! templates_valid; then
  template_package="$(download_verified "$template_archive" "$template_sha512")"
  expanded_templates="${temporary_directory}/expanded-templates"
  mkdir -p "$expanded_templates"
  ditto -x -k "$template_package" "$expanded_templates"
  template_source=""
  while IFS= read -r candidate; do
    if [[ "$(tr -d '\r\n' < "$candidate")" == "4.7.2.stable.mono" ]]; then
      if [[ -n "$template_source" ]]; then
        echo "Multiple 4.7.2.stable.mono template roots were found" >&2
        exit 1
      fi
      template_source="$(dirname "$candidate")"
    fi
  done < <(find "$expanded_templates" -type f -name version.txt -print)
  if [[ -z "$template_source" ]]; then
    echo "No 4.7.2.stable.mono template root was found" >&2
    exit 1
  fi
  mkdir -p "$template_root"
  ditto "$template_source" "$template_root"
fi
if ! templates_valid; then
  echo "Godot .NET export template installation is incomplete: ${template_root}" >&2
  exit 1
fi

reported_version="$($editor --version)"
if [[ "$reported_version" != 4.7.2.stable.mono* ]]; then
  echo "Unexpected Godot version: ${reported_version}" >&2
  exit 1
fi
if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  printf 'godot=%s\n' "$editor" >> "$GITHUB_OUTPUT"
fi
printf 'Godot %s ready at %s\n' "$reported_version" "$editor"

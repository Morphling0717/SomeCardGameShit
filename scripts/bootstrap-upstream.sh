#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
lock="$root/upstream/upstream.lock.json"

command -v python3 >/dev/null || { echo "python3 is required" >&2; exit 1; }
command -v git >/dev/null || { echo "git is required" >&2; exit 1; }

read_dep() {
  local key="$1"
  python3 - "$lock" "$key" <<'PY'
import json, sys
lock, key = sys.argv[1:]
data = json.load(open(lock, encoding="utf-8"))["dependencies"][key]
print(data["url"])
print(data["revision"])
print(data["path"])
PY
}

checkout_dep() {
  local key="$1"
  mapfile -t values < <(read_dep "$key")
  local url="${values[0]}"
  local revision="${values[1]}"
  local relative="${values[2]}"
  local target="$root/$relative"

  if [[ -d "$target/.git" ]]; then
    if [[ -n "$(git -C "$target" status --porcelain)" ]]; then
      echo "$relative has local changes; refusing to overwrite" >&2
      exit 1
    fi
    git -C "$target" fetch --tags --prune origin
  elif [[ -e "$target" ]]; then
    echo "$relative exists but is not a git checkout" >&2
    exit 1
  else
    mkdir -p "$(dirname "$target")"
    git clone --filter=blob:none --no-checkout "$url" "$target"
  fi

  git -C "$target" checkout --detach "$revision"
  local actual
  actual="$(git -C "$target" rev-parse HEAD)"
  [[ "$actual" == "$revision" ]] || {
    echo "$relative resolved to $actual instead of $revision" >&2
    exit 1
  }
  echo "ready: $relative @ $actual"
}

checkout_dep YGOProUnity_V2
checkout_dep ygopro-core

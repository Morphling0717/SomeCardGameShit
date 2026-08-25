#!/bin/sh
set -eu

launcher_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
application="$launcher_directory/SomeCardGameShit.app"
executable="$application/Contents/MacOS/SomeCardGameShit"

if [ ! -x "$executable" ]; then
  echo "SomeCardGameShit.app was not found beside this launcher." >&2
  exit 1
fi

if [ "${SCGS_ANIME_LAUNCHER_CI:-0}" = "1" ]; then
  if [ -z "${SCGS_ANIME_LAUNCHER_OUTPUT:-}" ]; then
    echo "SCGS_ANIME_LAUNCHER_OUTPUT is required when SCGS_ANIME_LAUNCHER_CI=1." >&2
    exit 2
  fi
  exec "$executable" --windowed --audio-driver Dummy --resolution 1600x900 -- \
    "--anime-style-slice=${SCGS_ANIME_LAUNCHER_OUTPUT}" \
    --anime-style-slice-exit "--ci-visual-viewport=1600x900"
fi

# Finder double-clicks use the same standalone, no-native path as source runs.
exec "$executable" --windowed --resolution 1600x900 -- --anime-style-slice

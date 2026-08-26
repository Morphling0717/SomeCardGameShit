#!/bin/sh
set -eu

launcher_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
executable="$launcher_directory/SomeCardGameShit.app/Contents/MacOS/SomeCardGameShit"

if [ ! -x "$executable" ]; then
  echo "SomeCardGameShit.app was not found beside this launcher." >&2
  exit 1
fi

if [ "${SCGS_CARD_BODY_LAUNCHER_CI:-0}" = "1" ]; then
  : "${SCGS_CARD_BODY_LAUNCHER_OUTPUT:?SCGS_CARD_BODY_LAUNCHER_OUTPUT is required in CI mode}"
  exec "$executable" --windowed --audio-driver Dummy --resolution 1024x684 -- \
    "--anime-card-body-slice=$SCGS_CARD_BODY_LAUNCHER_OUTPUT" \
    --anime-card-body-slice-exit \
    --ci-visual-viewport=1024x684 \
    --ci-anime-runner-viewport
fi

exec "$executable" --windowed --resolution 1600x900 -- --anime-card-body-slice

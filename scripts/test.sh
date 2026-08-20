#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

build_jobs="${SCGS_BUILD_JOBS:-2}"

cmake --preset dev
cmake --build --preset dev --parallel "$build_jobs"
ctest --preset dev
./build/dev/scgs_demo --verify

cmake --preset release
cmake --build --preset release --parallel "$build_jobs"
ctest --preset release

if command -v clang++ >/dev/null 2>&1; then
  cmake --preset asan
  cmake --build --preset asan --parallel "$build_jobs"
  ASAN_OPTIONS="${ASAN_OPTIONS:-detect_leaks=1:halt_on_error=1}" \
  UBSAN_OPTIONS="${UBSAN_OPTIONS:-halt_on_error=1:print_stacktrace=1}" \
    ctest --preset asan
else
  echo "clang++ not found; sanitizer build skipped" >&2
fi

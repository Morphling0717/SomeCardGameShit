#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

build_jobs="${SCGS_BUILD_JOBS:-2}"
release_seeds="${SCGS_RELEASE_STRESS_SEEDS:-2048}"
asan_seeds="${SCGS_ASAN_STRESS_SEEDS:-256}"

cmake --preset release
cmake --build --preset release --parallel "$build_jobs"
SCGS_SMOKE_SEEDS="$release_seeds" ./build/release/scgs_tests

if command -v clang++ >/dev/null 2>&1; then
  cmake --preset asan
  cmake --build --preset asan --parallel "$build_jobs"
  ASAN_OPTIONS="${ASAN_OPTIONS:-detect_leaks=1:halt_on_error=1}" \
  UBSAN_OPTIONS="${UBSAN_OPTIONS:-halt_on_error=1:print_stacktrace=1}" \
  SCGS_SMOKE_SEEDS="$asan_seeds" \
    ./build/asan/scgs_tests
else
  echo "clang++ not found; sanitizer stress run skipped" >&2
fi

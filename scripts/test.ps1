$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$Build = Join-Path $Root "build/windows"

cmake -S $Root -B $Build -A x64 -DSCGS_WARNINGS_AS_ERRORS=ON
cmake --build $Build --config Release --parallel 2
ctest --test-dir $Build -C Release --output-on-failure
& (Join-Path $Build "Release/scgs_demo.exe") --verify

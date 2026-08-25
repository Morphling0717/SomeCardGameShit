param(
    [Parameter(Mandatory = $true)]
    [string]$GodotPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,

    [string]$PythonPath = "python",

    [string[]]$Viewports = @("1280x720", "1600x900", "2560x1440", "2560x1600"),

    [switch]$AllowCiRunnerViewport,

    [switch]$AllowMissingAssets
)

$ErrorActionPreference = "Stop"
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$projectPath = Join-Path $workspaceRoot "client/godot"
$validator = Join-Path $workspaceRoot "scripts/ci/validate_anime_visual_slice.py"
$timeoutRunner = Join-Path $workspaceRoot "scripts/ci/run_with_timeout.py"
$resolvedGodot = (Resolve-Path -LiteralPath $GodotPath).Path
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null
foreach ($viewport in $Viewports) {
    $captureDirectory = Join-Path $resolvedOutput $viewport
    New-Item -ItemType Directory -Force -Path $captureDirectory | Out-Null
    $captureArguments = @(
        "--path", $projectPath,
        "--windowed",
        "--audio-driver", "Dummy",
        "--resolution", $viewport,
        "--",
        "--anime-style-slice=$captureDirectory",
        "--anime-style-slice-exit",
        "--ci-visual-viewport=$viewport"
    )
    if ($AllowCiRunnerViewport) {
        $captureArguments += "--ci-anime-runner-viewport"
    }
    & $PythonPath $timeoutRunner `
        --timeout 600 `
        --expect-output SCGS_ANIME_VISUAL_SLICE_READY `
        --expect-output-count 1 `
        --forbid-output "SCRIPT ERROR:" `
        --forbid-output "ERROR:" `
        --forbid-output "Unhandled exception" `
        -- $resolvedGodot @captureArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Godot AnimeV1 capture failed for $viewport with exit code $LASTEXITCODE."
    }

    $arguments = @(
        $validator,
        (Join-Path $captureDirectory "anime-visual-slice.json"),
        "--expected-viewport", $viewport
    )
    if ($AllowMissingAssets) {
        $arguments += "--allow-missing-assets"
    }
    if ($AllowCiRunnerViewport) {
        $arguments += "--allow-ci-runner-viewport"
    }
    & $PythonPath @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "AnimeV1 report validation failed for $viewport."
    }
}

Write-Host "SCGS_ANIME_VISUAL_SLICE_MATRIX_OK output=$resolvedOutput"

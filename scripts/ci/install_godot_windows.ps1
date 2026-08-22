[CmdletBinding()]
param(
    [string]$ToolRoot = (Join-Path $PSScriptRoot "../../build/godot-toolchain/windows"),
    [string]$TemplateRoot = (Join-Path $env:APPDATA "Godot/export_templates/4.7.2.stable.mono"),
    [string]$GithubOutput = $env:GITHUB_OUTPUT
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$Version = "4.7.2"
$ReleaseBase = "https://github.com/godotengine/godot-builds/releases/download/$Version-stable"
$EditorArchive = "Godot_v$Version-stable_mono_win64.zip"
$TemplateArchive = "Godot_v$Version-stable_mono_export_templates.tpz"
$EditorSha512 = "79229fd112b0c9cbeab82363a4ef7be18ea70f1caf86bf912789335b136fbe7e01db0053a33461438c5da1c680c17bbb10040bd09bedc51221cd4423d0367757"
$TemplateSha512 = "bb5c41d72370ed743660361f6228006f808ab04ca33abdc545d740b044f3fe057f32ae8cb7873a1bc86ddcd82ae683b9f6dfdfe4179852f2c0f1acde2ff6bd5a"

function Get-VerifiedArchive {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$ExpectedSha512,
        [Parameter(Mandatory = $true)][string]$TemporaryDirectory
    )

    $Path = Join-Path $TemporaryDirectory $Name
    Invoke-WebRequest -Uri "$ReleaseBase/$Name" -OutFile $Path
    $Actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA512).Hash.ToLowerInvariant()
    if ($Actual -ne $ExpectedSha512) {
        throw "SHA-512 mismatch for $Name`: expected $ExpectedSha512, found $Actual"
    }
    return $Path
}

function Test-TemplateInstall {
    param([Parameter(Mandatory = $true)][string]$Path)

    $VersionFile = Join-Path $Path "version.txt"
    if (-not (Test-Path -LiteralPath $VersionFile -PathType Leaf)) {
        return $false
    }
    if ((Get-Content -LiteralPath $VersionFile -Raw).Trim() -ne "4.7.2.stable.mono") {
        return $false
    }
    foreach ($Required in @("windows_release_x86_64.exe", "macos.zip", "icudt_godot.dat")) {
        if (-not (Test-Path -LiteralPath (Join-Path $Path $Required) -PathType Leaf)) {
            return $false
        }
    }
    return $true
}

$TemporaryBase = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$TemporaryDirectory = Join-Path $TemporaryBase ("scgs-godot-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $TemporaryDirectory -Force | Out-Null
$ResolvedTemporaryBase = [IO.Path]::GetFullPath($TemporaryBase).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$ResolvedTemporaryDirectory = [IO.Path]::GetFullPath($TemporaryDirectory)
if (-not $ResolvedTemporaryDirectory.StartsWith($ResolvedTemporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to manage a temporary directory outside $ResolvedTemporaryBase"
}

try {
    New-Item -ItemType Directory -Path $ToolRoot -Force | Out-Null

$EditorCandidates = @(Get-ChildItem -LiteralPath $ToolRoot -Recurse -File -Filter "Godot_v$Version-stable_mono_win64.exe")
if ($EditorCandidates.Count -eq 0) {
    $EditorZip = Get-VerifiedArchive -Name $EditorArchive -ExpectedSha512 $EditorSha512 -TemporaryDirectory $TemporaryDirectory
    [IO.Compression.ZipFile]::ExtractToDirectory($EditorZip, $ToolRoot, $true)
    $EditorCandidates = @(Get-ChildItem -LiteralPath $ToolRoot -Recurse -File -Filter "Godot_v$Version-stable_mono_win64.exe")
}
if ($EditorCandidates.Count -ne 1) {
    throw "Expected exactly one Godot .NET editor under $ToolRoot, found $($EditorCandidates.Count)"
}
$Editor = $EditorCandidates[0].FullName
if (-not (Test-Path -LiteralPath (Join-Path $EditorCandidates[0].Directory.FullName "GodotSharp") -PathType Container)) {
    throw "The cached Godot .NET editor is missing its adjacent GodotSharp directory"
}

if (-not (Test-TemplateInstall -Path $TemplateRoot)) {
    $TemplatePackage = Get-VerifiedArchive -Name $TemplateArchive -ExpectedSha512 $TemplateSha512 -TemporaryDirectory $TemporaryDirectory
    $ExpandedTemplates = Join-Path $TemporaryDirectory "expanded-templates"
    New-Item -ItemType Directory -Path $ExpandedTemplates -Force | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($TemplatePackage, $ExpandedTemplates, $true)
    $VersionFiles = @(
        Get-ChildItem -LiteralPath $ExpandedTemplates -Recurse -File -Filter "version.txt" |
            Where-Object { (Get-Content -LiteralPath $_.FullName -Raw).Trim() -eq "4.7.2.stable.mono" }
    )
    if ($VersionFiles.Count -ne 1) {
        throw "Expected exactly one 4.7.2.stable.mono template root, found $($VersionFiles.Count)"
    }
    New-Item -ItemType Directory -Path $TemplateRoot -Force | Out-Null
    Get-ChildItem -LiteralPath $VersionFiles[0].Directory.FullName -Force |
        Copy-Item -Destination $TemplateRoot -Recurse -Force
}
if (-not (Test-TemplateInstall -Path $TemplateRoot)) {
    throw "Godot .NET export template installation is incomplete: $TemplateRoot"
}

$ReportedVersion = (& $Editor --version | Out-String).Trim()
if (-not $ReportedVersion.StartsWith("4.7.2.stable.mono", [StringComparison]::Ordinal)) {
    throw "Unexpected Godot version: $ReportedVersion"
}
    if ($GithubOutput) {
        Add-Content -LiteralPath $GithubOutput -Value "godot=$Editor" -Encoding utf8
    }
    Write-Host "Godot $ReportedVersion ready at $Editor"
}
finally {
    if (Test-Path -LiteralPath $ResolvedTemporaryDirectory) {
        Remove-Item -LiteralPath $ResolvedTemporaryDirectory -Recurse -Force
    }
}

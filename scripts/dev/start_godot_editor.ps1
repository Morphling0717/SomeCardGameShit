# SPDX-License-Identifier: GPL-3.0-or-later
[CmdletBinding()]
param(
    [string]$GodotPath = '',
    [string]$DotnetRoot = (Join-Path $env:USERPROFILE '.dotnet')
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$projectRoot = Join-Path $repositoryRoot 'client/godot'
if (-not $GodotPath) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs/Godot/4.7.2-mono/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe'),
        (Join-Path $repositoryRoot 'build/godot-toolchain/windows/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe')
    )
    $GodotPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $GodotPath -or -not [IO.Path]::IsPathFullyQualified($GodotPath) -or -not (Test-Path -LiteralPath $GodotPath)) {
    throw 'Provide -GodotPath with the absolute path to Godot 4.7.2 .NET.'
}
if (-not (Test-Path -LiteralPath (Join-Path $DotnetRoot 'sdk/10.0.400/Microsoft.Build.dll'))) {
    throw 'The locked .NET SDK 10.0.400 is missing from -DotnetRoot.'
}
$editorVersion = (& $GodotPath --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $editorVersion -notlike '4.7.2.stable.mono.*') {
    throw "Unexpected Godot version: $editorVersion"
}
$normalizedProject = $projectRoot.Replace('\', '/').ToLowerInvariant()
$existingEditors = @(Get-CimInstance Win32_Process -Filter "Name LIKE 'Godot%exe'" | Where-Object {
    $_.CommandLine -and $_.CommandLine -match '(?:^|\s)--editor(?:\s|$)' -and
    $_.CommandLine.Replace('\', '/').ToLowerInvariant().Contains($normalizedProject)
})
if ($existingEditors.Count -gt 0) {
    Write-Output "This project's editor is already open (PID $($existingEditors.ProcessId -join ', ')); no second editor started."
    return
}
$logDirectory = Join-Path $repositoryRoot 'build/godot-mcp-acceptance'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$logPath = Join-Path $logDirectory ('editor-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $GodotPath
$startInfo.WorkingDirectory = $projectRoot
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.Environment['DOTNET_ROOT'] = $DotnetRoot
# Godot's MSBuild discovery also resolves dotnet from PATH, not only DOTNET_ROOT.
$startInfo.Environment['PATH'] = $DotnetRoot + [IO.Path]::PathSeparator + $env:PATH
foreach ($argument in @('--editor', '--path', $projectRoot, '--log-file', $logPath)) {
    $startInfo.ArgumentList.Add($argument)
}
$editorProcess = [Diagnostics.Process]::Start($startInfo)
[PSCustomObject]@{ ProcessId = $editorProcess.Id; Project = $projectRoot; Godot = $editorVersion; Log = $logPath }

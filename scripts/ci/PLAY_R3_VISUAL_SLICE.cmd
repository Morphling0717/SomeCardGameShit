@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "SCGS_R3_APP=%~dp0SomeCardGameShit.exe"

if not exist "%SCGS_R3_APP%" (
  echo SomeCardGameShit.exe was not found beside this launcher.
  exit /b 1
)

if /I "%SCGS_R3_LAUNCHER_CI%"=="1" goto ci_mode

rem The double-click path stays interactive: render beside the package and
rem leave the candidate window open for human review.
set "SCGS_R3_OUTPUT=%~dp0r3-visual-slice-output"
"%SCGS_R3_APP%" --windowed --resolution "1600x900" -- "--r3-visual-slice=%SCGS_R3_OUTPUT%"
exit /b %ERRORLEVEL%

:ci_mode
rem CI supplies a trusted absolute path through the environment. Keeping it
rem quoted preserves spaces, while the fixed mode switch prevents arbitrary
rem command-line fragments from being supplied through the environment.
if not defined SCGS_R3_LAUNCHER_OUTPUT (
  echo SCGS_R3_LAUNCHER_OUTPUT is required when SCGS_R3_LAUNCHER_CI=1.
  exit /b 2
)
set "SCGS_R3_OUTPUT=%SCGS_R3_LAUNCHER_OUTPUT%"
"%SCGS_R3_APP%" --windowed --audio-driver Dummy --resolution "1600x900" -- "--r3-visual-slice=%SCGS_R3_OUTPUT%" --r3-visual-slice-exit "--ci-visual-viewport=1600x900"
exit /b %ERRORLEVEL%

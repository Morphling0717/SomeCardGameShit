@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "SCGS_CARD_BODY_APP=%~dp0SomeCardGameShit.exe"

if not exist "%SCGS_CARD_BODY_APP%" (
  echo SomeCardGameShit.exe was not found beside this launcher.
  exit /b 1
)

if /I "%SCGS_CARD_BODY_LAUNCHER_CI%"=="1" goto ci_mode
"%SCGS_CARD_BODY_APP%" --windowed --resolution "1600x900" -- --anime-card-body-slice
exit /b %ERRORLEVEL%

:ci_mode
if not defined SCGS_CARD_BODY_LAUNCHER_OUTPUT (
  echo SCGS_CARD_BODY_LAUNCHER_OUTPUT is required in CI mode.
  exit /b 2
)
"%SCGS_CARD_BODY_APP%" --windowed --audio-driver Dummy --resolution "1600x900" -- "--anime-card-body-slice=%SCGS_CARD_BODY_LAUNCHER_OUTPUT%" --anime-card-body-slice-exit --ci-visual-viewport=1600x900
exit /b %ERRORLEVEL%

@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "SCGS_ANIME_APP=%~dp0SomeCardGameShit.exe"

if not exist "%SCGS_ANIME_APP%" (
  echo SomeCardGameShit.exe was not found beside this launcher.
  exit /b 1
)

if /I "%SCGS_ANIME_LAUNCHER_CI%"=="1" goto ci_mode

rem Double-clicking this file opens the standalone, no-native AnimeV1 sample.
rem It deliberately does not change the normal product entry point.
"%SCGS_ANIME_APP%" --windowed --resolution "1600x900" -- --anime-style-slice
exit /b %ERRORLEVEL%

:ci_mode
rem CI provides a trusted absolute capture directory. The mode and viewport
rem arguments remain fixed so the environment cannot inject command switches.
if not defined SCGS_ANIME_LAUNCHER_OUTPUT (
  echo SCGS_ANIME_LAUNCHER_OUTPUT is required when SCGS_ANIME_LAUNCHER_CI=1.
  exit /b 2
)
set "SCGS_ANIME_OUTPUT=%SCGS_ANIME_LAUNCHER_OUTPUT%"
"%SCGS_ANIME_APP%" --windowed --audio-driver Dummy --resolution "1600x900" -- "--anime-style-slice=%SCGS_ANIME_OUTPUT%" --anime-style-slice-exit "--ci-visual-viewport=1600x900"
exit /b %ERRORLEVEL%

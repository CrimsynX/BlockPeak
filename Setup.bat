@echo off
rem BlockPeak setup: double-click this file.
title BlockPeak Setup
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup\BlockPeakSetup.ps1" %*
if errorlevel 1 (
  echo.
  echo Something went wrong. See setup\setup-log.txt
  pause
)

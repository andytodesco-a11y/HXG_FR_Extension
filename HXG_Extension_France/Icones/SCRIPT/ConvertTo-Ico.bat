@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0ConvertTo-Ico.ps1" -InputPath "%~1"
pause

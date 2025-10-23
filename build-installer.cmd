@echo off
REM Wrapper to run build-installer.ps1 with execution policy bypass
REM This allows the script to run without changing system-wide PowerShell execution policies

powershell.exe -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" %*


@echo off
chcp 65001 >nul

set "SCRIPT_DIR=%~dp0"

powershell -ExecutionPolicy Bypass -File "%SCRIPT_DIR%start.ps1"

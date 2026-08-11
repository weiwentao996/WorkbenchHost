@echo off
setlocal
set "HOST=%~dp0WorkbenchHost.exe"

if not exist "%HOST%" (
    call "%~dp0build.cmd"
    if errorlevel 1 exit /b 1
)

start "" "%HOST%"

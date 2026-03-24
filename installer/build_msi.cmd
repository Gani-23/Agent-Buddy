@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0.."
set "STAGING=%ROOT%\installer\staging"
set "WXS=%ROOT%\installer\AgentBuddy.wxs"
set "OUT=%ROOT%\installer\AgentBuddy_Win11_20260320.msi"

if not exist "%STAGING%" (
  echo Staging folder not found: %STAGING%
  echo Run packaging steps to create staging first.
  exit /b 1
)

dotnet tool restore
if errorlevel 1 exit /b 1

dotnet tool run wix extension add WixToolset.UI.wixext >nul 2>nul

dotnet tool run wix build -arch x64 -ext WixToolset.UI.wixext -d SourceDir="%STAGING%" -o "%OUT%" "%WXS%"
if errorlevel 1 exit /b 1

echo MSI created: %OUT%
exit /b 0

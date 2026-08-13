@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework C# compiler was not found.
  exit /b 1
)
if not exist "%~dp0assets\codicon.ttf" (
  echo VS Code Codicon font was not found: assets\codicon.ttf
  exit /b 1
)
if not exist "%~dp0assets\seti.ttf" (
  echo VS Code Seti icon font was not found: assets\seti.ttf
  exit /b 1
)
if not exist "%~dp0profiles" mkdir "%~dp0profiles"
"%CSC%" /nologo /target:winexe /optimize+ /out:"%~dp0.\WorkbenchHost.exe" /win32icon:"%~dp0icon.ico" /resource:"%~dp0assets\codicon.ttf",WorkbenchHost.codicon.ttf /resource:"%~dp0assets\seti.ttf",WorkbenchHost.seti.ttf /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "%~dp0Program.cs" "%~dp0Profile.cs" "%~dp0ProfileImporter.cs" "%~dp0WorkspaceState.cs" "%~dp0NativeMethods.cs" "%~dp0WorkbenchTheme.cs" "%~dp0CodePanel.cs" "%~dp0QuickImportDialog.cs" "%~dp0MainForm.cs"
if errorlevel 1 exit /b %errorlevel%
echo Built WorkbenchHost.exe

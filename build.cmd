@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework C# compiler was not found.
  exit /b 1
)
if not exist "%~dp0profiles" mkdir "%~dp0profiles"
"%CSC%" /nologo /target:winexe /optimize+ /out:"%~dp0.\WorkbenchHost.exe" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "%~dp0Program.cs" "%~dp0Profile.cs" "%~dp0ProfileImporter.cs" "%~dp0WorkspaceState.cs" "%~dp0NativeMethods.cs" "%~dp0CodePanel.cs" "%~dp0QuickImportDialog.cs" "%~dp0MainForm.cs"
if errorlevel 1 exit /b %errorlevel%
echo Built WorkbenchHost.exe

@echo off
REM One-shot local build. Requires VS 2022 (any edition) with the
REM "Visual Studio extension development" workload installed.
setlocal

set VSWHERE="%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist %VSWHERE% (
    echo vswhere.exe not found - is Visual Studio 2022 installed?
    exit /b 1
)

for /f "usebackq tokens=*" %%i in (`%VSWHERE% -latest -requires Microsoft.VisualStudio.Workload.VisualStudioExtension -find MSBuild\**\Bin\MSBuild.exe`) do set MSBUILD=%%i

if "%MSBUILD%"=="" (
    echo Could not find MSBuild with the VS extension development workload.
    echo Open the Visual Studio Installer and add "Visual Studio extension development".
    exit /b 1
)

echo Using: %MSBUILD%
"%MSBUILD%" SsmsSqlFormatter.sln /t:Restore /p:Configuration=Release || exit /b 1
"%MSBUILD%" SsmsSqlFormatter.sln /p:Configuration=Release /p:DeployExtension=false /m || exit /b 1

echo.
echo Done. VSIX is at: src\SsmsSqlFormatter\bin\Release\SsmsSqlFormatter.vsix
echo Double-click it, or install into SSMS with:
echo "C:\Program Files\Microsoft SQL Server Management Studio 21\Release\Common7\IDE\VSIXInstaller.exe" src\SsmsSqlFormatter\bin\Release\SsmsSqlFormatter.vsix
endlocal

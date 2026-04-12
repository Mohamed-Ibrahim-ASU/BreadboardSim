@echo off
:: 1. Safety Check: Verify we are in the correct root folder
if not exist "BreadboardSim_Final.sln" (
    echo [ERROR] Please run this bat from the project root folder!
    pause
    exit /b
)

set DIST_DIR=_package
set ZIP_NAME=BreadboardSim-MI.zip

echo [1/4] Cleaning old package...
if exist "%DIST_DIR%" rmdir /s /q "%DIST_DIR%"
if exist "%ZIP_NAME%" del "%ZIP_NAME%"
mkdir "%DIST_DIR%"

echo [2/4] Copying fresh Release binaries...
:: Copying the UI
if exist "SimGUI\bin\Release\BreadboardSim.exe" (
    copy "SimGUI\bin\Release\BreadboardSim.exe" "%DIST_DIR%\" >nul
) else (
    echo [ERROR] BreadboardSim.exe not found in bin\Release!
    pause
    exit /b
)

:: Copying the Resources (which includes the simbe.exe engine)
xcopy "SimGUI\bin\Release\res" "%DIST_DIR%\res" /s /i /y /q >nul

echo [3/4] Adding legal and cleaning dust...
if exist "LICENSE" copy "LICENSE" "%DIST_DIR%\LICENSE" >nul
del "%DIST_DIR%\*.pdb" /q /f >nul 2>&1
del "%DIST_DIR%\res\*.pdb" /q /f >nul 2>&1

echo [4/4] Zipping package...
:: Using the .NET Framework directly to bypass PowerShell bugs with parentheses
powershell -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::CreateFromDirectory('%CD%\%DIST_DIR%', '%CD%\%ZIP_NAME%')"

echo.
echo ======================================================
echo SUCCESS: %ZIP_NAME% is created in the root folder.
echo You can now upload this file to your GitHub Release.
echo ======================================================
pause
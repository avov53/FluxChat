@echo off
setlocal

set "ROOT=%~dp0"
set "DIST=%ROOT%dist-badge-authority-linux"
set "PROJECT=%ROOT%FluxChat.BadgeAuthority\FluxChat.BadgeAuthority.csproj"

echo Building Official Badge Authority for Ubuntu/Linux x64...

if exist "%DIST%" rmdir /s /q "%DIST%"

dotnet publish "%PROJECT%" ^
    -m:1 ^
    -c Release ^
    -r linux-x64 ^
    --self-contained true ^
    -o "%DIST%" ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    -p:NuGetAudit=false

if errorlevel 1 (
    echo.
    echo Badge Authority publish failed.
    exit /b 1
)

for %%F in ("%DIST%\*.pdb") do if exist "%%~fF" del /q "%%~fF"

echo.
echo Done: %DIST%\FluxChat.BadgeAuthority
echo No authority private key or database was included.

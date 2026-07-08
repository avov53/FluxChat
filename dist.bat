@echo off
setlocal

set "ROOT=%~dp0"
set "DIST=%ROOT%dist"
set "PROJECT=%ROOT%FluxChat.Client\FluxChat.Client.csproj"

echo Building FluxChat single-file distribution...

if exist "%DIST%" (
    rmdir /s /q "%DIST%"
)

dotnet publish "%PROJECT%" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -o "%DIST%" ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false

if errorlevel 1 (
    echo.
    echo Publish failed.
    exit /b 1
)

for %%F in ("%DIST%\*.pdb") do (
    if exist "%%~fF" del /q "%%~fF"
)

if exist "%DIST%\FluxChat.Client.exe" (
    ren "%DIST%\FluxChat.Client.exe" "FluxChat.exe"
)

echo.
echo Done: %DIST%\FluxChat.exe

@echo off
setlocal

set "ROOT=%~dp0"
set "DIST=%ROOT%dist"
set "PROJECT=%ROOT%FluxChat.Client\FluxChat.Client.csproj"
set "TOOLS=%ROOT%tools"
set "FFMPEG_URL=https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
set "FFMPEG_ZIP=%TOOLS%\ffmpeg-release-essentials.zip"

echo Building FluxChat single-file distribution...
echo Root: %ROOT%
echo Dist: %DIST%

powershell -NoProfile -Command "if (Get-Process -Name FluxChat -ErrorAction SilentlyContinue) { exit 1 }"
if errorlevel 1 (
    echo FluxChat.exe is running. Close FluxChat before rebuilding dist.
    exit /b 1
)

if exist "%DIST%" (
    rmdir /s /q "%DIST%"
)

if exist "%DIST%" (
    echo Failed to clean dist folder. Close FluxChat.exe if it is running and try again.
    exit /b 1
)

dotnet publish "%PROJECT%" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -o "%DIST%" ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:NuGetAudit=false

if errorlevel 1 (
    echo.
    echo Publish failed.
    exit /b 1
)

if exist "%DIST%\FluxChat.Client.exe" (
    move /y "%DIST%\FluxChat.Client.exe" "%DIST%\FluxChat.exe" >nul
)

if not exist "%DIST%\FluxChat.exe" (
    echo.
    echo Publish finished, but FluxChat.exe was not found in dist.
    exit /b 1
)

for %%F in ("%DIST%\*.pdb") do (
    if exist "%%~fF" del /q "%%~fF"
)

call :include_ffmpeg
if errorlevel 1 (
    echo.
    echo FFmpeg setup failed.
    exit /b 1
)

echo.
echo Done: %DIST%\FluxChat.exe
if exist "%DIST%\ffmpeg.exe" (
    echo Done: %DIST%\ffmpeg.exe
)
echo Dist folder is ready.
exit /b 0

:include_ffmpeg
echo.
echo Adding FFmpeg for hardware H.264 screen share...

if exist "%TOOLS%\ffmpeg.exe" (
    copy /y "%TOOLS%\ffmpeg.exe" "%DIST%\ffmpeg.exe" >nul
    echo FFmpeg copied from tools\ffmpeg.exe.
    exit /b 0
)

set "FFMPEG_PATH="
for %%I in (ffmpeg.exe) do set "FFMPEG_PATH=%%~$PATH:I"
if defined FFMPEG_PATH (
    copy /y "%FFMPEG_PATH%" "%DIST%\ffmpeg.exe" >nul
    echo FFmpeg copied from PATH.
    exit /b 0
)

if not exist "%TOOLS%" (
    mkdir "%TOOLS%"
)

echo FFmpeg not found locally. Downloading release build...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ErrorActionPreference='Stop';" ^
    "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;" ^
    "Invoke-WebRequest -Uri '%FFMPEG_URL%' -OutFile '%FFMPEG_ZIP%';" ^
    "$extract=Join-Path '%TOOLS%' 'ffmpeg-extract';" ^
    "if (Test-Path $extract) { Remove-Item -LiteralPath $extract -Recurse -Force };" ^
    "Expand-Archive -LiteralPath '%FFMPEG_ZIP%' -DestinationPath $extract -Force;" ^
    "$ffmpeg=Get-ChildItem -LiteralPath $extract -Recurse -Filter ffmpeg.exe | Select-Object -First 1;" ^
    "if (-not $ffmpeg) { throw 'ffmpeg.exe not found in downloaded archive' };" ^
    "Copy-Item -LiteralPath $ffmpeg.FullName -Destination '%TOOLS%\ffmpeg.exe' -Force;" ^
    "Copy-Item -LiteralPath $ffmpeg.FullName -Destination '%DIST%\ffmpeg.exe' -Force;" ^
    "Remove-Item -LiteralPath $extract -Recurse -Force"

if errorlevel 1 (
    echo Failed to download or extract FFmpeg.
    exit /b 1
)

if not exist "%DIST%\ffmpeg.exe" (
    echo FFmpeg was not copied to dist.
    exit /b 1
)

echo FFmpeg downloaded and copied to dist.
exit /b 0

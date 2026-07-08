@echo off
setlocal

echo Resetting FluxChat firewall rules...

netsh advfirewall firewall delete rule name="FluxChat LAN Discovery UDP 42731" >nul 2>nul
netsh advfirewall firewall delete rule name="FluxChat LAN Messages TCP 42732" >nul 2>nul
netsh advfirewall firewall delete rule name="FluxChat LAN Messages UDP 42732" >nul 2>nul

echo Adding FluxChat firewall rules...

netsh advfirewall firewall add rule name="FluxChat LAN Messages TCP 42732" ^
    dir=in action=allow protocol=TCP localport=42732 profile=any

netsh advfirewall firewall add rule name="FluxChat LAN Messages UDP 42732" ^
    dir=in action=allow protocol=UDP localport=42732 profile=any

echo.
echo Done. Restart FluxChat on both devices.

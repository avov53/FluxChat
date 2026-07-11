# FluxChat

FluxChat is a self-hosted desktop messenger for Windows with a small VPS relay server.

The project is built with .NET 9 and WPF. It is designed for private chats where users connect through their own relay instead of a public messaging platform.

## Features

- Windows desktop client with local chat history.
- User identity stored locally under `%AppData%\FluxChat`.
- VPS relay messaging over TCP.
- Contacts by User ID or `UserId@host:42800`.
- Profile sync with image and video avatars.
- Incoming call UI, ringing notifications, and experimental relay voice calls.
- Linux x64 relay server distribution with the `fluxus` admin tool.
- GitHub release auto-update for the Windows client.

## Client

Run from source:

```powershell
dotnet run --project .\FluxChat.Client\FluxChat.Client.csproj
```

Build a single-file Windows executable:

```powershell
.\dist.bat
```

The output is:

```text
dist\FluxChat.exe
dist\ffmpeg.exe
```

## Server

Build the Linux x64 relay distribution:

```powershell
.\dist-server-linux.bat
```

The output is:

```text
dist-server-linux\FluxChat.Server
dist-server-linux\fluxus
```

Copy those files to the VPS, run `fluxus` to create invites/manage users, and run `FluxChat.Server` on TCP port `42800`.

## Auto-Update

The Windows client checks the latest GitHub release on startup:

```text
https://github.com/avov53/FluxChat/releases/latest
```

For auto-update to work:

- Create release tags like `v1.0.1`, `v1.0.2`, etc.
- Attach the full Windows client package as a ZIP asset, for example `FluxChat v1.0.9.zip`. The ZIP should contain a top-level folder named `FluxChat v1.0.9` with `FluxChat.exe`, `ffmpeg.exe`, and any other runtime files.
- Also attach the Windows client executable as an asset named exactly `FluxChat.exe` for users updating from older FluxChat builds that only know how to replace the executable.
- Increase `Version`, `AssemblyVersion`, and `FileVersion` in `FluxChat.Client/FluxChat.Client.csproj` before building a release.

When a newer release is found, FluxChat prompts the user, downloads the ZIP package when available, closes the running app, copies all package files into the app folder, and starts the new version. If an older client updates through the `FluxChat.exe` asset first, the new client can then offer to install missing companion files from the ZIP package.

## Data

Client data is stored under:

```text
%AppData%\FluxChat
```

The local profile private key is protected with Windows DPAPI for the current Windows user.

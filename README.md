# Proton Handler

This is a protocol handler for apps running with Proton. Developed for apps like ModOrganizer and Unverum to enable the "1-click install" functionality from sites like NexusMods and GameBanana.

Turns out that getting WINE/Proton apps to play nice with custom protocol handlers is not very straightforward, and it's a feature I sorely missed after leaving Windows. I've found existing solutions but they never seem to work quite right, so I decided to make my own.

This should work for any proton instance, whether launched from Steam, Lutris, command line, or anywhere else.

# How does it work?

It searches for proton processes ("srt-bwrap") and then iterates through matches until it finds the one matching the name of the exe passed in (`args[0]`).

Once it identifies the right process it then uses `/proc/{processId}/cmdline` and `/proc/{processId}/environ` to extract the information it needs: Proton executable, prefix path, steam path, dotnet root, app exe path, and app args.

With that information it can build a complete proton command and append the custom URL (and any other args required by the app) and execute it, effectively passing the URL to the already-running app. That command will look something like this:

    DOTNET_ROOT=/home/terminus/Games/Skyrim/prefix/drive_c/Program\ Files/dotnet/ STEAM_COMPAT_CLIENT_INSTALL_PATH="/home/terminus/Games/Skyrim/MO2" STEAM_COMPAT_DATA_PATH="/home/terminus/Games/Skyrim/prefix-proton" "/home/terminus/.local/share/Steam/steamapps/common/Proton - Experimental/proton" run "/home/terminus/Games/Skyrim/MO2/ModOrganizer.exe" "-i Skyrim Special Edition" "nxm://skyrimspecialedition/mods/9999999/files/11111111?key=ThisIsAKeyValue&expires=1755657725&user_id=44444444"


# I have no idea what I'm doing

This is my first C# app. I wanted to learn the language by making something I'd actually use. Before you recoil in horror at my code, you have been warned.

# Build From Source

## Dependencies

- dotnet9
- CliWrap
- ini-parser-netstandard
- Microsoft.Extensions.Logging.Console

## Build

    dotnet build --configuration Release

I just symlinked the `proton-handler/bin/Release/net9.0/proton-handler` executable into `/usr/local/bin` for now. It works.


# Usage

In your browser when you open a link with an unrecognized protocol it should prompt you to "open this with...". *Do not select proton-handler*.

You need to create a handler script for your app first, then install it to `/usr/local/bin` and make it executable.

A basic handler looks like this:

```
#!/bin/bash

proton-handler ModOrganizer.exe $@
```

It just needs to call `proton-handler`, pass an exe to search for as the first parameter, and then pass the custom URL ("$@") after that.

Sometimes apps will require specific parameters to be passed, such as Unverum:

```
#!/bin/bash

proton-handler Unverum.exe -download $@
```

You can use the nxm and unverum handlers in the `dist/` folder as-is, or use them as a reference to create your own for other apps.

# Config

Config is stored in $HOME/.config/proton-handler/config.ini

It will store the last-used configuration for any app it successfully handles.

This config allows proton-handler to launch a previously-used app that isn't already running with the same settings (proton version, prefix, args, etc) when you click a handled link.

Sample entry:

```
[ModOrganizer.exe]
APP = /home/terminus/Games/Skyrim/MO2/ModOrganizer.exe
PROTON = /home/terminus/.local/share/Steam/steamapps/common/Proton - Experimental/proton
STEAM_COMPAT_CLIENT_INSTALL_PATH = /home/terminus/Games/Skyrim/MO2
STEAM_COMPAT_DATA_PATH = /home/terminus/Games/Skyrim/prefix-proton
DOTNET_ROOT = /home/terminus/Games/Skyrim/prefix/drive_c/Program\ Files/dotnet/
ARGS = -i Skyrim Special Edition
```

If I download a mod from the Nexus now and MO2 isn't already running, proton-handler will be able to launch it for me and then pass it the download link like normal.


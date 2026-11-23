# Shader Sentinel — for Oblivion Remastered

A tiny overlay that tells you when Oblivion Remastered is compiling shaders —
and when it's safe to play.

The game shows its shader-compilation progress bar exactly once, on first
launch. Recompiles after GPU driver updates or game patches run silently and
look like a freeze. Shader Sentinel watches from outside the game process (no
injection) and shows a game-styled corner overlay: **Compiling Shaders…** with
a live timer, then **Shaders Ready**. It starts with the game via a UE4SS Lua
script and exits by itself when its job is done.

Full user documentation: [ue4ss-mod/ShaderSentinel/README.md](ue4ss-mod/ShaderSentinel/README.md)

## Repository layout

```
ue4ss-mod/ShaderSentinel/   The mod as shipped (drop into ue4ss\Mods\)
  Scripts/main.lua          UE4SS launcher — starts the exe on game launch
  Source/ShaderSentinel.cs  The entire watcher/overlay program (one file)
  Source/build.cmd          Builds the exe with Windows' built-in C# compiler
  Source/icon.ico           Embedded icon
  README.md                 User-facing documentation
make-icon.ps1               Regenerates icon.ico (and a PNG preview)
screenshot-*.png            The overlay in action
nexus-description.txt       Nexus mod page description (BBCode)
```

## Building

No SDK required — the .NET Framework compiler ships with Windows:

```
cd ue4ss-mod\ShaderSentinel\Source
build.cmd
```

The exe lands one folder up, replacing the bundled one.

## How detection works

1. CPU usage of `OblivionRemastered-Win64-Shipping.exe` (compilation works the
   CPU; an idle menu doesn't)
2. Write activity in the GPU driver shader caches (`%LOCALAPPDATA%\NVIDIA\DXCache`,
   `%LOCALAPPDATA%\D3DSCache`, AMD/Intel equivalents)

Both present → "Compiling Shaders" with a timer. Writes quiet + CPU calm (or a
75-second quiet cap) → "Shaders Ready". No compile within ~20 seconds of
launch → "no compiling needed" notice. In every path the process then exits.

The overlay is a transparent, click-through, always-on-top window — the same
mechanism Steam notifications use. No injection, no hooks, no game-file
changes, no network, no files written, nothing resident between game sessions.

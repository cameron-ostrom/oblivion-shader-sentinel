# Shader Sentinel — for Oblivion Remastered

Tells you when the game is compiling shaders, and when it's safe to play.

Oblivion Remastered shows its shader-compilation progress bar exactly once — on
first launch. Every recompile after that (GPU driver update, game patch) runs
silently: the game sits at a screen that looks frozen, people alt-F4 it, and
anyone who starts playing too early gets heavy stutter. Shader Sentinel fills
that gap with a small overlay in the corner of the game window, styled to match
the game's own menus:

- **Compiling Shaders…** — with a live elapsed timer, while compilation runs
- **Shaders Ready — completed in X:XX** — when it finishes
- **Shaders Ready — no compiling needed** — about 20 seconds after launch, if
  there was nothing to compile (the usual case)

The notice fades out after a few seconds and the tool closes itself. It only
runs while the game does. There is no setup and nothing to remember.

## Requirements

- [UE4SS for Oblivion Remastered](https://www.nexusmods.com/oblivionremastered)
  — it starts Shader Sentinel when the game launches. Most script-mod setups
  already have it.
- Windows 10/11. No other runtimes needed (uses the .NET Framework built into
  Windows).

## Installation

**Vortex:** install like any other mod. The folder deploys to
`...\OblivionRemastered\Binaries\Win64\ue4ss\Mods\ShaderSentinel\`.

**Manual:** drop the `ShaderSentinel` folder into
`...\OblivionRemastered\Binaries\Win64\ue4ss\Mods\`.

**Uninstall:** remove or disable the mod. Nothing else is left behind — the
tool writes no files, no settings, and no registry entries.

### Without UE4SS

You can skip the UE4SS part and instead set Steam Launch Options for the game
to:

    "<full path to>\OblivionShaderSentinel.exe" %command%

The exe then launches the game itself and watches it the same way.

## What the exe actually does (read before trusting a random exe)

Shader Sentinel is a single small external program. It does **not** inject
into the game, hook anything, or touch game files. It detects compilation
from the outside, by watching two things:

1. CPU usage of the game process (compilation pins the CPU; menus don't)
2. Write activity in your GPU driver's shader cache folders
   (`%LOCALAPPDATA%\NVIDIA\DXCache`, `%LOCALAPPDATA%\D3DSCache`, and the
   AMD/Intel equivalents)

When both go quiet, compilation is over. The overlay is a plain transparent
always-on-top window — the same mechanism Steam and Discord notifications use.
The tool makes no network connections, writes nothing to disk, and exits by
itself when its job is done (or immediately when the game closes).

The complete source code — one C# file and one Lua file — ships in this
archive under `Source\`, and builds with the compiler already installed on
every Windows machine (see `Source\build.cmd`). If you'd rather run your own
build than mine: delete `OblivionShaderSentinel.exe`, run `build.cmd`, done.

## Known quirks and limits

- It reports **busy vs. done**, not a percentage — the driver cache doesn't
  expose how many shaders remain. "Done" is the part that matters.
- You may see a **split-second console flash** when the game launches; that's
  the launcher script starting the exe.
- The overlay requires the game in **borderless/windowed** mode (the default).
  In exclusive fullscreen, nothing can draw on top — the tool still runs, it
  just can't be seen.
- A compile must begin within ~20 seconds of launch to be caught. In practice
  compilation always starts as the engine boots, well inside that window.
- Tested on **NVIDIA**. The AMD and Intel cache paths are implemented but
  unverified — reports welcome.
- Homemade .NET executables occasionally trip antivirus false positives. If
  yours complains, the source is right there — build it yourself.

## Extras

- Run the exe with `--demo` to preview the overlay for ~20 seconds.
- While it's active, a tray icon offers "Preview overlay" and "Exit".

## Version history

- **1.0.0** — initial release.

# Historical chat rendering

Historical themes use `CoreGuiChatWindow`, a separate per-pixel-alpha Windows surface.
The launcher's modern panels and RichEdit history are not displayed in these themes.
`/theme default` returns to the original launcher controls and saved dimensions.

## Source snapshots

Roblox Core-Scripts is licensed under Apache 2.0. Geometry and styling are ported from:

- 2014: https://github.com/Roblox/Core-Scripts/blob/73c1fa83f4a33572afc10f28134accf4eb0a62b7/CoreScriptsRoot/CoreScripts/ChatScript.lua
- 2016 (January, pre-modular chat): https://github.com/Roblox/Core-Scripts/blob/c550e3475ee80be765d9b6284d18228b03a73cfc/CoreScriptsRoot/Modules/Chat.lua
- 2018: https://github.com/Roblox/Core-Scripts/tree/425d2d641bdc4b6c1104a9d5f6c53c9ea758c5cb/CoreScriptsRoot/Modules/Server/ClientChat

For 2018, the relevant files are ChatWindow.lua, ChatBar.lua,
MessageLogDisplay.lua, DefaultClientChatModules/ChatSettings.lua, and
DefaultClientChatModules/MessageCreatorModules/Util.lua.

## Layout

2014 has a 500 by 120 message frame at (0,5), a custom 40%-opaque black history background,
white Source Sans messages with colored player icons, and a separate 20-pixel
input spanning the bottom of the viewport. The archive selects a 280-pixel
history width when the viewport height is below 600.

The early-2016 theme uses Chat.lua's top-bar-enabled design: a (31,31,31)
background at 50% alpha, (209,216,221) inner input at 50% alpha, 32-pixel idle
bar and 40-pixel active bar. History is bottom aligned with 10-pixel margins.
The late-2016 modular chat was nearly identical to 2018 and is no longer the
snapshot used for `/theme 2016`.

2018 uses desktop geometry: width 30% of the viewport, height 25%
of the space below the 36-pixel top inset plus 24 pixels. The input frame is
42 pixels high (18 + 14 + 10), with a 7-pixel box inset and 5-pixel text inset.
The message frame starts 2 pixels below its parent and ends 2 pixels before
the input. The scroller has 3-pixel vertical margins, an 8-pixel message inset,
and a 4-pixel scrollbar. Frames have square corners and black backgrounds at
40% opacity. Messages use Source Sans Bold at 18 pixels and a dark text stroke.

The dimensions are recomputed from the Roblox client viewport when it resizes;
saved launcher sizes and offsets do not affect historical layouts.

## Intentional differences and rendering limits

Input layers now use archived alpha values, kept steady instead of fading.
History stays visible instead of applying the archived idle fade. Windows draws
the glyphs, so rasterization can differ from Roblox's engine. Source Sans Pro
fonts and the 2014 player icon are loaded from the installed Roblox client;
the bundled Source Sans 3 font is a fallback when the original font is unavailable.
No Roblox top bar is painted over the actual game's controls.

## Requested controls and compatibility options

- `/theme 2014` and `/theme 2014 classic` keep the old 20-pixel in-game input.
- `/theme 2014 docked` reserves a 32-pixel strip below the Roblox
  window. The game is fitted above it within the Windows work area. Its original
  placement is restored when switching away, hiding chat, or exiting.
- Every theme has the original round toggle button. A short click toggles once;
  holding it allows movement when `/drag on` is enabled. `/drag off` locks position.
- Historical themes have a lower-right history resize grip. Sizes and offsets
  are saved separately for each theme.
- `/text dark` and `/text light` set message text for game-background contrast.
- Typing `:sku` (or another alias fragment) opens emoji suggestions. Up/Down
  selects, Tab/Enter inserts, mouse click inserts, and Escape dismisses.
  Emoji aliases come from github/gemoji (license included in client/Emoji).
  Suggestions, typed emoji, and message history display bundled colour PNGs
  extracted from the installed Roblox TwemojiMozilla font's COLR/CPAL layers.
  There are 1,742 supported images. No Windows emoji font is used for these
  images; identifiers remain Unicode in the message protocol for compatibility.

The round button, resize controls, docked mode, emoji menu, and persistent history
are requested extensions rather than claims of pixel-identical historical UI.

## Automatic attachment

Opening the launcher registers a per-user Windows startup entry and protocol
handler. A single tray watcher attaches to a stable Roblox process, including
already-running players, and survives exits and replacement processes. A new
protocol invocation forwards its URI without killing the current overlay.
The tray menu can exit the watcher. Uninstall removes the startup entry.
Automatic upstream installer downloads are disabled in this custom build so
closing a game cannot replace these modifications with the official launcher.

## Verification

Run `RobloxChatLauncher --render-theme-previews <directory>` for offline layout,
input-opacity, transparent-viewport, and native layered-window checks. This
generates previews at 1280 by 720 and 1920 by 1080 without launching Roblox,
registering a protocol handler, or contacting the chat server.

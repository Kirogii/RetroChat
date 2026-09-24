# 2013 overlay

Enable with `/coregui 2013`; disable with `/coregui off`.
Restart the updated Node server as well as the launcher for roster broadcasts.
The list contains current chatroom connections, not all Roblox server players.
The normal list is rendered at 1.65 times the original 150px width. Tab toggles the
Lua-style half-screen centered list; its bottom extension tab minimizes/restores it.
Guests have no friend action until verified through the existing verification flow.
The blue speech-bubble badge identifies chatroom membership, not Roblox staff.

The Game Menu uses the supplied screenshot's 394x367 panel and button coordinates.
It is an external overlay, not an injected or complete CoreGui replacement.
Resume closes the overlay. Leave asks for confirmation and closes the Roblox window.
Reset/settings/report open native Roblox controls; recording uses F12 and screenshot
uses Print Screen. Their availability depends on the installed Roblox/Windows version.
The primary Game Menu and reset confirmation follow the supplied reference screenshots:
black controls, thin gray outlines, red default/white hover outlines, Arial, and subtly
rounded edges. The menu uses a live translucent dim layer and does not freeze or cache
the game image. Reset confirmation forwards Escape, R, Enter
to the Roblox window, matching the supplied App.py behavior. Settings and report still
hand off to Roblox's native controls. The list is adapted to chatroom users and includes
session and persisted total usage, so it intentionally differs from Roblox leaderstats.

`/channel list` opens the server's live channel directory and joins a selected room.

`/cookie VALUE` stores authentication under LocalAppData/RobloxChatLauncher/account.dpapi,
encrypted for the current Windows user. `/cookie clear` deletes it. Input is masked;
the command is intercepted locally before networking. Only a dedicated HTTPS client
for friends.roblox.com receives the cookie; redirects are disabled. No cookie is sent
to the chat server. Friend actions ask for confirmation; Roblox may reject expired
cookies or require challenges. Authenticated success has not been tested with a real account.

# Pandora Overlay

A tiny always-on-top overlay for The Isle: Evrima showing your dino's health, stamina, hunger, thirst, growth, and fracture status while playing on Isla Pandora.

It is **fully external**: the only thing it ever does is replay the same authenticated HTTPS request the islapandora.eu live-map page makes in your browser (`POST /api/map/mylocation`). It never reads game memory, never touches game files, and never interacts with the game process in any way.

<!-- After publishing to GitHub: replace OWNER below and uncomment the badge,
     and add an in-game screenshot as docs/screenshot.png:
[![Build](https://github.com/OWNER/PandoraOverlay/actions/workflows/release.yml/badge.svg)](https://github.com/OWNER/PandoraOverlay/actions)
![Overlay in-game](docs/screenshot.png)
-->

## Get it

Download the latest zip from the [Releases page](../../releases) and unzip it anywhere — it needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) installed. Or build from source as described below.

## Requirements

- Windows 10/11, x64
- .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`) — only needed when building from source
- An Isla Pandora account, logged in via Discord on islapandora.eu
- The Isle running in **borderless windowed** mode (overlays cannot draw over exclusive fullscreen)

## Build

```
cd PandoraOverlay
dotnet build -c Release
```

The exe lands in `bin\Release\net8.0-windows\PandoraOverlay.exe`.

## First-run setup

1. Run `PandoraOverlay.exe` — a setup window opens automatically.
2. Click **Open live map in browser** and log in with Discord.
3. On the live-map page press F12 → Network tab → type `mylocation` into the filter → click any row.
4. Under **Request Headers**, copy the whole value of `cookie` and paste it into the setup window. It validates as you type (it also cleans up stray quotes, a `cookie:` prefix, and line breaks automatically). Hit Save — the overlay connects immediately, no restart needed.

You can reopen the setup window any time: **Ctrl+F8** to enter edit mode, then click the ⚙ button.

The pasted cookie is encrypted with **Windows DPAPI** (scoped to your Windows user account) and stored in `config.json` as an opaque blob — it never sits readable on disk, and copying the file to another machine yields nothing usable. The session also rolls forward automatically: every response renews it, and the overlay re-encrypts and saves the refreshed value on exit, so this should be a one-time setup unless you log out or Cloudflare re-challenges the browser.

## Usage

- The overlay starts **locked**: click-through, no focus stealing, invisible to Alt-Tab.
- **Ctrl+F8** toggles edit mode — an orange border appears, you can drag the panel anywhere, open account settings with ⚙, and close it with ✕. Ctrl+F8 again locks it back. Position is remembered.
- States you'll see:
  - `Not in-game` — you're logged in but not spawned on the server (or the server is restarting).
  - `Disconnected · retrying` — network/auth problem; it keeps retrying every poll. If it never recovers, reopen ⚙ and paste a fresh cookie.
  - `Not set up yet` — no cookie stored; open the setup window (Ctrl+F8 → ⚙).

## Configuration (`config.json`)

| Field | Meaning |
|---|---|
| `Cookie` | Paste-here inbox only. Encrypted into `CookieProtected` and blanked on next launch. |
| `CookieProtected` | DPAPI-encrypted session cookie (base64). Managed by the app — don't edit, and it's useless off this machine/account. |
| `UserAgent` | Sent with every request; keep it matching your real browser. |
| `PollIntervalSeconds` | Default 3. Don't go below 2 — the site's own page polls at this pace and the API is rate-limited (300/window). |
| `WindowX` / `WindowY` | Saved panel position. |

## Troubleshooting

- **`Disconnected (HttpRequestException)` or 401/403 forever** → your session or `cf_clearance` expired. Redo the cookie copy from DevTools.
- **Overlay not visible over the game** → make sure the game is borderless windowed, not fullscreen.
- **Ctrl+F8 does nothing** → another app grabbed the hotkey; change `VK_F8` / `MOD_CONTROL` in `MainWindow.xaml.cs`.
- **Bars frozen** → check the timestamp in the status line; it updates on every successful poll.

## Fair-play notes

- The overlay only shows **your own** dino — the same data Isla Pandora already displays to you in a browser tab. It cannot see other players (except the site's own friends feature, not used yet).
- It polls at the same rate as the website itself and respects their rate limit.
- This consumes Isla Pandora's private, login-gated API. Be a good citizen: ask their admins whether they're okay with a personal overlay client, and stop using it if they say no.

## Roadmap (v1.1+)

- Minimap: map image + world→pixel transform + rotating player arrow (`x`, `y`, `yaw` are already in the payload).
- Friends markers from the `friends` endpoint *(needs a green light from the site dev first)*.
- Zone overlays (sanctuaries, patrol zones, migrations) *(same — ask first)*.

## License

MIT — see [LICENSE](LICENSE).

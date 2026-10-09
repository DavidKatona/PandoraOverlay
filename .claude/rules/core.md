---
paths:
  - "Core/**"
---

# Writing code in Core/

- `PandoraClient` and `OverlayConfig` have no WPF reference (nor has
  `Waypoints/WaypointLibrary`) — keep it that way. `PollService` (DispatcherTimer) and
  `HotkeySpec` (WPF key types) legitimately have one.
- **A new site call**: the request in a `PandoraClient.<Feature>.cs` part, ALL its gating
  (busy flags, guards, floors) in a `PollService.<Feature>.cs` part, the pure parsing in
  the feature's own folder. The request sends the Cookie header and the Referer of the site
  page it mirrors, calls `UpdateRollingCookie` straight after SendAsync (before any status
  check — a failing answer may still carry the renewal), reads the body whatever the
  status where the site does, turns the site's refusal into `SessionEndedException`, and
  uses `ConfigureAwait(false)` inside. A write method is documented "NEVER on a timer or
  without a click". Results carry a Problem that is an exception's type name or cleaned
  server words — never raw text; server strings are mapped, or go through
  `PatreonSkins.Clean`.
- Every request gate is a pure `internal static` rule tested in `PandoraOverlay.Tests/Core`
  (`NextInterval`, `FriendsDue`, `CalibrationDue`, `SignedOutAfter`, `HeatmapWantedFor`,
  `FriendsWantedFor`, `HeatmapFresh` …). New gating follows suit.
- A new feature adds no timer or request stream of its own: windows consume PollService's
  events, which it raises on the UI thread.
- New network calls fail soft and surface an exception's type name at most.

## The site's API (verified Sep–Oct 2026)

- `POST https://islapandora.eu/api/map/mylocation`, empty body (Content-Length: 0).
  Headers: `Cookie` (`connect.sid=...`, plus `cf_clearance=...` if one exists — the
  sign-in found none, Oct 2026), `User-Agent` (browser-like, from config),
  `Origin: https://islapandora.eu`, `Referer: https://islapandora.eu/live-map`,
  `Accept: */*`.
- Answers: `{"inGame":false}`, or in game:
  `{"inGame":true,"player":{"steamId":"7656...","name":"Dave94Punk","dino":"Deinosuchus","gender":"Male","growth":0.416,"health":0.995,"stamina":1,"hunger":0.322,"thirst":0.77,"yaw":-140.394,"headFractured":false,"bodyFractured":false,"legsFractured":false,"x":6167.09,"y":-316782.07,"z":20754.4}}`
  Stats are 0–1 floats; x/y/z are Unreal world coords (cm); yaw is facing (deg).
- Every answer carries `Set-Cookie` renewing `connect.sid` (a rolling ~1-month
  session): `PandoraClient.UpdateRollingCookie` splices it in; persisted on exit.
- No session: a dead or missing cookie gets `403 {"error":"Forbidden"}` (a 403 with a
  JSON body); a Cloudflare block is a 403 with HTML and stays an ordinary failure.
- Behind Cloudflare, but a plain HttpClient passes — no TLS impersonation needed.
  The backend is Express; auth is the session cookie only.
- `POST /api/map/calibration`: empty body, same headers; POST-only (GET 404s); works
  even unauthenticated. Verified answer (Sep 2026):
  `{"success":true,"scaleX":0.002001...,"scaleY":-0.002000...,"offsetX":1160.92...,"offsetY":1223.28...,"mapSize":2500,"pinOffset":{"x":-15,"y":25}}`
  The transform (note the negative scaleY and the flip; pinOffset applies to EVERY
  marker, the player's arrow included) is `MapCalibration.ToFraction` in
  `PandoraClient.cs`, with its comment.
- The site's map picture is `/assets/map-<hash>.png` (1000×1000); the hash changes per
  deploy, so a copy is bundled as `Assets/map.png`.

# Truecolor Detection & Graceful Fallback — Research Findings (Vixel issue #2)

**Status:** research complete — recommended strategy below is directly implementable by the canvas renderer.
**Scope:** Vixel = C#/.NET cross-platform terminal pixel editor on Terminal.Gui v2. Canvas = grid of 24-bit RGB pixels rendered as half-block cells (top pixel = cell foreground, bottom pixel = cell background, glyph U+2580 ▀). Renderer must degrade truecolor -> xterm-256 -> ANSI-16.

---

## TL;DR — recommended strategy

1. **Detection is owned by Terminal.Gui for environment heuristics, by Vixel for the 256-color tier.** Terminal.Gui v2 detects capability from environment variables (`TerminalEnvironmentDetector`) and exposes it as `IDriver.ColorCapabilities` (`TerminalColorCapabilities.Capability` ∈ `NoColor | Colors16 | Colors256 | TrueColor`). It also knows whether the console is a legacy Windows conhost (`IDriver.IsLegacyConsole`, and `SupportsTrueColor => !IsLegacyConsole`).
2. **Terminal.Gui has NO built-in truecolor -> xterm-256 degradation.** Its output layer writes either 24-bit SGR (`38;2;r;g;b` / `48;2;r;g;b`) or, when `Force16Colors` is set, ANSI-16 via a nearest-named-color Euclidean match. There is no `38;5;n` / `48;5;n` (256-indexed) output mode. **Vixel must quantize 24-bit -> 256 itself** (map each pixel to the nearest xterm-256 palette entry and put that palette RGB in the `Attribute`); Terminal.Gui will then pass it through as truecolor SGR, which 256-color terminals render from their palette.
3. **24-bit -> ANSI-16 can be delegated to Terminal.Gui** by setting `Driver.Force16Colors = true` (app-level toggle); quantization then happens inside Terminal.Gui (`Color.GetAnsiColorCode()`). Alternatively Vixel quantizes to 16 itself if it wants a better distance metric.
4. **The safest capability decision tree** (details + sources below): `NO_COLOR`/`TERM=dumb` -> no color; Windows legacy conhost (no VT) -> 16; `COLORTERM=truecolor|24bit` or `WT_SESSION` -> truecolor; `TERM` ends in `-256color` or contains `256` -> 256; else default to 256 (not truecolor — see failure modes: SSH, tmux/screen, Linux console, Terminal.app < macOS 26).
5. **Quantization algorithms:** 24-bit -> 256 via the xterm 6×6×6 cube (steps 0/95/135/175/215/255 at indices 16–231) + 24-step grayscale ramp (indices 232–255, `8+10n`), using a perceptually-weighted distance (redmean) — this is Rich's exact approach. 24-bit -> 16 via nearest palette match (Rich uses redmean; Terminal.Gui uses plain Euclidean; both are fine, redmean is better for skin tones).

---

## 1. What Terminal.Gui v2 owns vs what Vixel must own

### Per-cell drawing API (the canvas contract)

A custom canvas `View` draws with the deferred `Move()` / `SetAttribute()` / `AddRune()` model; `Attribute` is a record struct of `Color Foreground` / `Color Background` / `TextStyle`. `Color` holds 24-bit RGB (`new Color(byte r, byte g, byte b)`, `Color.FromRgb`). The drawing doc's custom-view example explicitly checks `App?.Driver?.SupportsTrueColor` and then `SetAttribute(new Attribute(Color.FromRgb(...), Color.FromRgb(...)))`. Half-block rendering is simply `AddRune("▀")` (U+2580) after `SetAttribute(new Attribute(topPixel, bottomPixel))`.
- Sources:
  - https://github.com/gui-cs/Terminal.Gui/blob/develop/docfx/docs/drawing.md ("Terminal.Gui apps draw using the Move() and AddRune() APIs… SetAttribute()", custom view example at the bottom)
  - https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drawing/Attribute.cs (Attribute record struct: Foreground/Background/Style)
  - https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drawing/Cell.cs (Cell = Attribute + Grapheme, one grapheme cluster per cell)

### Capability detection (Terminal.Gui owns env-var heuristics)

`TerminalEnvironmentDetector.DetectColorCapabilities()` reads `TERM`, `COLORTERM`, `TERM_PROGRAM`, `WT_SESSION`, `NO_COLOR` and returns a `TerminalColorCapabilities` record with a `ColorCapabilityLevel Capability` (`NoColor`, `Colors16`, `Colors256`, `TrueColor`). Decision order in `DetermineCapability`:
1. `NO_COLOR` set (any value) -> `NoColor`
2. `TERM=dumb` -> `NoColor`
3. `COLORTERM` = `truecolor` or `24bit` (case-insensitive) -> `TrueColor`
4. `WT_SESSION` non-empty -> `TrueColor` ("Windows Terminal always supports TrueColor")
5. `TERM=linux` -> `Colors16`
6. `TERM` ends with `-256color` -> `Colors256`
7. default -> `TrueColor` ("assume TrueColor for modern terminals")

This is exposed on `IDriver.ColorCapabilities` (null until populated via internal `SetColorCapabilities`).
- Sources:
  - https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drivers/TerminalEnvironment/TerminalEnvironmentDetector.cs
  - https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drivers/TerminalEnvironment/TerminalColorCapabilities.cs
  - https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drivers/TerminalEnvironment/ColorCapabilityLevel.cs
  - https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drivers/IDriver.cs ("ColorCapabilities — The terminal's color capabilities as detected from environment variables")
  - https://github.com/gui-cs/Terminal.Gui/blob/develop/docfx/docs/drivers.md (Color Support section listing `ColorCapabilities`, `SupportsTrueColor`, `Force16Colors`)

**Caveat:** in the current `develop` source, `SetColorCapabilities` is `internal` and I could not locate a production caller via the web UI (GitHub code search requires auth); `ApplicationImpl.CreateDriver` sets `Driver.Force16Colors = Drivers.Driver.Force16Colors` but I did not find where `ColorCapabilities` is populated at startup. **Action for Vixel: call `TerminalEnvironmentDetector.DetectColorCapabilities()` directly at startup (it is `public static`) rather than relying on `IDriver.ColorCapabilities` being non-null.** Verify against the pinned Terminal.Gui package version — the API surface here is `develop`-branch and the Vixel prototype (issue #7) should confirm the exact members on the referenced NuGet version.
- Source: https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/App/ApplicationImpl.Driver.cs

### Output behavior (quantize-or-passthrough)

`OutputBase.AppendOrWriteAttribute` writes, per attribute change:
- default: truecolor SGR — `ESC[38;2;R;G;Bm` / `ESC[48;2;R;G;Bm` (24-bit passthrough, no quantization);
- if `Force16Colors`: 16-color SGR via `attr.Foreground.GetAnsiColorCode()`;
- `Color.None` -> `39m`/`49m` (terminal default fg/bg).

`IDriver.SupportsTrueColor => !IsLegacyConsole`; `Driver.Force16Colors` is a static config mirrored to each driver instance (`IDriver.Force16Colors`), and is forced true when `SupportsTrueColor` is false.
- Sources:
  - https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drivers/Output/OutputBase.cs (AppendOrWriteAttribute emits `38;2;`/`48;2;` sequences; Force16Colors branch)
  - https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drivers/DriverImpl.cs (`SupportsTrueColor => !IsLegacyConsole`; `Force16Colors` passthrough; `SetColorCapabilities` internal setter)
  - https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drivers/Driver.cs (static `Force16Colors` config + `Force16ColorsChanged`)

### 24-bit -> 16 quantization inside Terminal.Gui

`Color.GetAnsiColorCode()` = `ColorName16ToAnsiColorMap[GetClosestNamedColor16()]`; `GetClosestNamedColor16` picks the minimum **Euclidean distance** (`Vector3`-style) against a hard-coded 16-color RGB table (W3C-ish values: e.g. BrightRed = (231,72,86), BrightBlue = (59,120,255), DarkGray = (118,118,118) — the Windows 10 console palette). On legacy Windows conhost, `WindowsOutput` bypasses ANSI entirely: `SetConsoleTextAttribute(_screenBuffer, fg16 | bg16<<4)`.
- Sources:
  - https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drawing/Color/Color.cs (`GetAnsiColorCode`, `GetClosestNamedColor16`, Euclidean `CalculateColorDistance`)
  - https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drawing/Color/Color.ColorExtensions.cs (the 16-color RGB table)
  - https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drivers/WindowsDriver/WindowsOutput.cs (`IsLegacyConsole` via `ENABLE_VIRTUAL_TERMINAL_PROCESSING` console-mode check; legacy path uses `SetConsoleTextAttribute`)

### The 256-color gap (Vixel owns this tier)

There is **no** `38;5;n`/`48;5;n` emission anywhere in `OutputBase`/`AnsiOutput`; grep of the output pipeline shows only the truecolor and Force16Colors branches. Terminal.Gui's `ColorQuantizer` class (`Drawing/Color/ColorQuantizer.cs`) is for **sixel image palettes** (popularity palette + nearest match, `MaxColors=256`), not for terminal color fallback. **Consequence: when the detected level is `Colors256`, Vixel must map each 24-bit pixel to the nearest xterm-256 palette entry and put that entry's RGB into the `Attribute`. Terminal.Gui will emit it as a 24-bit SGR; 256-color terminals (xterm without directColor, Windows console in VT mode, etc.) then snap it to their palette internally.** This is exactly what xterm documents: "If xterm is not compiled with direct-color support, it uses the closest match in its palette for the given RGB Pr/Pg/Pb", and what Microsoft documents for conhost VT: "the Windows Console will choose the nearest appropriate color from the existing 16 color table" — but note Microsoft's snap is **to the 16-color table** for `38;2` (see §2 Windows), which is why emitting the *pre-quantized 256 palette RGB* vs the raw 24-bit value matters on some hosts.
- Sources:
  - https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drivers/Output/OutputBase.cs
  - https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drawing/Color/ColorQuantizer.cs (sixel-oriented quantizer)
  - https://invisible-island.net/xterm/ctlseqs/ctlseqs.html (SGR 38:2/38:5 semantics; closest-match fallback when direct-color unsupported)
  - https://learn.microsoft.com/en-us/windows/console/console-virtual-terminal-sequences (Extended Colors: 38;2 / 48;2 / 38;5 / 48;5; nearest-color note)

---

## 2. Detection conventions per OS (what .NET sees)

### Cross-cutting conventions

- **`COLORTERM`** = de-facto truecolor advertisement: values `truecolor` or `24bit` (the termstandard/colors doc notes S-Lang treats them case-sensitively; Terminal.Gui/Rich/tmux compare case-insensitively). Set by VTE-based terminals, Konsole, iTerm2, kitty, etc. Weakness: **not forwarded by default over ssh/sudo** — sshd's `AcceptEnv` defaults to none; the fix is `SendEnv COLORTERM` client-side + `AcceptEnv COLORTERM` server-side (and `env_keep` in sudoers). Because it errs toward under-advertising, it is still the recommended first check.
  - Sources: https://github.com/termstandard/colors ("Truecolor Detection"); https://man7.org/linux/man-pages/man5/sshd_config.5.html (`AcceptEnv` "specifies what environment variables sent by the client will be copied into the session's environ(7)")
- **`TERM`** drives terminfo lookup on Unix; `-256color` suffix => 256. ncurses terminfo has supported a **user-defined boolean `RGB` capability** (and the tmux `Tc` convention) for 24-bit since ncurses-6.0-20180121; `tigetflag("RGB")`/`tigetflag("Tc")` is the terminfo-native truecolor check (notcurses uses exactly this, falling back to COLORTERM). .NET has no managed terminfo reader; on Unix the practical equivalent is matching `TERM` strings, or trusting COLORTERM.
  - Sources: https://invisible-island.net/ncurses/man/terminfo.5.html; https://github.com/dankamongmen/notcurses/blob/master/src/lib/termdesc.c (`query_rgb`: `tigetflag("RGB") > 0 || tigetflag("Tc") > 0`, else COLORTERM)
- **`NO_COLOR`**: "when present and not an empty string (regardless of its value), prevents the addition of ANSI color". (Terminal.Gui's detector treats mere presence as NoColor — technically looser than the spec; Vixel should follow the spec: non-empty.) Note NO_COLOR is about *app output preference*, not terminal capability — it should not change detection, just suppress color at the app layer.
  - Source: https://no-color.org/
- **`TERM=dumb`** => no color. (Terminal.Gui, Rich both honor this.)

### macOS / Linux

- Modern terminals (kitty, alacritty, ghostty, iTerm2, wezterm, foot, VTE-based) set `COLORTERM=truecolor`. Ghostty additionally ships `TERM=xterm-ghostty` with its own terminfo entry (ncurses 6.5-20241228+); over SSH to a host missing that entry you get "missing or unsuitable terminal" errors, and the documented workarounds are `infocmp -x ... | ssh ... tic -x -` or `SetEnv TERM=xterm-256color` — i.e. **after the fallback, truecolor still works but TERM alone will only claim 256**, which is another reason COLORTERM must be checked first.
  - Source: https://ghostty.org/docs/help/terminfo
- **macOS Terminal.app**: no truecolor until macOS 26 (per termstandard/colors; it also historically does not set COLORTERM — `TERM=xterm-256color`). Vixel on Terminal.app should resolve to 256.
- **Linux console (fbcon, `TERM=linux`)**: parses 24-bit sequences since kernel 3.16 but **downgrades to 16 foregrounds and 8 backgrounds**; Terminal.Gui's detector correctly maps `TERM=linux` -> `Colors16`. Treat as 16.
  - Source: https://github.com/termstandard/colors (Partial Support: Linux console)

### Windows

- **What .NET sees:** env vars (`TERM`/`COLORTERM`) are usually **absent** on Windows; detection is via the console host: `GetConsoleMode` on the output handle lacking `ENABLE_VIRTUAL_TERMINAL_PROCESSING` => legacy conhost (Terminal.Gui `IsLegacyConsole` does exactly this). VT processing is available from Windows 10 TH2 (1511, build 10586); **24-bit color in conhost requires Windows 10 build 14931+** (Spectre.Console uses `major == 10 && build >= 15063` as its truecolor gate). With VT on, conhost accepts `38;2;r;g;b` but historically snaps to the 16-color table; Windows Terminal renders full 24-bit.
  - Sources: https://learn.microsoft.com/en-us/windows/console/console-virtual-terminal-sequences; https://learn.microsoft.com/en-us/windows/console/classic-vs-vt ("Configuration of the console modes on a handle to opt in to Virtual Terminal Sequence support"); https://github.com/spectreconsole/spectre.console/blob/main/src/Spectre.Console.Ansi/ColorSystemDetector.cs (build >= 15063 -> TrueColor)
- **Windows Terminal**: sets `WT_SESSION` (Terminal.Gui keys on it) and supports truecolor. -> TrueColor.
- **ConEmu**: supports xterm-256 and 24-bit ("TrueMod") when enabled; detection signal is the **`ConEmuANSI` environment variable** (`ON`/`OFF`) — worth an explicit check in Vixel's Windows branch since WT_SESSION/TERM/COLORTERM may all be absent. Outside its "working area" ConEmu approximates to 16 colors.
  - Source: https://conemu.github.io/en/AnsiEscapeCodes.html ("Environment variable … check ConEmuANSI"; 38;2/48;2 supported SGR; TrueMod requirement)
- **Legacy conhost** (no VT): Terminal.Gui's `WindowsOutput` legacy path handles it with Win32 `SetConsoleTextAttribute` = 16 colors only. -> 16, nothing Vixel can do beyond that.
  - Source: https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drivers/WindowsDriver/WindowsOutput.cs

### .NET-side summary table

| Host | Signals visible to .NET | Level |
|---|---|---|
| kitty/alacritty/ghostty/iTerm2/wezterm/VTE/Konsole | `COLORTERM=truecolor` (usually) | TrueColor |
| Windows Terminal | `WT_SESSION` set | TrueColor |
| ConEmu | `ConEmuANSI=ON` (+TrueMod) | 256 / TrueColor when TrueMod on |
| conhost Win10 ≥ 15063 (VT on) | no env; VT mode on console handle; OS build | TrueColor (Spectre rule) / snap-to-16 historically |
| conhost legacy (VT off) | `ENABLE_VIRTUAL_TERMINAL_PROCESSING` absent | 16 (Win32 API) |
| Terminal.app (< macOS 26) | `TERM=xterm-256color`, no COLORTERM | 256 |
| Linux console | `TERM=linux` | 16 |
| inside tmux/screen (default) | `TERM=screen|tmux[-256color]`; COLORTERM often stripped or set from tmux's client env | usually 256; truecolor only if tmux client advertised it |
| SSH without forwarding | COLORTERM/TERM_PROGRAM missing | under-detects: fall back via TERM |
| CI/redirected | `TERM=dumb` or unset, not a tty | NoColor |

---

## 3. Prior art for quantization

### 24-bit -> xterm-256

The xterm-256 layout (documented values): indices 0–15 system colors; **16–231 = 6×6×6 cube** with channel levels `{0,95,135,175,215,255}`, index = `16 + 36r' + 6g' + b'`; **232–255 = 24-step grayscale** `v = 8 + 10n` (n = 0..23).
- Source: palette values per https://www.ditig.com/256-colors-cheat-sheet (full RGB table; consistent with Rich's `EIGHT_BIT_PALETTE` literal in https://github.com/Textualize/rich/blob/master/rich/_palettes.py); cube/gray structure also described at https://github.com/termstandard/colors ("256-color palette: 16 colors, plus a 6×6×6 color-cube and a 24-level gray scale").

Established algorithms:

1. **Rich (Python)** — `Color.downgrade()`:
   - If HLS saturation < 0.15, treat as grayscale: `gray = round(l * 25)`; picks from the grayscale ramp (with special-casing around the ramp ends to use cube black/white).
   - Else map each channel to a 0–5 cube coordinate with the piecewise `v < 95 ? v/95*? : 1 + (v-95)/40`-style scaling, then combine into the cube index (plus comparing against grayscale when it's a near-tie).
   - Source: https://github.com/Textualize/rich/blob/master/rich/color.py (`downgrade`, lines around 506–530)
2. **Nearest-palette with weighted distance (Rich palettes, and chafa/others)** — precompute the 256 RGB triples, pick argmin of the **"redmean"** weighted Euclidean distance:
   `d = sqrt( ((512+r̄)·Δr² >> 8) + 4Δg² + ((767-r̄)·Δb² >> 8) )`, `r̄ = (r1+r2)/2`.
   - Source: https://github.com/Textualize/rich/blob/master/rich/palette.py (`Palette.match`)
3. **Terminal.Gui internal quantizer** — popularity palette + plain Euclidean nearest match, `MaxColors=256` — built for sixel image export, not terminal SGR fallback; usable as reference but not on the text output path.
   - Source: https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drawing/Color/ColorQuantizer.cs (+ `Drawing/Quant/EuclideanColorDistance.cs`)

**Recommendation for Vixel:** precompute the static 256-entry xterm palette once; per pixel, nearest-match with redmean distance (with a small memo cache keyed by 24-bit RGB — pixel art has few distinct colors, and Terminal.Gui's own quantizer uses the same `Dictionary<Color,int>` cache trick). Optionally adopt Rich's low-saturation->grayscale-ramp shortcut for better grays. Cache + 256-entry scan is trivially fast enough for interactive redraw.

### 24-bit -> ANSI-16

- **Terminal.Gui:** plain Euclidean nearest against its hard-coded 16-color table (Windows-10-palette values). This is what `Force16Colors` gets you for free.
- **Rich:** redmean nearest against `STANDARD_PALETTE` (or `WINDOWS_PALETTE` on legacy Windows).
  - Sources: https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drawing/Color/Color.cs; https://github.com/Textualize/rich/blob/master/rich/color.py (`downgrade` -> `STANDARD_PALETTE.match`)
- **Note on perceptual quality:** plain √(ΔR²+ΔG²+ΔB²) "gives poor results when trying to find the 'nearest' available color as perceived by most humans"; CIEDE2000 is the gold standard but "considerably more complex". Redmean is the accepted cheap middle ground (used by Rich, compuphase-derived).
  - Source: https://github.com/termstandard/colors ("Note about color differences")

**Recommendation for Vixel:** for v1, delegate 16-color degradation to `Driver.Force16Colors = true` (one line, maintained upstream, handles the legacy-conhost Win32 path automatically). Only implement a custom 16-color quantizer if the Euclidean matches visibly hurt (gradients/palette swatches).

---

## 4. Known failure modes

1. **SSH drops COLORTERM/TERM_PROGRAM by default** (`AcceptEnv` unset on sshd; also `sudo` env scrubbing). Truecolor hosts then under-report; mitigations: `SendEnv`/`AcceptEnv`/`env_keep`. Design consequence: **never treat missing COLORTERM as proof of no-truecolor; keep a user override (`--colors=truecolor|256|16` or config) to force the level.**
   - Sources: https://man7.org/linux/man-pages/man5/sshd_config.5.html (AcceptEnv); https://github.com/termstandard/colors (COLORTERM forwarding section)
2. **tmux**: truecolor inside tmux requires the *client* to advertise it — tmux's `tty-term.c` checks the client env's COLORTERM (`truecolor`/`24bit` => grants RGB feature) or the terminfo `RGB`/`Tc` capability; otherwise users need `terminal-features`/`terminal-overrides` (e.g. `set -as terminal-features ",xterm-256color:RGB"`). tmux 2.2+ supports truecolor. Inside a default tmux, `TERM=screen`/`tmux`/`tmux-256color` and COLORTERM is often absent or stale from session creation (`update-environment` only refreshes on attach). Expect 256 inside tmux unless configured.
   - Sources: https://man7.org/linux/man-pages/man1/tmux.1.html (`terminal-features` `RGB` — "Supports RGB colour with the SGR escape sequences"; `Tc` terminfo extension "equivalent to the RGB terminfo(5) capability"; `update-environment`); https://github.com/tmux/tmux/blob/master/tty-term.c (COLORTERM check granting RGB); https://github.com/termstandard/colors (tmux 2.2+)
3. **GNU screen**: truecolor only in the 5.x/master branch behind a `truecolor on` config; released 4.x approximates to 256. Expect 256 (or 16) inside screen.
   - Source: https://github.com/termstandard/colors (Multiplexers: screen "has support in 'master' branch, need to be enabled")
4. **Terminals that accept 24-bit SGR but silently approximate** (advertise-vs-render mismatch):
   - Linux console (fbcon): parses since 3.16, downgrades to 16 fg / 8 bg.
   - mlterm: approximates via a 512-color embedded palette.
   - urxvt: caps the number of simultaneously-used colors.
   - conhost with VT on: snaps extended colors to the 16-color table (per Microsoft docs), so `COLORTERM`-less Windows hosts may mangle truecolor even when sequences are accepted.
   - Sources: https://github.com/termstandard/colors (Partial Support); https://learn.microsoft.com/en-us/windows/console/console-virtual-terminal-sequences (Extended Colors note)
5. **Colon vs semicolon subparameters**: ISO-8613-6 direct color officially uses colons (`38:2:r:g:b`); xterm accepts both, Konsole originated the semicolon form, and most terminals (incl. Windows console) only document/accept semicolons. **Terminal.Gui emits semicolons** (`38;2;…`) — the maximally compatible choice; Vixel should not bypass it with hand-rolled colon sequences.
   - Sources: https://invisible-island.net/xterm/ctlseqs/ctlseqs.html; https://github.com/termstandard/colors (delimiter column)
6. **Runtime-verified alternative** (if env heuristics ever prove insufficient): set an unlikely truecolor (`48:2:1:2:3m`) then DECRQSS-query current SGR (`ESC P $q m ESC \`); a reply echoing `48:2:1:2:3m` proves truecolor; `40m`/no-reply proves otherwise. Transparent to ssh/sudo but requires an interactive request/response round-trip — Terminal.Gui already has the `AnsiRequestScheduler`/OSC-10/11 machinery for this class of query (used for `DefaultAttribute`), so it's feasible later but not needed for v1.
   - Sources: https://github.com/termstandard/colors ("Querying The Terminal"); https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drivers/DriverImpl.cs (DefaultAttribute via OSC 10/11)
7. **`NO_COLOR` empty-string nuance**: spec says "present **and not an empty string**"; Terminal.Gui's detector triggers on mere presence. Vixel should implement its own check per spec (or accept the framework's stricter stance knowingly).
   - Sources: https://no-color.org/; https://github.com/gui-cs/Terminal.Gui/blob/develop/Terminal.Gui/Drivers/TerminalEnvironment/TerminalEnvironmentDetector.cs
8. **256-palette indices 0–15 are reprogrammable/user-themed** (and ConEmu only supports 256 in its "working area"); don't rely on indices 0–15 matching the textbook RGB — for the cube/grayscale tiers (16–255) values are stable; for 16-color fallback Terminal.Gui's themed table is the right target anyway.
   - Sources: https://github.com/termstandard/colors ("all entries are separately reprogrammable"); https://conemu.github.io/en/AnsiEscapeCodes.html

---

## 5. Decision tree Vixel should implement

```
if NO_COLOR non-empty or TERM == "dumb" or output redirected/not a tty:
    level = NoColor                      (canvas disabled or mono)
elif Windows:
    if conhost without ENABLE_VIRTUAL_TERMINAL_PROCESSING:  level = 16   (Terminal.Gui IsLegacyConsole covers this)
    elif WT_SESSION set:                                     level = TrueColor
    elif ConEmuANSI == "ON":                                level = 256 (TrueColor if ConEmu TrueMod enabled — can't detect; prefer 256)
    elif OS build >= 15063:                                  level = TrueColor   (Spectre rule; VT-era conhost)
    else:                                                    level = 16
else:  # macOS/Linux/BSD
    if COLORTERM in {truecolor, 24bit}:                      level = TrueColor
    elif TERM ends with -256color or contains "256":          level = 256
    elif TERM == "linux":                                    level = 16
    elif TERM_PROGRAM == "Apple_Terminal" and macOS < 26:    level = 256
    else:                                                    level = 256   (NOT truecolor — SSH/multiplexer-safe default)
level = min(level, user_override_if_any)   # --colors / config beats detection, per NO_COLOR philosophy
```

Then per render: TrueColor -> pass `Attribute(pixelTop, pixelBottom)` straight through; 256 -> replace each pixel with `Xterm256Palette[Nearest(pixel)]` RGB first; 16 -> set `Driver.Force16Colors = true` once and render unchanged; NoColor -> refuse/disable canvas.

## 6. Open verifications for the prototype (issue #7)

- Confirm the exact Terminal.Gui NuGet version's public surface: `TerminalEnvironmentDetector` visibility, whether `IDriver.ColorCapabilities` is populated at startup on that version, and `Driver.Force16Colors` static setter (all verified here against `develop` branch source, which may drift from the pinned package).
- Confirm `SetAttribute`+`AddRune("▀")` per-cell truecolor throughput is acceptable (that's issue #7's core question).
- Confirm Ghostty/iTerm2 COLORTERM values empirically on the dev machine (ghostty sets COLORTERM=truecolor per its terminfo; verify in a live session).

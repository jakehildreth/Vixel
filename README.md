# Vixel

Draw pixel art in your terminal. Yes, really.

Vixel is a cross-platform terminal app for drawing pixel art... in the terminal. C# / .NET 10, vim-style modal controls, and a rendering trick so nice I did it twice: every terminal cell shows TWO pixels (top half in the foreground color, bottom half in the background... `▀`, the hardest-working glyph in Unicode).

Truecolor where your terminal supports it, graceful degradation to 256 or 16 colors where it doesn't. It works over SSH, in tmux, and on Windows, macOS, and Linux.

## Installation

.NET global tool (if you have the SDK):

```bash
dotnet tool install -g Vixel
```

Or grab a self-contained binary (no .NET required) from the [releases page](https://github.com/jakehildreth/Vixel/releases).

## Quick start

```bash
vixel                  # new 64x32 canvas
vixel art.vixel        # open an existing file
vixel --colors 256     # force a color tier (truecolor | 256 | 16)
```

Now draw something:

```
i            " enter PAINT mode... movement draws
hjkl/arrows  " move around
Esc          " back to NORMAL
:w art.vixel " save
:q           " quit
```

If you know vim, you already know Vixel. If you don't... there's a status line. You'll be fine.

## Controls

The mode is always on the status line at the bottom. NORMAL is where you live.

| Key | What it does |
|---|---|
| `hjkl` / arrows | move the brush |
| `Space` / `r` | stamp a pixel (the brush doesn't move... stamping is not walking) |
| `x` | erase the pixel(s) under the brush |
| `i` | PAINT mode: movement draws until you hit `Esc` |
| `f` | flood fill |
| `L` / `R` | line / rectangle: move to the second point, `Enter` commits, `Esc` cancels. `Shift+Enter` fills the rect |
| `v` | select a region, move, `y` yanks it, `d` erases it |
| `p` | paste: the copy floats at the brush, `Enter` stamps it |
| `P` | eyedropper: pick up the color under the brush |
| `e` | eraser toggle |
| `-`/`_` `=`/`+` | brush size down / up |
| `o` | brush shape, square ↔ circle |
| `1`–`9`, `0` | palette slots, `Tab` / `Shift+Tab` to flip pages |
| `u` / `Ctrl+R` | undo / redo |
| `:` | command line |

The command line speaks vim: `:w [file]` `:q` `:q!` `:wq` `:e <file>`, plus `:color #rrggbb` for custom colors, `:export art.png 4` for a 4x nearest-neighbor PNG, and `:new 128x64` / `:resize 128x64` for canvas size.

Coming from vim? [docs/vi-keybindings.md](docs/vi-keybindings.md) translates the common vi bindings into their Vixel equivalents.

## Files

- **`.vixel`** is the native format: palette-indexed JSON. Human-readable, git-diffable, hand-editable if you're feeling brave.
- **PNG export** is built in (`:export`). 1:1 by default; pass a scale for crispy big pixels.
- **Interop**: imports and exports `.piskel`, `.px` (Pixquare), and `.ase`/`.aseprite` files. Imports flatten layers and frames; exports are single-layer minimal. If you want your pixels rendered as console scripts, that's what [px2go](https://github.com/jakehildreth/px2go) and [px2ps](https://github.com/jakehildreth/Px2PS) are for... they play nicely together.

## License

MIT License w/Commons Clause - see [LICENSE](LICENSE) file for details.

---

Made with 💜 by [Jake Hildreth](https://jakehildreth.com)

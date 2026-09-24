# Vi Keybindings, Translated to Vixel

Vixel is vim-modal, but the canvas is not a text buffer. Some vi keys map directly. Some vi concepts map to a different key because the target is pixels, not lines. Some vi keys have no meaning in Vixel.

This document covers the common vi bindings and gives the Vixel equivalent for each.

## Modes

| Vi | Vixel | Notes |
|---|---|---|
| Normal mode | NORMAL mode | Default state. Movement does not draw. |
| Insert mode (`i`, `a`, `o`, ...) | `i` enters PAINT mode | Vixel has one insert mode. In PAINT, movement draws. `Esc` returns to NORMAL. |
| Visual mode (`v`, `V`, `Ctrl+V`) | `v` starts a region select | One selection type only. Move to the second corner. `y` yanks, `d` erases, `Esc` cancels. |
| Command mode (`:`) | `:` opens the command line | Same idea. See "Ex commands" below. |

## Movement

| Vi | Vixel | Notes |
|---|---|---|
| `h` `j` `k` `l` | `h` `j` `k` `l` / arrow keys | Direct map. Moves the brush one pixel. |
| `{count}h` (e.g. `5l`) | Not implemented | No count prefixes in v1. |
| `w` `b` `e` (word motions) | No equivalent | Pixels have no words. |
| `0` `^` `$` (line start/end) | No equivalent | Canvas rows have no home/end bindings yet. |
| `gg` / `G` (file top/bottom) | No equivalent | No canvas-edge jumps in v1. |
| `Ctrl+D` / `Ctrl+U` (half-page) | No equivalent | |
| `%` (matching bracket) | No equivalent | |

## Editing actions

| `x` (delete char) | `x` erases the pixel(s) under the brush | Direct verb map: delete what is under the cursor. Respects brush size. |
| `r` (replace char) | `r` stamps a pixel | Direct verb map: replace what is under the cursor. `Space` also stamps. |
| `i` then type | `i` then move with `hjkl` | PAINT draws with every move until `Esc`. |
| `d` (delete operator) | `x` erases under the brush; `v` … `d` erases the selection | Operator + target. `e` toggles an eraser brush state for painting-style erasing. |
| `dd` (delete line) | `:clear` or `:%d` | Clears the whole canvas. |
| `c` (change operator) | `x` or `v`…`d`, then stamp or paint | Erase then draw. |
| `yy` (yank line) | `v`, move, `y` | Yanks the selected region. |
| `p` / `P` (paste after/before) | `p` | Paste floats the copy at the brush. Move to position, `Enter` stamps it. |
| `u` (undo) | `u` | Direct map. |
| `Ctrl+R` (redo) | `Ctrl+R` | Direct map. |
| `.` (repeat last change) | Not implemented | |
| `>>` / `<<` (indent) | No equivalent | |

## Tools unique to pixel editing

These have no vi counterpart:

| Vixel | What it does |
|---|---|
| `Space` | Stamp a pixel. The brush does not move. `r` does the same (vi "replace char"). |
| `f` | Flood fill from the brush position. |
| `L` | Line: move to the second endpoint, `Enter` commits. |
| `R` | Rectangle: move to the far corner, `Enter` commits, `Shift+Enter` fills. |
| `P` | Eyedropper: pick up the color under the brush. |
| `o` | Brush shape: square / circle. |
| `-`/`_` `=`/`+` | Brush size down / up. |
| `1`–`9`, `0` | Palette slots. `Tab` / `Shift+Tab` flip palette pages. |

## Ex commands

| Vi | Vixel | Notes |
|---|---|---|
| `:w` | `:w [path]` | Writes `.vixel` JSON. |
| `:q` | `:q` | Refuses with unsaved changes. |
| `:q!` | `:q!` | Discards. |
| `:wq` | `:wq` | Direct map. |
| `:e {file}` | `:e {path}` | Opens any supported format (`.vixel`, `.ase`, `.aseprite`, `.px`, `.piskel`). Import flattens layers and frames. |
| `:%d` (delete all lines) | `:%d` / `:clear` | Clears the canvas. |
| Not in vi | `:export {path} [scale]` | Writes PNG. Scale is an integer multiplier. |
| Not in vi | `:color #rrggbb` | Adds a palette color and selects it. |
| Not in vi | `:new [WxH]` | Fresh canvas. Default 64x32. |
| Not in vi | `:resize WxH` | Resizes the canvas. |
| `:set` | No equivalent | |
| `:s///` (substitute) | No equivalent | |

## Intentionally absent

Vixel does not implement these vi features in v1:

- Count prefixes (`5l`, `3dd`)
- Marks (`ma`, `` `a ``)
- Registers (`"ay`)
- Macros (`qa` ... `@a`)
- Search (`/`, `?`, `n`, `N`)
- Text objects (`ciw`, `dap`)
- Splits and tabs

The canvas has no lines, words, or paragraphs. Count prefixes are the most plausible future addition (`10l` to move ten pixels).

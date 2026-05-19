# AeroSwarm Demo Drone Shell — 3D printable

Parametric OpenSCAD model fits all hardware of the demo drone into an X-frame quadcopter shape ~270mm tip-to-tip.

## Quick start

```bash
# Install OpenSCAD (free): https://openscad.org/downloads.html
# Or use online: https://ochafik.com/openscad2/

open aeroswarm_demo_drone.scad   # opens in OpenSCAD GUI
# F5  — fast preview
# F6  — final render (slow)
# F7  — export STL
```

## What gets printed

The `print_layout()` call at the bottom of the .scad places **both pieces side-by-side** on the build plate:

| Piece | Dimensions | Print time @ 0.2mm | Filament |
|---|---|---|---|
| Bottom shell + 4 arms (integrated) | 80×80×23mm + arms to 60mm out | ~3-4h | ~25g PLA |
| Top lid | 80×80×12mm | ~1h | ~8g PLA |

Total: ~4-5h, ~33g PLA. Print as 1 plate or split into 2 jobs.

## Print settings (PLA)

- Layer height: **0.2mm** (0.16 for finer detail on motor mount)
- Infill: **20%** (gyroid or grid)
- Walls: **3 perimeters**
- Top/bottom: **4 layers**
- Supports: **none needed** (lid prints flat side down; arms are horizontal cantilever, OK without)
- Brim: **5mm** (helps arms stick during print)
- Print speed: default ~50mm/s

## Cutouts on the shell

| Cutout | Location | For |
|---|---|---|
| LED RGB window | center top of lid | Status indicator visible from above |
| Buzzer vent (8 small holes) | back-left of lid | Sound passage |
| USB-C cutout | rear of bottom shell, top edge | Plug into ESP32 USB-C for debug/flash |
| Micro-USB cutout | side of bottom shell | Plug into 134N3P for charging |
| BOOT button hole (Φ3mm) | top of lid above ESP32 | Insert pin to reset |
| Motor mount cylinder | tip of each arm | Friction-fit 716 motor |
| Wire pass-through slot | each motor mount → into arm | Route motor wires inside arm |

## Customization — measure your parts first!

Edit the **PARAMETERS** block at the top of `aeroswarm_demo_drone.scad`:

```scad
body_w = 80;          // increase if 134N3P or battery longer than expected
pwr_w = 42;           // ← MEASURE your 134N3P width with calipers
pwr_d = 26;           // ← MEASURE depth
pwr_h = 10;           // ← MEASURE thickness
batt_w = 22;          // Li-Po 502030 width tolerance
batt_d = 32;          // length
motor_dia = 7.2;      // 7.0 motor + 0.2 clearance
```

Press F5 to preview after each tweak.

## Assembly order

1. Print both pieces.
2. **Press-fit 4 motors** into arm tips (light tap with rubber hammer). Wires route through slot into arm channel.
3. **Drop battery + 134N3P** into bottom cradles.
4. **Drop ESP32-C3** into top cradle. USB-C should align with rear cutout.
5. **Route all splice clusters** (see firmware/README.md "Splice clusters" section) and tuck around components.
6. **Hot glue dot** the splice clusters to inside walls so they don't shift.
7. **Test fit lid** — should snap onto 4 alignment pegs. If too tight, sand pegs slightly.
8. **Open lid + USB cutout = service access.** Lid not glued shut — can pop open to swap battery.

## Variations

- **Mock drone shell** (4 of them): same .scad but set `arm_len = 0` + remove peripheral cutouts (LED window, buzzer). Body ~60×60×25mm enough.
- **More compact**: reduce `body_h` to 30mm if your 134N3P is thinner than 10mm.
- **+ frame** instead of X: set `arm_angle = 0`.

## Source verification

Open OpenSCAD → load .scad → press F5. You should see:
- 2 box-shaped pieces side by side
- The body has 4 angled arms with cylinder ends
- The lid has visible cutouts (LED hole, buzzer cluster, BOOT hole)

If anything looks wrong, tweak parameters and F5 again. Once happy, F6 to render, then **File → Export → Export as STL** for each piece (you may need to comment one out and re-render to export them separately).

## Future work (not in scope)

- Add prop guards (rings around each motor)
- Camera mount on front face
- LED strip channel along arms
- Battery hatch (hinged door instead of full lid removal)

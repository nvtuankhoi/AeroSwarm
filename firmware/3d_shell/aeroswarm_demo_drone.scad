// AeroSwarm Demo Drone Shell v2 — parametric OpenSCAD (optimized)
//
// Print 2 STL files: bottom (with arms + gussets) + lid (with SysID emboss).
// Render: F5 preview, F6 final, File → Export STL.
//
// To print just the bottom: render only body_bottom_with_arms()
// To print just the lid:    render only lid()
// Side-by-side print:       use print_layout() (default at the bottom)

// ── PARAMETERS ───────────────────────────────────────────────────────────
sysid         = 2;       // 1..5 — drone identifier (embossed on lid)
include_prop_guards = false;   // optional rings around props (heavier print)
include_logo  = true;    // "AeroSwarm" emboss on top lid front edge

body_w        = 80;
body_d        = 80;
body_h        = 35;
lid_h         = 12;
wall          = 2.0;
arm_len       = 60;
arm_w         = 10;
arm_h         = 8;
arm_angle     = 45;      // X-frame
corner_r      = 4;

motor_dia     = 7.2;
motor_depth   = 16;
motor_collar  = 11;
prop_dia      = 55;
prop_guard_w  = 2;       // prop guard wall thickness

bottom_h      = body_h - lid_h;
batt_w        = 22;
batt_d        = 32;
pwr_w         = 42;      // ← MEASURE 134N3P
pwr_d         = 26;

led_dia       = 5.6;
buzzer_dia    = 12.5;
buzzer_x      = -20;
buzzer_y      = 22;
buzzer_holes  = 8;

usb_c_w       = 9.5;
usb_c_h       = 4.5;
micro_usb_w   = 8.5;
micro_usb_h   = 4.5;

peg_dia       = 4;
peg_h         = 3;

// Optimization-specific dims
wifi_slot_w   = 12;      // WiFi antenna clearance window in lid
wifi_slot_d   = 4;
wifi_slot_x   = 22;      // offset: front-right corner where ESP32 antenna sits
wifi_slot_y   = -22;

foot_dia      = 6;       // anti-skid silicone bumper recess
foot_depth    = 1.5;
foot_offset   = 8;       // from each body corner

vent_count    = 6;       // TIP120 cooling vents (bottom slot)
vent_w        = 14;
vent_h        = 1.5;
vent_spacing  = 4;

emboss_h      = 0.8;     // text emboss depth (negative = into lid, positive = raised)

gusset_size   = 12;      // arm-to-body reinforcement gusset

$fn = 48;

// ── HELPER MODULES ───────────────────────────────────────────────────────
module rbox(w, d, h, r) {
    hull() {
        for (x = [-w/2 + r, w/2 - r])
            for (y = [-d/2 + r, d/2 - r])
                translate([x, y, 0]) cylinder(r=r, h=h);
    }
}

module cradle(cx, cy, z, w, d, h) {
    translate([cx, cy, z])
        difference() {
            cube([w, d, h], center=true);
            translate([0, 0, 1.0])
                cube([w - 3, d - 3, h], center=true);
        }
}

// Right-triangle gusset between arm root and body wall.
// Sits on bed (z=0) so it prints supportless.
module gusset(size) {
    translate([0, 0, 0])
        rotate([90, 0, 0])
            linear_extrude(height = arm_w, center = true)
                polygon([[0, 0], [size, 0], [0, size]]);
}

// Label emboss on interior surface (raised text)
module label(x, y, z, txt, sz, rot = 0) {
    translate([x, y, z])
        rotate([0, 0, rot])
            linear_extrude(0.6)
                text(txt, size=sz, halign="center", valign="center",
                     font="Arial:style=Bold");
}

// Arrow pointing toward USB-C (drone "rear")
module arrow_marker(x, y, z, rot = 0) {
    translate([x, y, z])
        rotate([0, 0, rot])
            linear_extrude(0.6)
                polygon([[-3, -2], [3, 0], [-3, 2], [-1, 0]]);
}

// ── BOTTOM SHELL ─────────────────────────────────────────────────────────
module body_bottom_with_arms() {
    difference() {
        union() {
            // Outer shell
            rbox(body_w, body_d, bottom_h, corner_r);

            // Reinforcement gussets at the 4 arm roots (X-frame corners)
            // Arms sit at z=0 (touch bed during print — no overhang, no supports)
            for (a = [arm_angle, arm_angle + 90, arm_angle + 180, arm_angle + 270]) {
                rotate([0, 0, a])
                    translate([body_w * sqrt(2)/2 - 6, 0, 0])
                        gusset(gusset_size);
            }

            // 4 arms at X corners, FLAT ON BED (z = 0 to arm_h)
            for (a = [arm_angle, arm_angle + 90, arm_angle + 180, arm_angle + 270]) {
                rotate([0, 0, a])
                    translate([body_w * sqrt(2)/2 - 4, 0, 0])
                        arm_with_motor_mount();
            }

            // Optional prop guards (rings around each motor)
            if (include_prop_guards) {
                for (a = [arm_angle, arm_angle + 90, arm_angle + 180, arm_angle + 270]) {
                    rotate([0, 0, a])
                        translate([body_w * sqrt(2)/2 - 4 + arm_len, 0, 0])
                            prop_guard();
                }
            }

        }

        // ── Subtractive cutouts ──────────────────────────────────────────
        // Hollow interior
        translate([0, 0, wall])
            rbox(body_w - 2*wall, body_d - 2*wall,
                 bottom_h - wall + 0.01, corner_r - 1);

        // USB-C cutout — REAR wall (+X), upper half (aligns with ESP32 top tier)
        translate([body_w/2, 8, bottom_h - 7])
            cube([wall*2 + 1, usb_c_w + 1, usb_c_h + 2], center=true);

        // Micro-USB cutout — SAME REAR wall (+X), lower half (134N3P bottom tier)
        translate([body_w/2, -8, 7])
            cube([wall*2 + 1, micro_usb_w + 1, micro_usb_h + 2], center=true);

        // Lid alignment peg sockets (4 corners)
        for (x = [-body_w/2 + corner_r + 2, body_w/2 - corner_r - 2])
            for (y = [-body_d/2 + corner_r + 2, body_d/2 - corner_r - 2])
                translate([x, y, bottom_h - peg_h])
                    cylinder(d=peg_dia + 0.4, h=peg_h + 0.01);

        // Anti-skid feet recesses (bottom of shell)
        for (x = [-body_w/2 + foot_offset, body_w/2 - foot_offset])
            for (y = [-body_d/2 + foot_offset, body_d/2 - foot_offset])
                translate([x, y, -0.01])
                    cylinder(d=foot_dia, h=foot_depth + 0.01);

        // TIP120 cooling vents (slots on bottom under TIP120 cradle position)
        for (i = [0 : vent_count - 1])
            translate([10 + (i - (vent_count-1)/2) * vent_spacing,
                       0, -0.01])
                cube([vent_w, vent_h, wall + 0.1], center=true);
    }

    // ── ADDITIVE INTERIOR FEATURES (placed AFTER hollow subtraction) ─────
    // Layout — 2 tiers stacked at REAR (+X side, USB cutouts here):
    //   TOP TIER (~z=14-20): ESP32 with USB-C pointing +X (rear)
    //   BOT TIER (~z=2-12): 134N3P with Micro-USB pointing +X (rear)
    //   FRONT (-X side): battery in cradle, swappable

    // ESP32 cradle (top tier, near rear +X wall, length along X)
    translate([5, 8, bottom_h - 7])
        difference() {
            cube([28, 22, 5], center=true);
            translate([0, 0, 1.5])
                cube([24, 20, 5], center=true);
        }
    // 134N3P cradle (bottom tier, rear side, length along X)
    cradle(8, -8, wall, pwr_w + 2, pwr_d + 2, 4);
    // Battery cradle (FRONT, -X side, swap-friendly)
    cradle(-22, 0, wall, batt_w + 2, batt_d + 2, 4);

    // Interior labels — raised text guides where each part goes
    label(-22,  -16, wall + 0.01, "BATT",   3.5);
    label( 8,  -8,  wall + 0.01, "134N3P", 3);
    label( 8,  -22, wall + 0.01, "uUSB->", 2.5);
    label( 5,   8,  bottom_h - 7 + 2.7, "ESP32",   3);
    label( 5,   20, bottom_h - 7 + 2.7, "USBC->",  2.5);
    label(-22, 20,  wall + 0.01, "TIPx2",  2.8);

    // Arrows pointing toward rear cutouts (+X)
    arrow_marker(33, 8,  wall + 0.01, 0);
    arrow_marker(33, -8, wall + 0.01, 0);
}

module arm_with_motor_mount() {
    union() {
        // Arm bar — sits from z=0 to z=arm_h (flat on bed for printing)
        translate([arm_len/2, 0, arm_h/2])
            cube([arm_len, arm_w, arm_h], center=true);

        // Motor mount collar at arm tip (rises vertically from arm top)
        translate([arm_len, 0, 0])
            difference() {
                cylinder(d=motor_collar, h=motor_depth);
                translate([0, 0, -0.1])
                    cylinder(d=motor_dia, h=motor_depth + 0.2);
                // Wire pass-through slot toward arm
                translate([-motor_collar/2, 0, motor_depth/2])
                    cube([motor_collar, 2.5, 4], center=true);
            }
    }
}

// Prop guard — ring around motor with 4 thin struts
module prop_guard() {
    guard_dia = prop_dia + 6;
    guard_h = arm_h;
    difference() {
        cylinder(d=guard_dia, h=guard_h);
        translate([0, 0, -0.1])
            cylinder(d=guard_dia - 2*prop_guard_w, h=guard_h + 0.2);
        // Cut top half so prop has clearance (we only need lower half ring)
        translate([-guard_dia, -guard_dia, guard_h/2])
            cube([guard_dia*2, guard_dia*2, guard_h]);
    }
    // Connecting strut to arm
    translate([-guard_dia/2 + prop_guard_w, 0, guard_h/2])
        cube([prop_guard_w * 2, 3, guard_h], center=true);
}

// ── TOP LID ──────────────────────────────────────────────────────────────
module lid() {
    difference() {
        rbox(body_w, body_d, lid_h, corner_r);

        // Hollow underside
        translate([0, 0, -0.01])
            rbox(body_w - 2*wall, body_d - 2*wall,
                 lid_h - wall, corner_r - 1);

        // LED RGB window (center top)
        translate([0, 0, -0.1])
            cylinder(d=led_dia, h=lid_h + 0.2);

        // Buzzer main hole
        translate([buzzer_x, buzzer_y, -0.1])
            cylinder(d=buzzer_dia, h=lid_h + 0.2);
        // Buzzer vent ring
        translate([buzzer_x, buzzer_y, -0.1])
            for (a = [0:360/buzzer_holes:359])
                rotate([0, 0, a])
                    translate([buzzer_dia/2 + 3, 0, 0])
                        cylinder(d=1.5, h=lid_h + 0.2);

        // BOOT button access (above ESP32 GPIO9 button)
        translate([22, -8, -0.1])
            cylinder(d=3, h=lid_h + 0.2);

        // WiFi antenna clearance slot (above ESP32 PCB antenna position)
        translate([wifi_slot_x, wifi_slot_y, -0.1])
            cube([wifi_slot_w, wifi_slot_d, lid_h + 0.2], center=true);

        // SysID number — RECESSED into lid top so it can be color-filled
        // (print bottom-up: at last 1.5mm switch filament color, fills recess)
        translate([0, -body_d/2 + 18, lid_h - 1.5])
            linear_extrude(1.6)   // 1.5mm deep recess + 0.1mm cut-through margin
                text(str(sysid), size=14, halign="center", valign="center",
                     font="Arial:style=Bold");

        // AeroSwarm logo (front-side wall outer face) — keep recessed too
        if (include_logo) {
            translate([0, -body_d/2 + 0.6, lid_h/2])
                rotate([90, 0, 0])
                    linear_extrude(0.7)
                        text("AeroSwarm", size=4, halign="center", valign="center");
        }
    }

    // Alignment pegs (underneath corners)
    for (x = [-body_w/2 + corner_r + 2, body_w/2 - corner_r - 2])
        for (y = [-body_d/2 + corner_r + 2, body_d/2 - corner_r - 2])
            translate([x, y, 0])
                cylinder(d=peg_dia, h=peg_h);

    // Inside-lid labels (visible when lid is flipped over, helps assembly)
    label( 0,    9, wall - 0.01, "RGB",   3, 180);             // mirror text — inside surface
    label( buzzer_x, buzzer_y - 9, wall - 0.01, "BUZZ", 2.8, 180);
    label( 22, -16, wall - 0.01, "BOOT", 2.5, 180);
    label( wifi_slot_x, wifi_slot_y + 4, wall - 0.01, "WiFi", 2.5, 180);
    label( 0, -body_d/2 + 5, wall - 0.01, "FRONT", 3, 180);
}

// ── PRINT LAYOUTS ────────────────────────────────────────────────────────
module print_layout() {
    body_bottom_with_arms();
    // Lid flipped upside-down for printing:
    //  - text-recess sits on bed (first layer = color A → fills SysID number)
    //  - alignment pegs face UP (vertical cylinders, no overhang)
    //  - flat lid top becomes the build-plate-facing surface (clean finish)
    translate([body_w + 80, 0, lid_h])
        rotate([180, 0, 0])
            lid();
}

module assembled() {
    body_bottom_with_arms();
    translate([0, 0, bottom_h + 0.5])
        lid();
}

// ── RENDER ────────────────────────────────────────────────────────────────
// CLI: openscad -o output.stl -D 'render_target="bottom"' -D 'sysid=2' file.scad
// Targets: "layout" | "bottom" | "lid" | "assembled"
render_target = "layout";

if      (render_target == "layout")    print_layout();
else if (render_target == "bottom")    body_bottom_with_arms();
else if (render_target == "lid")
    // Output lid in print-ready orientation (upside-down):
    // text-recess on bed for color change, pegs face up (no overhang)
    translate([0, 0, lid_h])
        rotate([180, 0, 0])
            lid();
else if (render_target == "lid_upright") lid();   // for visual / fit-check only
else if (render_target == "assembled") assembled();
else                                    print_layout();

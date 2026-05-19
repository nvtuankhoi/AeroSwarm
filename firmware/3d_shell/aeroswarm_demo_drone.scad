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

// Right-triangle gusset between arm root and body wall
module gusset(size) {
    rotate([90, 0, 0])
        linear_extrude(height = arm_w, center = true)
            polygon([[0,0], [size, 0], [0, size]]);
}

// ── BOTTOM SHELL ─────────────────────────────────────────────────────────
module body_bottom_with_arms() {
    difference() {
        union() {
            // Outer shell
            rbox(body_w, body_d, bottom_h, corner_r);

            // Reinforcement gussets at the 4 arm roots (X-frame corners)
            for (a = [arm_angle, arm_angle + 90, arm_angle + 180, arm_angle + 270]) {
                rotate([0, 0, a])
                    translate([body_w * sqrt(2)/2 - 6, 0, bottom_h/2 - arm_h/2])
                        gusset(gusset_size);
            }

            // 4 arms at X corners
            for (a = [arm_angle, arm_angle + 90, arm_angle + 180, arm_angle + 270]) {
                rotate([0, 0, a])
                    translate([body_w * sqrt(2)/2 - 4, 0, bottom_h/2 - arm_h/2])
                        arm_with_motor_mount();
            }

            // Optional prop guards (rings around each motor)
            if (include_prop_guards) {
                for (a = [arm_angle, arm_angle + 90, arm_angle + 180, arm_angle + 270]) {
                    rotate([0, 0, a])
                        translate([body_w * sqrt(2)/2 - 4 + arm_len, 0, bottom_h/2 - arm_h/2])
                            prop_guard();
                }
            }

            // Cradles inside (low walls, hot-glue components in)
            cradle(-15, 0, wall, batt_w + 2, batt_d + 2, 4);
            cradle(15, 0, wall, pwr_w + 2, pwr_d + 2, 4);
        }

        // ── Subtractive cutouts ──────────────────────────────────────────
        // Hollow interior
        translate([0, 0, wall])
            rbox(body_w - 2*wall, body_d - 2*wall,
                 bottom_h - wall + 0.01, corner_r - 1);

        // USB-C port cutout (drone rear, +X)
        translate([body_w/2 - wall, 0, bottom_h - 8])
            cube([wall*2 + 0.5, usb_c_w, usb_c_h + 2], center=true);

        // Micro-USB cutout (drone side +Y, for 134N3P)
        translate([5, body_d/2 - wall, 6])
            cube([micro_usb_w, wall*2 + 0.5, micro_usb_h + 2], center=true);

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
}

module arm_with_motor_mount() {
    union() {
        // Arm bar
        translate([arm_len/2, 0, arm_h/2])
            cube([arm_len, arm_w, arm_h], center=true);

        // Cable channel along arm top (groove for motor wires)
        translate([arm_len/2, 0, arm_h - 1.2])
            difference() {
                cube([arm_len - 4, arm_w - 2, 0.01], center=true);
                cube([arm_len - 6, arm_w - 4, 0.02], center=true);
            }

        // Motor mount collar at arm tip
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

        // SysID emboss on top (raised text 1..5)
        translate([0, -body_d/2 + 18, lid_h - emboss_h])
            linear_extrude(emboss_h + 0.1)
                text(str(sysid), size=10, halign="center", valign="center",
                     font="Arial:style=Bold");

        // AeroSwarm logo (front-side wall outer face)
        if (include_logo) {
            translate([0, -body_d/2 + 0.1, lid_h/2])
                rotate([90, 0, 0])
                    linear_extrude(emboss_h + 0.1)
                        text("AeroSwarm", size=4, halign="center", valign="center");
        }
    }

    // Alignment pegs (underneath corners)
    for (x = [-body_w/2 + corner_r + 2, body_w/2 - corner_r - 2])
        for (y = [-body_d/2 + corner_r + 2, body_d/2 - corner_r - 2])
            translate([x, y, 0])
                cylinder(d=peg_dia, h=peg_h);
}

// ── PRINT LAYOUTS ────────────────────────────────────────────────────────
module print_layout() {
    body_bottom_with_arms();
    translate([body_w + 80, 0, 0])
        lid();
}

module assembled() {
    body_bottom_with_arms();
    translate([0, 0, bottom_h + 0.5])
        lid();
}

// ── RENDER (uncomment one) ───────────────────────────────────────────────
print_layout();
// assembled();
// body_bottom_with_arms();
// lid();

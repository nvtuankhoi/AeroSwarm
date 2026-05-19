// AeroSwarm Demo Drone Shell — parametric OpenSCAD
//
// Print 2 STL files: bottom (with arms) + lid.
// All dimensions in mm. Tweak the PARAMETERS block to fit your actual parts.
//
// Render: OpenSCAD → F5 preview, F6 render, F7 export STL.
// Print: PLA, 0.2mm layer, 20% infill, no supports needed for the lid.
//        Arms have 1.5mm wall + integrated motor mount.
//
// To print just bottom: comment out `lid_side_by_side()` at the end.
// To print just lid:    comment out `body_bottom_with_arms()`.

// ── PARAMETERS ───────────────────────────────────────────────────────────
body_w        = 80;     // body width  (front-back axis)
body_d        = 80;     // body depth  (left-right axis)
body_h        = 35;     // total height
lid_h         = 12;     // lid height (rest is bottom shell)
wall          = 2.0;    // shell wall thickness
arm_len       = 60;     // arm length from body edge to motor mount center
arm_w         = 10;     // arm width
arm_h         = 8;      // arm height
arm_angle     = 45;     // X-frame quad (45°) or use 0/90 for + frame
corner_r      = 4;      // body corner radius

// Motor mount
motor_dia     = 7.2;    // 716 motor diameter + 0.2mm clearance
motor_depth   = 16;     // motor body length
motor_collar  = 11;     // outer diameter of mount collar

// Internal compartments
bottom_h      = body_h - lid_h;      // bottom shell height
batt_w        = 22;     // Li-Po 502030 width clearance
batt_d        = 32;     // length
batt_h        = 7;      // height
pwr_w         = 42;     // 134N3P width clearance — MEASURE YOURS
pwr_d         = 26;     // length
pwr_h         = 10;     // height
esp_w         = 24;     // ESP32-C3 Super Mini + tolerance
esp_d         = 20;
esp_h         = 8;

// Lid cutouts
led_dia       = 5.6;    // LED RGB 5mm hole
buzzer_dia    = 12.5;   // buzzer TMB12A05 cylinder
buzzer_x      = -20;    // buzzer offset from center
buzzer_y      = 22;
buzzer_holes  = 8;      // small vent holes around buzzer

// Side cutouts
usb_c_w       = 9.5;    // ESP32 USB-C plug clearance
usb_c_h       = 4.5;
micro_usb_w   = 8.5;    // 134N3P Micro-USB plug clearance
micro_usb_h   = 4.5;

// Snap-fit lid pegs (corners)
peg_dia       = 4;
peg_h         = 3;

$fn = 48;

// ── HELPER: rounded box ──────────────────────────────────────────────────
module rbox(w, d, h, r) {
    hull() {
        for (x = [-w/2 + r, w/2 - r])
            for (y = [-d/2 + r, d/2 - r])
                translate([x, y, 0]) cylinder(r=r, h=h);
    }
}

// ── HELPER: component cradle (low side walls only, open top) ─────────────
module cradle(cx, cy, z, w, d, h) {
    translate([cx, cy, z])
        difference() {
            cube([w, d, h], center=true);
            translate([0, 0, 1.0])
                cube([w - 3, d - 3, h], center=true);
        }
}

// ── BOTTOM SHELL ─────────────────────────────────────────────────────────
module body_bottom_with_arms() {
    union() {
        difference() {
            // Outer shell
            rbox(body_w, body_d, bottom_h, corner_r);

            // Hollow interior
            translate([0, 0, wall])
                rbox(body_w - 2*wall, body_d - 2*wall,
                     bottom_h - wall + 0.01, corner_r - 1);

            // USB-C port cutout (drone rear, +X side, top of bottom shell)
            translate([body_w/2 - wall, 0, bottom_h - esp_h - 1])
                cube([wall + 1, usb_c_w, usb_c_h], center=true);

            // Micro-USB cutout (drone side, +Y, for 134N3P)
            translate([5, body_d/2 - wall, pwr_h/2 + wall])
                cube([micro_usb_w, wall + 1, micro_usb_h], center=true);

            // Lid alignment pegs (recessed sockets in bottom rim)
            for (x = [-body_w/2 + corner_r + 2, body_w/2 - corner_r - 2])
                for (y = [-body_d/2 + corner_r + 2, body_d/2 - corner_r - 2])
                    translate([x, y, bottom_h - peg_h])
                        cylinder(d=peg_dia + 0.4, h=peg_h + 0.01);
        }

        // Internal cradles (centered, low-wall, no roof — hot-glue components in)
        // Battery cradle (left side, centered on Y)
        cradle(-15, 0, wall, batt_w + 2, batt_d + 2, 4);
        // Power module cradle (right side, centered on Y)
        cradle(15, 0, wall, pwr_w + 2, pwr_d + 2, 4);

        // 4 arms at X corners
        for (a = [arm_angle, arm_angle + 90, arm_angle + 180, arm_angle + 270]) {
            rotate([0, 0, a])
                translate([body_w/2 * cos(45), 0, bottom_h/2 - arm_h/2])
                    arm_with_motor_mount();
        }
    }
}

module arm_with_motor_mount() {
    union() {
        // Arm bar
        translate([arm_len/2, 0, arm_h/2])
            cube([arm_len, arm_w, arm_h], center=true);
        // Motor mount collar at arm tip
        translate([arm_len, 0, 0])
            difference() {
                cylinder(d=motor_collar, h=motor_depth);
                translate([0, 0, -0.1])
                    cylinder(d=motor_dia, h=motor_depth + 0.2);
                // Wire pass-through slot to inside of arm
                translate([-motor_collar/2, 0, motor_depth/2])
                    cube([motor_collar, 3, 4], center=true);
            }
    }
}

// ── TOP LID ──────────────────────────────────────────────────────────────
module lid() {
    difference() {
        rbox(body_w, body_d, lid_h, corner_r);

        // Hollow underside (skin = wall)
        translate([0, 0, -0.01])
            rbox(body_w - 2*wall, body_d - 2*wall,
                 lid_h - wall, corner_r - 1);

        // LED RGB window (center)
        translate([0, 0, -0.1])
            cylinder(d=led_dia, h=lid_h + 0.2);

        // Buzzer hole cluster
        translate([buzzer_x, buzzer_y, -0.1])
            cylinder(d=buzzer_dia, h=lid_h + 0.2);
        // Buzzer vent holes around
        translate([buzzer_x, buzzer_y, -0.1])
            for (a = [0:360/buzzer_holes:359])
                rotate([0, 0, a])
                    translate([buzzer_dia/2 + 3, 0, 0])
                        cylinder(d=1.5, h=lid_h + 0.2);

        // BOOT button access hole (above ESP32 BOOT button location)
        translate([15, -10 + esp_d/2, -0.1])
            cylinder(d=3, h=lid_h + 0.2);
    }

    // Alignment pegs (4 corners, underneath)
    for (x = [-body_w/2 + corner_r + 2, body_w/2 - corner_r - 2])
        for (y = [-body_d/2 + corner_r + 2, body_d/2 - corner_r - 2])
            translate([x, y, 0])
                cylinder(d=peg_dia, h=peg_h);
}

// ── PRINT LAYOUT (both pieces side-by-side for one print) ────────────────
module print_layout() {
    body_bottom_with_arms();
    translate([body_w + arm_len*2 + 20, 0, 0])
        lid();
}

// Pick one: render whole assembled, or print layout
// assembled();
print_layout();

// ── ASSEMBLED PREVIEW (for visualization, not for printing) ──────────────
module assembled() {
    body_bottom_with_arms();
    translate([0, 0, bottom_h + 0.5])
        lid();
}

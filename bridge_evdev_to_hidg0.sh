#!/system/bin/sh
#
# Bridge one Android evdev gamepad (/dev/input/eventX) to /dev/hidg0.
#
# Output report matches setup_y700_gamepad_v2.sh:
#   byte 0-1: 16 buttons
#   byte 2  : hat switch, 8 neutral
#   byte 3  : X
#   byte 4  : Y
#   byte 5  : Z, default sourced from ABS_RX or ABS_Z
#   byte 6  : Rz, default sourced from ABS_RY or ABS_RZ
#   byte 7  : reserved

set -e

EVENT="${1:-$EVENT}"
HID="${HID:-/dev/hidg0}"
DEADZONE="${DEADZONE:-6}"
VERBOSE="${VERBOSE:-1}"

buttons=0
dpad_up=0
dpad_down=0
dpad_left=0
dpad_right=0
hat=8
x=0
y=0
z=0
rz=0

x_min=-32768
x_max=32767
y_min=-32768
y_max=32767
z_min=-32768
z_max=32767
rz_min=-32768
rz_max=32767
z_code_name=ABS_Z
z_code_hex=0002
rz_code_name=ABS_RZ
rz_code_hex=0005

die() {
    echo "ERROR: $*" >&2
    exit 1
}

find_event() {
    getevent -lp 2>/dev/null | awk '
    function flush() {
        if (path != "" && score > best_score) {
            best_path = path
            best_name = name
            best_score = score
        }
    }

    /^add device/ {
        flush()
        path = $4
        name = ""
        score = 0
        next
    }

    /^[[:space:]]+name:/ {
        name = $0
        sub(/^[^\"]*\"/, "", name)
        sub(/\".*/, "", name)
        next
    }

    /BTN_SOUTH|BTN_A|BTN_EAST|BTN_B|BTN_NORTH|BTN_X|BTN_WEST|BTN_Y/ { score += 5 }
    /BTN_TL|BTN_TR|BTN_TL2|BTN_TR2|BTN_SELECT|BTN_START|BTN_MODE|BTN_THUMBL|BTN_THUMBR/ { score += 4 }
    /BTN_DPAD_UP|BTN_DPAD_DOWN|BTN_DPAD_LEFT|BTN_DPAD_RIGHT/ { score += 4 }
    /ABS_HAT0X|ABS_HAT0Y/ { score += 4 }
    /ABS_RX|ABS_RY/ { score += 3 }

    END {
        flush()
        if (best_path != "") {
            print best_path
        }
    }'
}

hex2() {
    n="$(($1 & 255))"
    printf '%02x' "$n"
}

write_report() {
    b0="$((buttons & 255))"
    b1="$(((buttons >> 8) & 255))"
    report="\\x$(hex2 "$b0")\\x$(hex2 "$b1")\\x$(hex2 "$hat")\\x$(hex2 "$x")\\x$(hex2 "$y")\\x$(hex2 "$z")\\x$(hex2 "$rz")\\x00"
    printf '%b' "$report" > "$HID"
}

neutral_report() {
    buttons=0
    dpad_up=0
    dpad_down=0
    dpad_left=0
    dpad_right=0
    hat=8
    x=0
    y=0
    z=0
    rz=0
    write_report 2>/dev/null || true
}

set_button() {
    bit="$1"
    value="$2"

    if [ "$value" != "0" ]; then
        buttons="$((buttons | (1 << bit)))"
    else
        buttons="$((buttons & ~(1 << bit)))"
    fi
}

update_hat() {
    if [ "$dpad_up" = "1" ] && [ "$dpad_right" = "1" ]; then
        hat=1
    elif [ "$dpad_right" = "1" ] && [ "$dpad_down" = "1" ]; then
        hat=3
    elif [ "$dpad_down" = "1" ] && [ "$dpad_left" = "1" ]; then
        hat=5
    elif [ "$dpad_left" = "1" ] && [ "$dpad_up" = "1" ]; then
        hat=7
    elif [ "$dpad_up" = "1" ]; then
        hat=0
    elif [ "$dpad_right" = "1" ]; then
        hat=2
    elif [ "$dpad_down" = "1" ]; then
        hat=4
    elif [ "$dpad_left" = "1" ]; then
        hat=6
    else
        hat=8
    fi
}

scale_axis() {
    raw="$1"
    min="$2"
    max="$3"

    if [ "$max" = "$min" ]; then
        echo 0
        return
    fi

    out="$((((raw - min) * 254 / (max - min)) - 127))"
    [ "$out" -gt 127 ] && out=127
    [ "$out" -lt -127 ] && out=-127

    abs="$out"
    [ "$abs" -lt 0 ] && abs="$((-abs))"
    [ "$abs" -le "$DEADZONE" ] && out=0

    echo "$out"
}

read_axis_range() {
    axis="$1"
    default_min="$2"
    default_max="$3"

    getevent -lp "$EVENT" 2>/dev/null | awk -v axis="$axis" -v dmin="$default_min" -v dmax="$default_max" '
    $1 == axis {
        min = dmin
        max = dmax
        for (i = 1; i <= NF; i++) {
            if ($i == "min") {
                min = $(i + 1)
                gsub(/,/, "", min)
            }
            if ($i == "max") {
                max = $(i + 1)
                gsub(/,/, "", max)
            }
        }
        print min " " max
        found = 1
        exit
    }
    END {
        if (!found) {
            print dmin " " dmax
        }
    }'
}

load_axis_ranges() {
    set -- $(read_axis_range ABS_X -32768 32767)
    x_min="$1"; x_max="$2"
    set -- $(read_axis_range ABS_Y -32768 32767)
    y_min="$1"; y_max="$2"

    if getevent -lp "$EVENT" 2>/dev/null | grep -q 'ABS_RX'; then
        set -- $(read_axis_range ABS_RX -32768 32767)
        z_min="$1"; z_max="$2"
        z_code_name=ABS_RX
        z_code_hex=0003
    else
        set -- $(read_axis_range ABS_Z -32768 32767)
        z_min="$1"; z_max="$2"
        z_code_name=ABS_Z
        z_code_hex=0002
    fi

    if getevent -lp "$EVENT" 2>/dev/null | grep -q 'ABS_RY'; then
        set -- $(read_axis_range ABS_RY -32768 32767)
        rz_min="$1"; rz_max="$2"
        rz_code_name=ABS_RY
        rz_code_hex=0004
    else
        set -- $(read_axis_range ABS_RZ -32768 32767)
        rz_min="$1"; rz_max="$2"
        rz_code_name=ABS_RZ
        rz_code_hex=0005
    fi
}

hex_value() {
    # Android getevent prints hex values. mksh handles signed 32-bit values here.
    echo "$((0x$1))"
}

handle_key() {
    code="$1"
    value="$2"

    case "$code" in
        BTN_SOUTH|BTN_A|0130) set_button 0 "$value" ;;
        BTN_EAST|BTN_B|0131) set_button 1 "$value" ;;
        BTN_WEST|BTN_Y|0134) set_button 2 "$value" ;;
        BTN_NORTH|BTN_X|0133) set_button 3 "$value" ;;
        BTN_TL|0136) set_button 4 "$value" ;;
        BTN_TR|0137) set_button 5 "$value" ;;
        BTN_TL2|0138) set_button 6 "$value" ;;
        BTN_TR2|0139) set_button 7 "$value" ;;
        BTN_SELECT|KEY_BACK|013a) set_button 8 "$value" ;;
        BTN_START|KEY_MENU|013b) set_button 9 "$value" ;;
        BTN_MODE|KEY_HOMEPAGE|013c) set_button 10 "$value" ;;
        BTN_THUMBL|013d) set_button 11 "$value" ;;
        BTN_THUMBR|013e) set_button 12 "$value" ;;
        BTN_DPAD_UP|0220) dpad_up="$([ "$value" != "0" ] && echo 1 || echo 0)"; update_hat ;;
        BTN_DPAD_DOWN|0221) dpad_down="$([ "$value" != "0" ] && echo 1 || echo 0)"; update_hat ;;
        BTN_DPAD_LEFT|0222) dpad_left="$([ "$value" != "0" ] && echo 1 || echo 0)"; update_hat ;;
        BTN_DPAD_RIGHT|0223) dpad_right="$([ "$value" != "0" ] && echo 1 || echo 0)"; update_hat ;;
    esac
}

handle_abs() {
    code="$1"
    value="$2"

    case "$code" in
        ABS_X|0000) x="$(scale_axis "$value" "$x_min" "$x_max")" ;;
        ABS_Y|0001) y="$(scale_axis "$value" "$y_min" "$y_max")" ;;
        ABS_HAT0X|0010)
            if [ "$value" -lt 0 ]; then
                dpad_left=1; dpad_right=0
            elif [ "$value" -gt 0 ]; then
                dpad_left=0; dpad_right=1
            else
                dpad_left=0; dpad_right=0
            fi
            update_hat
            ;;
        ABS_HAT0Y|0011)
            if [ "$value" -lt 0 ]; then
                dpad_up=1; dpad_down=0
            elif [ "$value" -gt 0 ]; then
                dpad_up=0; dpad_down=1
            else
                dpad_up=0; dpad_down=0
            fi
            update_hat
            ;;
    esac

    if [ "$code" = "$z_code_name" ] || [ "$code" = "$z_code_hex" ]; then
        z="$(scale_axis "$value" "$z_min" "$z_max")"
    elif [ "$code" = "$rz_code_name" ] || [ "$code" = "$rz_code_hex" ]; then
        rz="$(scale_axis "$value" "$rz_min" "$rz_max")"
    fi
}

[ -n "$EVENT" ] || EVENT="$(find_event)"
[ -n "$EVENT" ] || die "No likely gamepad event found. Pair/connect the controller, then run list_gamepad_events.sh."
[ -e "$EVENT" ] || die "Missing event device: $EVENT"
[ -e "$HID" ] || die "Missing $HID. Run setup_y700_gamepad_v2.sh first."

load_axis_ranges

if [ "$VERBOSE" != "0" ]; then
    echo "Bridging $EVENT -> $HID"
    echo "Axis ranges:"
    echo "  X : $x_min..$x_max"
    echo "  Y : $y_min..$y_max"
    echo "  Z : $z_min..$z_max from $z_code_name"
    echo "  Rz: $rz_min..$rz_max from $rz_code_name"
    echo "Press Ctrl+C to stop. Sending neutral report on exit."
fi

trap 'neutral_report; exit 0' INT TERM EXIT
neutral_report

getevent -l "$EVENT" | while read dev type code raw extra; do
    [ -n "$type" ] || continue
    value="$(hex_value "$raw")"

    case "$type" in
        EV_KEY|0001) handle_key "$code" "$value" ;;
        EV_ABS|0003) handle_abs "$code" "$value" ;;
        EV_SYN|0000) write_report ;;
    esac
done

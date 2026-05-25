#!/system/bin/sh
#
# Bridge one Android evdev gamepad (/dev/input/eventX) into the Switch 2
# responder state file consumed by Switch2FfsResponder.
#
# Output state fields:
#   b5: Y X B A R ZR
#   b6: - + RStick LStick Home Capture C
#   b7: Down Up Right Left L ZL
#   b8: paddles
#   lx/ly/rx/ry: 12-bit stick positions, neutral 2048

set -e

EVENT="${1:-$EVENT}"
STATE="${STATE:-/data/local/tmp/switch2_state.txt}"
DEADZONE="${DEADZONE:-96}"
VERBOSE="${VERBOSE:-1}"

b5=0
b6=0
b7=0
b8=0
lx=2048
ly=2048
rx=2048
ry=2048

lx_min=-32768
lx_max=32767
ly_min=-32768
ly_max=32767
rx_min=-32768
rx_max=32767
ry_min=-32768
ry_max=32767
rx_code_name=ABS_RX
rx_code_hex=0003
ry_code_name=ABS_RY
ry_code_hex=0004

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

write_state() {
    printf 'b5=%s b6=%s b7=%s b8=%s lx=%s ly=%s rx=%s ry=%s\n' \
        "$(hex2 "$b5")" "$(hex2 "$b6")" "$(hex2 "$b7")" "$(hex2 "$b8")" \
        "$lx" "$ly" "$rx" "$ry" > "$STATE"
}

neutral_state() {
    b5=0
    b6=0
    b7=0
    b8=0
    lx=2048
    ly=2048
    rx=2048
    ry=2048
    write_state 2>/dev/null || true
}

set_mask() {
    var="$1"
    mask="$2"
    value="$3"
    current="$(eval "echo \$$var")"

    if [ "$value" != "0" ]; then
        current="$((current | mask))"
    else
        current="$((current & ~mask))"
    fi
    eval "$var=$current"
}

scale_axis12() {
    raw="$1"
    min="$2"
    max="$3"

    if [ "$max" = "$min" ]; then
        echo 2048
        return
    fi

    out="$(((raw - min) * 4095 / (max - min)))"
    [ "$out" -gt 4095 ] && out=4095
    [ "$out" -lt 0 ] && out=0

    delta="$((out - 2048))"
    [ "$delta" -lt 0 ] && delta="$((-delta))"
    [ "$delta" -le "$DEADZONE" ] && out=2048

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
    lx_min="$1"; lx_max="$2"
    set -- $(read_axis_range ABS_Y -32768 32767)
    ly_min="$1"; ly_max="$2"

    if getevent -lp "$EVENT" 2>/dev/null | grep -q 'ABS_RX'; then
        set -- $(read_axis_range ABS_RX -32768 32767)
        rx_min="$1"; rx_max="$2"
        rx_code_name=ABS_RX
        rx_code_hex=0003
    else
        set -- $(read_axis_range ABS_Z -32768 32767)
        rx_min="$1"; rx_max="$2"
        rx_code_name=ABS_Z
        rx_code_hex=0002
    fi

    if getevent -lp "$EVENT" 2>/dev/null | grep -q 'ABS_RY'; then
        set -- $(read_axis_range ABS_RY -32768 32767)
        ry_min="$1"; ry_max="$2"
        ry_code_name=ABS_RY
        ry_code_hex=0004
    else
        set -- $(read_axis_range ABS_RZ -32768 32767)
        ry_min="$1"; ry_max="$2"
        ry_code_name=ABS_RZ
        ry_code_hex=0005
    fi
}

hex_value() {
    echo "$((0x$1))"
}

handle_key() {
    code="$1"
    value="$2"

    case "$code" in
        BTN_WEST|BTN_Y|0134) set_mask b5 0x01 "$value" ;;
        BTN_NORTH|BTN_X|0133) set_mask b5 0x02 "$value" ;;
        BTN_SOUTH|BTN_B|0130) set_mask b5 0x04 "$value" ;;
        BTN_EAST|BTN_A|0131) set_mask b5 0x08 "$value" ;;
        BTN_TR|0137) set_mask b5 0x40 "$value" ;;
        BTN_TR2|0139) set_mask b5 0x80 "$value" ;;

        BTN_SELECT|KEY_BACK|013a) set_mask b6 0x01 "$value" ;;
        BTN_START|KEY_MENU|013b) set_mask b6 0x02 "$value" ;;
        BTN_THUMBR|013e) set_mask b6 0x04 "$value" ;;
        BTN_THUMBL|013d) set_mask b6 0x08 "$value" ;;
        BTN_MODE|KEY_HOMEPAGE|013c) set_mask b6 0x10 "$value" ;;

        BTN_DPAD_DOWN|0221) set_mask b7 0x01 "$value" ;;
        BTN_DPAD_UP|0220) set_mask b7 0x02 "$value" ;;
        BTN_DPAD_RIGHT|0223) set_mask b7 0x04 "$value" ;;
        BTN_DPAD_LEFT|0222) set_mask b7 0x08 "$value" ;;
        BTN_TL|0136) set_mask b7 0x40 "$value" ;;
        BTN_TL2|0138) set_mask b7 0x80 "$value" ;;
    esac
}

handle_abs() {
    code="$1"
    value="$2"

    case "$code" in
        ABS_X|0000) lx="$(scale_axis12 "$value" "$lx_min" "$lx_max")" ;;
        ABS_Y|0001) ly="$(scale_axis12 "$value" "$ly_min" "$ly_max")" ;;
        ABS_HAT0X|0010)
            if [ "$value" -lt 0 ]; then
                set_mask b7 0x08 1
                set_mask b7 0x04 0
            elif [ "$value" -gt 0 ]; then
                set_mask b7 0x08 0
                set_mask b7 0x04 1
            else
                set_mask b7 0x08 0
                set_mask b7 0x04 0
            fi
            ;;
        ABS_HAT0Y|0011)
            if [ "$value" -lt 0 ]; then
                set_mask b7 0x02 1
                set_mask b7 0x01 0
            elif [ "$value" -gt 0 ]; then
                set_mask b7 0x02 0
                set_mask b7 0x01 1
            else
                set_mask b7 0x02 0
                set_mask b7 0x01 0
            fi
            ;;
    esac

    if [ "$code" = "$rx_code_name" ] || [ "$code" = "$rx_code_hex" ]; then
        rx="$(scale_axis12 "$value" "$rx_min" "$rx_max")"
    elif [ "$code" = "$ry_code_name" ] || [ "$code" = "$ry_code_hex" ]; then
        ry="$(scale_axis12 "$value" "$ry_min" "$ry_max")"
    fi
}

[ -n "$EVENT" ] || EVENT="$(find_event)"
[ -n "$EVENT" ] || die "No likely gamepad event found. Pair/connect a controller, then run list_gamepad_events.sh."
[ -e "$EVENT" ] || die "Missing event device: $EVENT"

load_axis_ranges

if [ "$VERBOSE" != "0" ]; then
    echo "Bridging $EVENT -> $STATE"
    echo "Axis ranges:"
    echo "  LX: $lx_min..$lx_max"
    echo "  LY: $ly_min..$ly_max"
    echo "  RX: $rx_min..$rx_max from $rx_code_name"
    echo "  RY: $ry_min..$ry_max from $ry_code_name"
    echo "Press Ctrl+C to stop. Sending neutral state on exit."
fi

trap 'neutral_state; exit 0' INT TERM EXIT
neutral_state

getevent -l "$EVENT" | while read dev type code raw extra; do
    [ -n "$type" ] || continue
    value="$(hex_value "$raw")"

    case "$type" in
        EV_KEY|0001) handle_key "$code" "$value" ;;
        EV_ABS|0003) handle_abs "$code" "$value" ;;
        EV_SYN|0000) write_state ;;
    esac
done

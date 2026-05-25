#!/system/bin/sh
#
# Print likely /dev/input/eventX nodes for Bluetooth/USB game controllers.

getevent -lp 2>/dev/null | awk '
function flush() {
    if (path != "" && score > 0) {
        printf "%s\t%s\tscore=%d\n", path, name, score
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
}
'

#!/system/bin/sh

STATE="${STATE:-/data/local/tmp/switch2_state.txt}"
BUTTON="${1:-a}"
MS="${2:-180}"

press() {
    case "$1" in
        b) echo "b5=0x01" > "$STATE" ;;
        a) echo "b5=0x02" > "$STATE" ;;
        y) echo "b5=0x04" > "$STATE" ;;
        x) echo "b5=0x08" > "$STATE" ;;
        r) echo "b5=0x10" > "$STATE" ;;
        zr) echo "b5=0x20" > "$STATE" ;;
        plus|start) echo "b5=0x40" > "$STATE" ;;
        rstick) echo "b5=0x80" > "$STATE" ;;
        down) echo "b6=0x01" > "$STATE" ;;
        right) echo "b6=0x02" > "$STATE" ;;
        left) echo "b6=0x04" > "$STATE" ;;
        up) echo "b6=0x08" > "$STATE" ;;
        l) echo "b6=0x10" > "$STATE" ;;
        zl) echo "b6=0x20" > "$STATE" ;;
        minus|back) echo "b6=0x40" > "$STATE" ;;
        lstick) echo "b6=0x80" > "$STATE" ;;
        home|guide) echo "b7=0x01" > "$STATE" ;;
        capture|share) echo "b7=0x02" > "$STATE" ;;
        gr) echo "b7=0x04" > "$STATE" ;;
        gl) echo "b7=0x08" > "$STATE" ;;
        c|camera) echo "b7=0x10" > "$STATE" ;;
        lx-left) echo "lx=0 ly=2048" > "$STATE" ;;
        lx-right) echo "lx=4095 ly=2048" > "$STATE" ;;
        ly-up) echo "lx=2048 ly=0" > "$STATE" ;;
        ly-down) echo "lx=2048 ly=4095" > "$STATE" ;;
        rx-left) echo "rx=0 ry=2048" > "$STATE" ;;
        rx-right) echo "rx=4095 ry=2048" > "$STATE" ;;
        ry-up) echo "rx=2048 ry=0" > "$STATE" ;;
        ry-down) echo "rx=2048 ry=4095" > "$STATE" ;;
        neutral|release) echo "" > "$STATE" ;;
        *)
            echo "Usage: $0 a|b|x|y|l|r|zl|zr|plus|minus|home|capture|c|gl|gr|up|down|left|right|lx-left|lx-right|ly-up|ly-down|rx-left|rx-right|ry-up|ry-down [ms]" >&2
            exit 1
            ;;
    esac
}

press "$BUTTON"
sleep "$(awk "BEGIN { printf \"%.3f\", $MS / 1000 }")"
echo "" > "$STATE"

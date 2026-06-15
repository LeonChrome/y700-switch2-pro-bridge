using System;

namespace Y700Switch2V60Viiper;

public sealed class Pro2InputStabilityFilter
{
    private const int AxisJumpThreshold = 1200;
    private const int ConfirmationTolerance = 384;
    private GamepadState? lastAccepted;
    private GamepadState? pendingAxisJump;

    public bool TryAccept(
        GamepadState parsed,
        out GamepadState accepted,
        out string reason)
    {
        accepted = parsed;
        reason = "";

        if (lastAccepted == null)
        {
            Accept(parsed, out accepted);
            return true;
        }

        if (IsLargeAxisJump(lastAccepted, parsed))
        {
            if (pendingAxisJump != null &&
                AxesAreSimilar(pendingAxisJump, parsed))
            {
                pendingAxisJump = null;
                Accept(parsed, out accepted);
                return true;
            }

            pendingAxisJump = parsed.Clone();
            reason = "axis_spike_pending";
            accepted = lastAccepted.Clone();
            return false;
        }

        pendingAxisJump = null;
        Accept(parsed, out accepted);
        return true;
    }

    public void Reset()
    {
        lastAccepted = null;
        pendingAxisJump = null;
    }

    private void Accept(GamepadState state, out GamepadState accepted)
    {
        accepted = state.Clone();
        lastAccepted = accepted.Clone();
    }

    private static bool IsLargeAxisJump(GamepadState previous, GamepadState current)
    {
        return AxisDelta(previous.Lx, current.Lx) >= AxisJumpThreshold ||
               AxisDelta(previous.Ly, current.Ly) >= AxisJumpThreshold ||
               AxisDelta(previous.Rx, current.Rx) >= AxisJumpThreshold ||
               AxisDelta(previous.Ry, current.Ry) >= AxisJumpThreshold;
    }

    private static bool AxesAreSimilar(GamepadState a, GamepadState b)
    {
        return AxisDelta(a.Lx, b.Lx) <= ConfirmationTolerance &&
               AxisDelta(a.Ly, b.Ly) <= ConfirmationTolerance &&
               AxisDelta(a.Rx, b.Rx) <= ConfirmationTolerance &&
               AxisDelta(a.Ry, b.Ry) <= ConfirmationTolerance;
    }

    private static int AxisDelta(ushort a, ushort b)
    {
        int delta = a - b;
        return delta < 0 ? -delta : delta;
    }
}

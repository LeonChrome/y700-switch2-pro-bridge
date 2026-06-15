using System;

namespace Y700Switch2V60Viiper;

public static class InputContinuityPolicy
{
    public static readonly TimeSpan FreshInputAge = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan HoldLastStateAge = TimeSpan.FromMilliseconds(750);
    public static readonly TimeSpan SafeReleaseAge = TimeSpan.FromMilliseconds(2000);

    public static GamepadState Resolve(
        GamepadState latest,
        TimeSpan age,
        out string source)
    {
        if (age <= FreshInputAge)
        {
            source = "pro2_ble";
            return latest;
        }

        if (age <= HoldLastStateAge)
        {
            source = "pro2_ble_hold";
            return latest;
        }

        if (age <= SafeReleaseAge)
        {
            source = "pro2_ble_safe_hold";
            return SafeHoldAnalog(latest);
        }

        source = "neutral";
        return GamepadState.Neutral();
    }

    private static GamepadState SafeHoldAnalog(GamepadState state)
    {
        GamepadState result = state.Clone();
        result.Buttons = GamepadButtons.None;
        result.L2 = 0;
        result.R2 = 0;
        result.AccelValid = false;
        result.GyroValid = false;
        return result;
    }
}

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

public static class SteamInputVibrationProbe
{
    private const int MaxControllers = 16;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool SteamAPI_Init();

    [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SteamAPI_Shutdown();

    [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SteamAPI_RunCallbacks();

    [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SteamAPI_SteamInput_v005();

    [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool SteamAPI_ISteamInput_Init(IntPtr self, bool explicitlyCallRunFrame);

    [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SteamAPI_ISteamInput_RunFrame(IntPtr self, bool reserved);

    [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SteamAPI_ISteamInput_GetConnectedControllers(IntPtr self, [Out] ulong[] handlesOut);

    [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SteamAPI_ISteamInput_TriggerVibration(IntPtr self, ulong inputHandle, ushort leftSpeed, ushort rightSpeed);

    [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SteamAPI_ISteamInput_TriggerVibrationExtended(IntPtr self, ulong inputHandle, ushort leftSpeed, ushort rightSpeed, ushort leftTriggerSpeed, ushort rightTriggerSpeed);

    [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool SteamAPI_ISteamInput_Shutdown(IntPtr self);

    [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SteamAPI_SteamController_v008();

    [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool SteamAPI_ISteamController_Init(IntPtr self);

    [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SteamAPI_ISteamController_RunFrame(IntPtr self);

    [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SteamAPI_ISteamController_GetConnectedControllers(IntPtr self, [Out] ulong[] handlesOut);

    [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SteamAPI_ISteamController_TriggerVibration(IntPtr self, ulong controllerHandle, ushort leftSpeed, ushort rightSpeed);

    [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool SteamAPI_ISteamController_Shutdown(IntPtr self);

    public static int Main(string[] args)
    {
        string defaultDllDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam",
            "steamapps",
            "common",
            "Stardew Valley");
        string dllDir = GetStringArg(args, "--steam-dll-dir", defaultDllDir);
        string logPath = GetStringArg(args, "--log", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SteamInputVibrationProbe.log"));
        int seconds = GetArg(args, "--seconds", 20);
        int pulseMs = GetArg(args, "--pulse-ms", 550);
        int gapMs = GetArg(args, "--gap-ms", 350);
        ushort low = (ushort)Math.Max(0, Math.Min(65535, GetArg(args, "--low", 60000)));
        ushort high = (ushort)Math.Max(0, Math.Min(65535, GetArg(args, "--high", 60000)));

        using (var log = new StreamWriter(logPath, true))
        {
            Log(log, "start seconds=" + seconds + " pulseMs=" + pulseMs + " gapMs=" + gapMs +
                " low=" + low + " high=" + high + " pid=" + System.Diagnostics.Process.GetCurrentProcess().Id);
            Log(log, "dllDir=" + dllDir + " setDllDirectory=" + SetDllDirectory(dllDir) +
                " err=" + Marshal.GetLastWin32Error());

            bool steamReady;
            try
            {
                steamReady = SteamAPI_Init();
            }
            catch (Exception ex)
            {
                Log(log, "SteamAPI_Init threw " + ex.GetType().Name + ": " + ex.Message);
                return 2;
            }

            Log(log, "SteamAPI_Init=" + steamReady);
            if (!steamReady)
            {
                return 3;
            }

            try
            {
                RunSteamInputProbe(log, seconds, pulseMs, gapMs, low, high);
                RunSteamControllerProbe(log, seconds, pulseMs, gapMs, low, high);
            }
            finally
            {
                SteamAPI_Shutdown();
                Log(log, "SteamAPI_Shutdown complete");
            }
        }

        return 0;
    }

    private static void RunSteamInputProbe(StreamWriter log, int seconds, int pulseMs, int gapMs, ushort low, ushort high)
    {
        IntPtr input = SteamAPI_SteamInput_v005();
        Log(log, "SteamInput_v005=0x" + input.ToInt64().ToString("x"));
        if (input == IntPtr.Zero)
        {
            return;
        }

        bool init = SteamAPI_ISteamInput_Init(input, true);
        Log(log, "ISteamInput.Init=" + init);

        DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
        int pulse = 0;
        while (DateTime.UtcNow < deadline)
        {
            SteamAPI_RunCallbacks();
            SteamAPI_ISteamInput_RunFrame(input, true);

            ulong[] handles = new ulong[MaxControllers];
            int count = SteamAPI_ISteamInput_GetConnectedControllers(input, handles);
            Log(log, "input handles count=" + count + " values=" + FormatHandles(handles, count));

            for (int i = 0; i < count && i < handles.Length; i++)
            {
                SteamAPI_ISteamInput_TriggerVibration(input, handles[i], low, high);
                SteamAPI_ISteamInput_TriggerVibrationExtended(input, handles[i], low, high, 0, 0);
                Log(log, "input rumble-on pulse=" + pulse + " handle=0x" + handles[i].ToString("x"));
            }

            Thread.Sleep(Math.Max(1, pulseMs));

            for (int i = 0; i < count && i < handles.Length; i++)
            {
                SteamAPI_ISteamInput_TriggerVibration(input, handles[i], 0, 0);
                SteamAPI_ISteamInput_TriggerVibrationExtended(input, handles[i], 0, 0, 0, 0);
                Log(log, "input rumble-off pulse=" + pulse + " handle=0x" + handles[i].ToString("x"));
            }

            pulse++;
            Thread.Sleep(Math.Max(1, gapMs));
        }

        Log(log, "ISteamInput.Shutdown=" + SteamAPI_ISteamInput_Shutdown(input));
    }

    private static void RunSteamControllerProbe(StreamWriter log, int seconds, int pulseMs, int gapMs, ushort low, ushort high)
    {
        IntPtr controller = SteamAPI_SteamController_v008();
        Log(log, "SteamController_v008=0x" + controller.ToInt64().ToString("x"));
        if (controller == IntPtr.Zero)
        {
            return;
        }

        bool init = SteamAPI_ISteamController_Init(controller);
        Log(log, "ISteamController.Init=" + init);

        DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
        int pulse = 0;
        while (DateTime.UtcNow < deadline)
        {
            SteamAPI_RunCallbacks();
            SteamAPI_ISteamController_RunFrame(controller);

            ulong[] handles = new ulong[MaxControllers];
            int count = SteamAPI_ISteamController_GetConnectedControllers(controller, handles);
            Log(log, "controller handles count=" + count + " values=" + FormatHandles(handles, count));

            for (int i = 0; i < count && i < handles.Length; i++)
            {
                SteamAPI_ISteamController_TriggerVibration(controller, handles[i], low, high);
                Log(log, "controller rumble-on pulse=" + pulse + " handle=0x" + handles[i].ToString("x"));
            }

            Thread.Sleep(Math.Max(1, pulseMs));

            for (int i = 0; i < count && i < handles.Length; i++)
            {
                SteamAPI_ISteamController_TriggerVibration(controller, handles[i], 0, 0);
                Log(log, "controller rumble-off pulse=" + pulse + " handle=0x" + handles[i].ToString("x"));
            }

            pulse++;
            Thread.Sleep(Math.Max(1, gapMs));
        }

        Log(log, "ISteamController.Shutdown=" + SteamAPI_ISteamController_Shutdown(controller));
    }

    private static string FormatHandles(ulong[] handles, int count)
    {
        int n = Math.Max(0, Math.Min(count, handles.Length));
        string[] parts = new string[n];
        for (int i = 0; i < n; i++)
        {
            parts[i] = "0x" + handles[i].ToString("x");
        }
        return string.Join(",", parts);
    }

    private static int GetArg(string[] args, string name, int defaultValue)
    {
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                int parsed;
                if (int.TryParse(args[i + 1], out parsed))
                {
                    return parsed;
                }
            }
        }
        return defaultValue;
    }

    private static string GetStringArg(string[] args, string name, string defaultValue)
    {
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return defaultValue;
    }

    private static void Log(StreamWriter log, string message)
    {
        log.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message);
        log.Flush();
    }
}

using System;
using HarmonyLib;
using Spine.Unity.AttachmentTools;

namespace CustomSpineLoader.SpineLoaderHelper;

public static class SpineMemory
{
    private static float _nextSampleAt;
    private static long _lastManaged;
    private static long _lastNative;
    private static long _lastGraphics;
    private static long _lastMonoHeap;
    private static long _lastWorkingSet;
    private static long _lastPrivate;
    private static bool _osProbeBroken;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx
    {
        public uint cb;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivateUsage;
    }

    [System.Runtime.InteropServices.DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, out ProcessMemoryCountersEx counters, uint size);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    private const long JumpThreshold = 256L * 1024L * 1024L;

    public static long PrivateBytes()
    {
        try
        {
            var counters = new ProcessMemoryCountersEx
            {
                cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf<ProcessMemoryCountersEx>()
            };
            if (GetProcessMemoryInfo(GetCurrentProcess(), out counters, counters.cb))
                return (long)counters.PrivateUsage.ToUInt64();
        }
        catch (Exception)
        {
        }
        return 0;
    }

    // Every phase is main-thread work inside Plugin.Awake, which BepInEx runs before the game draws
    // its first frame -- so milliseconds here are milliseconds of blank window. PhaseMilliseconds is
    // what the phases add up to; Awake reports its own total so the gap (config binds, folder scans,
    // editor hosts) is visible as one number instead of hiding.
    public static long PhaseMilliseconds { get; private set; }

    public static void Phase(string name, Action work)
    {
        var before = PrivateBytes();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            work();
        }
        finally
        {
            clock.Stop();
            PhaseMilliseconds += clock.ElapsedMilliseconds;

            var delta = PrivateBytes() - before;
            Plugin.Log.LogWarning($"STARTUP PHASE {name}: {clock.ElapsedMilliseconds}ms, {delta / 1048576}MB private.");
        }
    }

    public static void Watch()
    {
        if (UnityEngine.Time.unscaledTime < _nextSampleAt) return;
        _nextSampleAt = UnityEngine.Time.unscaledTime + 1f;

        var managed = GC.GetTotalMemory(false);
        var native = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        var graphics = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();

        var monoHeap = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
        long workingSet = 0;
        long privateBytes = 0;
        if (!_osProbeBroken)
        {
            try
            {
                var counters = new ProcessMemoryCountersEx
                {
                    cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf<ProcessMemoryCountersEx>()
                };
                if (GetProcessMemoryInfo(GetCurrentProcess(), out counters, counters.cb))
                {
                    workingSet = (long)counters.WorkingSetSize.ToUInt64();
                    privateBytes = (long)counters.PrivateUsage.ToUInt64();
                }
                else
                {
                    _osProbeBroken = true;
                    Plugin.Log.LogWarning("MEMORY watch: GetProcessMemoryInfo refused; process-level numbers unavailable.");
                }
            }
            catch (Exception e)
            {
                _osProbeBroken = true;
                Plugin.Log.LogWarning("MEMORY watch: OS probe failed (" + e.Message + "); process-level numbers unavailable.");
            }
        }

        if (_lastManaged > 0 &&
            (managed - _lastManaged > JumpThreshold ||
             native - _lastNative > JumpThreshold ||
             graphics - _lastGraphics > JumpThreshold ||
             monoHeap - _lastMonoHeap > JumpThreshold ||
             workingSet - _lastWorkingSet > JumpThreshold ||
             privateBytes - _lastPrivate > JumpThreshold))
        {
            Plugin.Log.LogWarning(
                $"MEMORY JUMP within 1s: workingSet {_lastWorkingSet / 1048576}MB -> {workingSet / 1048576}MB | " +
                $"private {_lastPrivate / 1048576}MB -> {privateBytes / 1048576}MB | " +
                $"monoHeap {_lastMonoHeap / 1048576}MB -> {monoHeap / 1048576}MB " +
                $"(used {managed / 1048576}MB) | " +
                $"native {_lastNative / 1048576}MB -> {native / 1048576}MB | " +
                $"graphics {_lastGraphics / 1048576}MB -> {graphics / 1048576}MB " +
                $"(reserved {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576}MB).");
        }

        _lastManaged = managed;
        _lastNative = native;
        _lastGraphics = graphics;
        _lastMonoHeap = monoHeap;
        _lastWorkingSet = workingSet;
        _lastPrivate = privateBytes;
    }

    public static void TrimRepackCaches(string reason)
    {
        try
        {
            AtlasUtilities.ClearCache();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Spine repack cache trim failed: " + e.Message);
        }

        try
        {
            var patches = typeof(COTL_API.CustomSkins.CustomSkinManager).Assembly
                .GetType("COTL_API.CustomSkins.CustomSkinPatches");
            var cache = patches != null
                ? Traverse.Create(patches).Field("CachedTextures")
                    .GetValue<System.Collections.IDictionary>()
                : null;

            if (cache != null && cache.Count > 0)
            {
                Plugin.Log.LogInfo($"Dropped {cache.Count} duplicated texture page(s) from the " +
                                   $"skin pipeline ({reason}); the next sweep frees them.");
                cache.Clear();
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("COTL_API texture cache trim failed: " + e.Message);
        }
    }
}

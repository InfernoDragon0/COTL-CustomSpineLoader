using System;
using HarmonyLib;
using Spine.Unity.AttachmentTools;

namespace CustomSpineLoader.SpineLoaderHelper;

// Two caches grow while skins are repacked, and nothing on our code path ever empties them:
// Spine's own AtlasUtilities keeps a CPU copy of every texture a repack read, and COTL_API's
// Graphics.CopyTexture patch keeps a converted duplicate of every page it touched, keyed by
// name - the API clears it only inside its own skin-building path, which we never take. With a
// few custom skins installed those copies are a large share of the mod's memory growth, so
// they are dropped once startup loading is done and again on every scene change. Both rebuild
// on demand; the cost of dropping them is a slower next repack, not a behaviour change.
public static class SpineMemory
{
    // A once-per-second sample, logged only when memory leaps - the neighbouring log lines are
    // then the suspect list for what allocated it. Split three ways because the fix differs:
    // managed = parses and arrays, native = textures and meshes, graphics = the driver's copy.
    private static float _nextSampleAt;
    private static long _lastManaged;
    private static long _lastNative;
    private static long _lastGraphics;
    private static long _lastMonoHeap;
    private static long _lastWorkingSet;
    private static long _lastPrivate;
    private static bool _osProbeBroken;

    // Both Environment.WorkingSet and System.Diagnostics.Process are stubs in this Mono
    // profile - they answer 0 without erroring - so the numbers Task Manager shows have to
    // come from the OS itself.
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

    // The process's committed private bytes, straight from the OS - the probe the watcher
    // uses, exposed so startup can attribute its own phases.
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

    // Runs one startup phase and says what it cost the process - the attribution the watcher
    // cannot give, because at boot everything happens inside one of its samples.
    public static void Phase(string name, Action work)
    {
        var before = PrivateBytes();
        try
        {
            work();
        }
        finally
        {
            var delta = PrivateBytes() - before;
            if (Math.Abs(delta) > 10L * 1024L * 1024L)
                Plugin.Log.LogWarning($"STARTUP PHASE {name}: {delta / 1048576}MB private.");
        }
    }

    public static void Watch()
    {
        if (UnityEngine.Time.unscaledTime < _nextSampleAt) return;
        _nextSampleAt = UnityEngine.Time.unscaledTime + 1f;

        var managed = GC.GetTotalMemory(false);
        var native = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        var graphics = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();

        // The two the first version missed. The mono heap SIZE is the GC's reserved arena -
        // it grows in huge steps under allocation pressure and never shrinks, invisible to
        // GetTotalMemory (used bytes). The working set is the process number Task Manager
        // shows, so a jump there always registers whatever its source. Environment.WorkingSet
        // is a stub under Mono (always 0), so the process object is asked directly.
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

        // Internal to the API, so it is reached by name. A build of the API without the field
        // simply logs once and moves on.
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

using System;
using System.IO;
using System.Threading;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace CustomSpineLoader.APIHelper;

// Pulls a cutscene's soundtrack out of its video, using the codecs Windows already has.
//
// The chain of constraints that leads here is worth stating, because none of it is obvious:
// Unity's own audio engine is compiled out of this build (sample rate 0, zero voices), so nothing
// routed through an AudioSource can ever be heard; the game's sound is FMOD, which is alive and
// well but does not decode AAC on Windows; and AAC is what sits inside an .mp4. So the audio has
// to arrive as something FMOD can read.
//
// Media Foundation - the decoder behind every video thumbnail and preview in Explorer - can read
// that AAC track. Decoding it once into a .wav next to the video turns "a format FMOD cannot
// open" into "a file FMOD opens without thinking about it", and the result is cached, so it
// happens on the first run and never again.
public static class CutsceneAudioExtractor
{
    // One decode thread is spawned per video, and they all read and write these - so they sit
    // behind one gate. Startup() in particular must not run twice concurrently.
    private static readonly object Gate = new();
    private static bool _started;
    private static bool _unavailable;

    // 16-bit PCM at 44.1k: the least surprising thing to hand a sound engine, and small enough
    // that a cutscene's cache is measured in tens of megabytes rather than hundreds.
    private const int SampleRate = 44100;
    private const int BitsPerSample = 16;

    public static bool Available => !_unavailable;

    // Runs on a thread of its own - a few minutes of audio takes a moment to decode, and none of
    // it needs to happen before the game finishes loading. `onFinished` reports the outcome, not
    // the file: the caller looks for the file where it always looks.
    public static void ExtractInBackground(string videoPath, string wavPath, Action<bool, string> onFinished)
    {
        var thread = new Thread(() =>
        {
            var ok = TryExtract(videoPath, wavPath, out var error);
            onFinished?.Invoke(ok, error);
        })
        {
            IsBackground = true,
            Name = "CultTweaker cutscene audio"
        };

        try
        {
            // Media Foundation is COM, and COM wants to know which apartment it is in. Mono
            // ignores this on some runtimes, which is harmless - the call is what matters.
            thread.SetApartmentState(ApartmentState.MTA);
        }
        catch (Exception)
        {
            // Already started, or unsupported. Neither stops the decode from being attempted.
        }

        thread.Start();
    }

    public static bool TryExtract(string videoPath, string wavPath, out string error)
    {
        error = null;

        lock (Gate)
        {
            if (_unavailable)
            {
                error = "Media Foundation is not usable in this runtime.";
                return false;
            }

            if (!_started)
            {
                try
                {
                    MediaFoundationApi.Startup();
                    _started = true;
                }
                catch (Exception e)
                {
                    _unavailable = true;
                    error = e.GetType().Name + ": " + e.Message;
                    return false;
                }
            }
        }

        // Written under a temporary name and moved into place, so a decode that dies halfway
        // through never leaves a half-written file where the player will find and try to play it.
        var partial = wavPath + ".partial";

        try
        {
            using (var reader = new MediaFoundationReader(videoPath))
            {
                if (reader.WaveFormat == null)
                {
                    error = "the video has no audio track";
                    return false;
                }

                var channels = Math.Min(2, Math.Max(1, reader.WaveFormat.Channels));
                var target = new WaveFormat(SampleRate, BitsPerSample, channels);

                using var resampler = new MediaFoundationResampler(reader, target) { ResamplerQuality = 60 };
                WaveFileWriter.CreateWaveFile(partial, resampler);
            }

            if (File.Exists(wavPath)) File.Delete(wavPath);
            File.Move(partial, wavPath);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;

            // A runtime that cannot do the COM interop fails on the first attempt and would fail
            // on every one after it, so it is not tried again this session.
            // Media Foundation is a Windows API: on macOS and Linux the native libraries simply
            // are not there, and the failure is one of these rather than a decode error. Once is
            // enough to know it will never work here.
            if (e is TypeLoadException or EntryPointNotFoundException or DllNotFoundException
                or NotSupportedException or PlatformNotSupportedException
                or TypeInitializationException or System.IO.FileNotFoundException)
            {
                lock (Gate) _unavailable = true;
                error = e.GetType().Name + ": " + e.Message;
            }

            try
            {
                if (File.Exists(partial)) File.Delete(partial);
            }
            catch (Exception)
            {
                // Nothing more to do about it; the name is only reused by the next attempt.
            }

            return false;
        }
    }
}

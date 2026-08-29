using System;
using System.IO;
using System.Threading;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace CustomSpineLoader.APIHelper;

public static class CutsceneAudioExtractor
{
    private static readonly object Gate = new();
    private static bool _started;
    private static bool _unavailable;

    private const int SampleRate = 44100;
    private const int BitsPerSample = 16;

    public static bool Available => !_unavailable;

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
            thread.SetApartmentState(ApartmentState.MTA);
        }
        catch (Exception)
        {
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
            }

            return false;
        }
    }
}

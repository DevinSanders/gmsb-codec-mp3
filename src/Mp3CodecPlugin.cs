using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NLayer;
using SoundBoard.PluginApi;

namespace Mp3CodecPlugin;

/// <summary>
/// <see cref="IAudioCodecPlugin"/> that adds MP3 playback (<c>.mp3</c>) to
/// Game Master Sound Board via the
/// <a href="https://github.com/naudio/NLayer">NLayer</a> managed decoder.
/// NLayer is pure-managed (no native libraries), so the plugin folder only
/// needs this DLL and <c>NLayer.dll</c> alongside.
///
/// <para><b>Inter-plugin dispatch.</b> This codec implements the
/// <see cref="IAudioCodecPlugin.CreateStream(System.IO.Stream, string)"/>
/// overload, so transport plugins (e.g. <c>codec.webstream</c>) can hand
/// a pre-opened HTTP/ICY <see cref="System.IO.Stream"/> here for decode
/// without bundling NLayer themselves. The MIME type <c>"audio/mpeg"</c>
/// is declared in <see cref="SupportedContentTypes"/> so the registry's
/// <c>GetByContentType("audio/mpeg")</c> routes here.</para>
/// </summary>
public sealed class Mp3CodecPlugin : IAudioCodecPlugin
{
    public string Id => "codec.mp3";
    public string Name => "MP3 Codec";
    public string Description => "Adds .mp3 playback support via the NLayer managed MPEG decoder.";
    // Read from the assembly's AssemblyInformationalVersion — set by the
    // release workflow's `-p:Version=<tag>` and by the csproj's <Version>
    // for local builds. Single source of truth; no hand-maintained literal
    // to drift from plugin.json.
    public string Version => PluginVersion.OfAssembly(typeof(Mp3CodecPlugin));
    public string Author => "Devin Sanders";

    public IEnumerable<string> SupportedPatterns => new[] { ".mp3" };

    // MIME types this codec advertises to the registry. Browsers /
    // Icecast / Shoutcast emit "audio/mpeg" for MP3 streams;
    // "audio/mp3" is a non-standard variant some servers use anyway.
    public IEnumerable<string> SupportedContentTypes => new[] { "audio/mpeg", "audio/mp3" };

    public bool SupportsStreamInput => true;

    public WaveStream CreateStream(string source) => new NLayerMp3WaveStream(source);

    /// <summary>Decode an already-open <see cref="Stream"/> of MP3 bytes.
    /// Ownership of <paramref name="source"/> transfers to the returned
    /// <see cref="WaveStream"/> — its <c>Dispose</c> closes the input
    /// Stream. <paramref name="formatHint"/> is advisory; NLayer
    /// validates the frame headers itself and throws on malformed
    /// input.</summary>
    public WaveStream CreateStream(Stream source, string formatHint)
        => new NLayerMp3WaveStream(source);

    public void Initialize(IPluginContext context) { }
    public void Shutdown() { }
}

/// <summary>
/// Bridges <see cref="MpegFile"/> (which produces float samples) to
/// <see cref="WaveStream"/> (which <see cref="IAudioCodecPlugin.CreateStream"/>
/// must return — the host wraps it in <c>GenericSeekableSampleProvider</c>).
/// Length and Position are computed from <c>MpegFile.Duration</c> /
/// <c>Time</c> against the wave format's average bytes-per-second so
/// the scrub slider works.
///
/// <para>Two constructors: one takes a file path (host's normal flow);
/// the other takes a <see cref="Stream"/> handed in by a transport
/// plugin. The Stream-mode constructor takes ownership and disposes
/// the input stream alongside <see cref="MpegFile"/>.</para>
/// </summary>
internal sealed class NLayerMp3WaveStream : WaveStream
{
    private readonly MpegFile _mpeg;
    private readonly Stream? _owningStream;     // non-null when constructed from a Stream — disposed alongside _mpeg.
    private readonly float[] _scratch = new float[16384];

    public override WaveFormat WaveFormat { get; }

    /// <summary>File-path constructor. NLayer opens + owns the file.</summary>
    public NLayerMp3WaveStream(string path)
    {
        _mpeg = new MpegFile(path);
        _owningStream = null;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_mpeg.SampleRate, _mpeg.Channels);
    }

    /// <summary>Stream constructor for inter-plugin dispatch. Takes
    /// ownership of <paramref name="source"/>; <see cref="Dispose(bool)"/>
    /// disposes both <see cref="MpegFile"/> and the input stream.</summary>
    public NLayerMp3WaveStream(Stream source)
    {
        _mpeg = new MpegFile(source);
        _owningStream = source;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_mpeg.SampleRate, _mpeg.Channels);
    }

    /// <summary>NAudio's <c>WaveStream</c> defaults <c>CanSeek</c> to
    /// <c>true</c>. For the Stream-input path that's wrong — the
    /// codec's seekability is bounded by whether the input transport
    /// itself supports seeks. Delegate to the input Stream when we
    /// have one; file mode is always seekable.</summary>
    public override bool CanSeek => _owningStream?.CanSeek ?? true;

    public override long Length =>
        (long)(_mpeg.Duration.TotalSeconds * WaveFormat.AverageBytesPerSecond);

    public override long Position
    {
        get => (long)(_mpeg.Time.TotalSeconds * WaveFormat.AverageBytesPerSecond);
        set
        {
            // Refuse seeks when the underlying transport can't honour
            // them — defends against any upstream code that forgets to
            // check CanSeek before scrubbing.
            if (!CanSeek) return;
            var seconds = value / (double)WaveFormat.AverageBytesPerSecond;
            if (seconds < 0) seconds = 0;
            _mpeg.Time = TimeSpan.FromSeconds(seconds);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        // Host downstream expects IEEE float frames at the sample rate /
        // channels we advertised. NLayer reads floats directly; copy
        // them into the byte buffer the WaveStream contract requires.
        int floatsRequested = Math.Min(count / sizeof(float), _scratch.Length);
        if (floatsRequested <= 0) return 0;

        int floatsRead = _mpeg.ReadSamples(_scratch, 0, floatsRequested);
        int bytesRead = floatsRead * sizeof(float);
        Buffer.BlockCopy(_scratch, 0, buffer, offset, bytesRead);
        return bytesRead;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _mpeg.Dispose();
            // Dispose the input stream we took ownership of. The `?.`
            // short-circuits in file mode where _owningStream is null
            // (NLayer owns the file). The try/catch swallows the
            // double-dispose some network-backed streams throw on.
            try { _owningStream?.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }
}

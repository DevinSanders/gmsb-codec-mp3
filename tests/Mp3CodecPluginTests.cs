using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using NAudio.Wave;
using Xunit;

namespace Mp3CodecPlugin.Tests;

public class Mp3CodecPluginTests
{
    // 0.5 s, 440 Hz, stereo, 44.1 kHz. See tests/fixtures/README.txt.
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "fixtures", "sine-440hz-0.5s.mp3");

    private const int ExpectedChannels = 2;

    // ── Declarations ──────────────────────────────────────────────────

    [Fact]
    public void SupportedPatterns_claims_mp3()
    {
        new Mp3CodecPlugin().SupportedPatterns.Should().Contain(".mp3");
    }

    [Fact]
    public void SupportedContentTypes_advertises_mp3_mime_types()
    {
        new Mp3CodecPlugin().SupportedContentTypes
            .Should().Contain(new[] { "audio/mpeg", "audio/mp3" });
    }

    [Fact]
    public void SupportsStreamInput_is_true()
    {
        new Mp3CodecPlugin().SupportsStreamInput.Should().BeTrue();
    }

    [Fact]
    public void Version_is_resolved_from_assembly_not_empty()
    {
        new Mp3CodecPlugin().Version.Should().NotBeNullOrWhiteSpace();
    }

    // ── Decode a real file ────────────────────────────────────────────

    [Fact]
    public void CreateStream_path_decodes_to_seekable_ieee_float()
    {
        using var stream = new Mp3CodecPlugin().CreateStream(FixturePath);

        stream.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
        stream.WaveFormat.Channels.Should().Be(ExpectedChannels);
        stream.CanSeek.Should().BeTrue("a local file is always seekable");

        // Total length should be plausible for a ~0.5 s clip.
        var seconds = stream.Length / (double)stream.WaveFormat.AverageBytesPerSecond;
        seconds.Should().BeInRange(0.4, 0.7);
    }

    [Fact]
    public void CreateStream_path_Read_returns_audio()
    {
        using var stream = new Mp3CodecPlugin().CreateStream(FixturePath);

        var buffer = new byte[16384];
        long total = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            total += read;

        total.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CreateStream_path_seek_round_trips()
    {
        using var stream = new Mp3CodecPlugin().CreateStream(FixturePath);

        // Seek to ~0.25 s in. NLayer snaps to the nearest frame, so allow a
        // tolerance of a few frames' worth of bytes rather than exact equality.
        long target = stream.WaveFormat.AverageBytesPerSecond / 4;
        stream.Position = target;

        var toleranceBytes = stream.WaveFormat.AverageBytesPerSecond / 10; // 0.1 s
        stream.Position.Should().BeInRange(target - toleranceBytes, target + toleranceBytes);
    }

    // ── Stream overload + ownership ───────────────────────────────────

    [Fact]
    public void CreateStream_stream_decodes_same_bytes()
    {
        var bytes = File.ReadAllBytes(FixturePath);
        using var input = new MemoryStream(bytes);

        using var stream = new Mp3CodecPlugin().CreateStream(input, "audio/mpeg");

        stream.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
        stream.WaveFormat.Channels.Should().Be(ExpectedChannels);

        var buffer = new byte[16384];
        stream.Read(buffer, 0, buffer.Length).Should().BeGreaterThan(0);
    }

    [Fact]
    public void CreateStream_stream_takes_ownership_and_disposes_input()
    {
        var bytes = File.ReadAllBytes(FixturePath);
        var input = new DisposeTrackingStream(bytes);

        var stream = new Mp3CodecPlugin().CreateStream(input, "audio/mpeg");
        input.Disposed.Should().BeFalse("ownership transferred but nothing disposed yet");

        stream.Dispose();
        input.Disposed.Should().BeTrue("disposing the WaveStream must dispose the owned input stream");
    }

    [Fact]
    public void CreateStream_stream_propagates_non_seekable_input()
    {
        var bytes = File.ReadAllBytes(FixturePath);
        // A forward-only stream reports CanSeek = false, mimicking a live
        // HTTP/ICY transport. The codec must surface that to the host.
        using var input = new ForwardOnlyStream(bytes);

        using var stream = new Mp3CodecPlugin().CreateStream(input, "audio/mpeg");

        stream.CanSeek.Should().BeFalse();

        // Position.set must be a no-op when the transport can't seek.
        stream.Invoking(s => s.Position = 1000).Should().NotThrow();
    }

    // ── Test doubles ──────────────────────────────────────────────────

    /// <summary>MemoryStream that records whether it has been disposed.</summary>
    private sealed class DisposeTrackingStream : MemoryStream
    {
        public bool Disposed { get; private set; }

        public DisposeTrackingStream(byte[] buffer) : base(buffer, writable: false) { }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>Read-only, non-seekable stream over a byte buffer — stands
    /// in for a live network transport.</summary>
    private sealed class ForwardOnlyStream : Stream
    {
        private readonly MemoryStream _inner;
        public ForwardOnlyStream(byte[] buffer) => _inner = new MemoryStream(buffer, writable: false);

        public override bool CanSeek => false;
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

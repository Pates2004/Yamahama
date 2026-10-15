using System.Reflection;
using YamahaRemote.Core;
using Xunit;

namespace YamahaRemote.Core.Tests;

public sealed class PacketDecoderTests
{
    [Theory]
    [InlineData("CCAA0100FF", "00")]
    [InlineData(ProtocolSamples.PowerOffFrame, "40787F")]
    [InlineData(ProtocolSamples.TvFrame, "4078DF")]
    [InlineData(ProtocolSamples.IdentityFrame, ProtocolSamples.IdentityPayload)]
    [InlineData(ProtocolSamples.StatusFrame, ProtocolSamples.StatusPayload)]
    public void EveryFragmentBoundaryPreservesThePacketAndRejectsIncompletePrefixes(string frameHex, string payloadHex)
    {
        byte[] frame = Convert.FromHexString(frameHex);
        byte[] expected = Convert.FromHexString(payloadHex);

        for (int boundary = 0; boundary <= frame.Length; boundary++)
        {
            PacketDecoder decoder = new();
            IReadOnlyList<byte[]> first = decoder.Append(frame.AsSpan(0, boundary));
            if (boundary < frame.Length)
                Assert.Empty(first);
            List<byte[]> packets = [.. first, .. decoder.Append(frame.AsSpan(boundary))];

            Assert.Equal(expected, Assert.Single(packets));
            Assert.Empty(decoder.Append([]));
        }
    }

    [Fact]
    public void MultiplePacketsAreDecodedInOrderForEveryUniformFragmentSize()
    {
        byte[] stream = Convert.FromHexString(
            ProtocolSamples.IdentityFrame + ProtocolSamples.StatusFrame +
            ProtocolSamples.PowerOffFrame + ProtocolSamples.TvFrame);
        string[] expected = [ProtocolSamples.IdentityPayload, ProtocolSamples.StatusPayload, "40787F", "4078DF"];

        for (int size = 1; size <= stream.Length + 1; size++)
        {
            PacketDecoder decoder = new();
            List<byte[]> packets = [];
            for (int offset = 0; offset < stream.Length; offset += size)
                packets.AddRange(decoder.Append(stream.AsSpan(offset, Math.Min(size, stream.Length - offset))));

            Assert.Equal(expected, packets.Select(packet => Convert.ToHexString(packet)));
            Assert.Empty(decoder.Append([]));
        }
    }

    [Fact]
    public void NonuniformFragmentationPreservesPacketsAcrossNoiseAndHeaders()
    {
        byte[] stream = Convert.FromHexString(
            "00FFCC" + ProtocolSamples.IdentityFrame + "00FF" +
            ProtocolSamples.StatusFrame + "CCCC" + ProtocolSamples.TvFrame);
        string[] expected = [ProtocolSamples.IdentityPayload, ProtocolSamples.StatusPayload, "4078DF"];

        for (int seed = 0; seed < 100; seed++)
        {
            Random random = new(seed);
            PacketDecoder decoder = new();
            List<byte[]> packets = [];
            int offset = 0;
            while (offset < stream.Length)
            {
                int length = Math.Min(random.Next(1, 18), stream.Length - offset);
                packets.AddRange(decoder.Append(stream.AsSpan(offset, length)));
                offset += length;
            }

            Assert.Equal(expected, packets.Select(packet => Convert.ToHexString(packet)));
        }
    }

    [Theory]
    [InlineData("00")]
    [InlineData("00FFCC")]
    [InlineData("CCCCCCCC")]
    [InlineData("CCAACC")]
    [InlineData("CCAA00")]
    [InlineData("CCAA7D")]
    [InlineData("CCAAFF")]
    [InlineData("CCAA0340787FC7")]
    [InlineData("CCAA010001")]
    public void NoiseInvalidLengthsAndBadChecksumsResynchronizeToTheNextPacket(string noise)
    {
        PacketDecoder decoder = new();
        byte[] stream = Convert.FromHexString(noise + ProtocolSamples.TvFrame);

        Assert.Equal("4078DF", Convert.ToHexString(Assert.Single(decoder.Append(stream))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(125)]
    [InlineData(126)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(255)]
    public void InvalidLengthHeadersDoNotProducePackets(int length)
    {
        PacketDecoder decoder = new();
        byte[] invalid = [0xCC, 0xAA, (byte)length, .. new byte[260]];

        Assert.Empty(decoder.Append(invalid));
        Assert.Equal("40787F", Convert.ToHexString(Assert.Single(
            decoder.Append(Convert.FromHexString(ProtocolSamples.PowerOffFrame)))));
    }

    [Theory]
    [InlineData(ProtocolSamples.PowerOffFrame)]
    [InlineData(ProtocolSamples.IdentityFrame)]
    [InlineData(ProtocolSamples.StatusFrame)]
    public void EverySingleBitCorruptionIsRejected(string frameHex)
    {
        byte[] original = Convert.FromHexString(frameHex);

        for (int offset = 0; offset < original.Length; offset++)
        {
            for (int bit = 0; bit < 8; bit++)
            {
                byte[] damaged = [.. original];
                damaged[offset] ^= (byte)(1 << bit);
                Assert.Empty(new PacketDecoder().Append(damaged));
            }
        }
    }

    [Fact]
    public void BadStatusChecksumNeverReachesTheStateParser()
    {
        byte[] damaged = Convert.FromHexString("CCAA0E05000107001910000000000A0001B0");
        PacketDecoder decoder = new();

        Assert.Empty(decoder.Append(damaged));
        byte[] payload = Assert.Single(decoder.Append(Convert.FromHexString(ProtocolSamples.StatusFrame)));
        Assert.Equal(25, ProtocolSamples.ParseState(payload).Volume);
    }

    [Fact]
    public void ValidOuterPacketOwnsTheEmbeddedHeaderAndPacket()
    {
        byte[] frame = Convert.FromHexString("CCAA05CCAA0100FF85");
        PacketDecoder decoder = new();

        Assert.Empty(decoder.Append(frame.AsSpan(0, 8)));
        Assert.Empty(decoder.Append([]));
        Assert.Equal("CCAA0100FF", Convert.ToHexString(Assert.Single(decoder.Append(frame.AsSpan(8)))));
        Assert.Empty(decoder.Append([]));
    }

    [Fact]
    [Trait("Regression", "DecoderResynchronization")]
    public void CorruptOuterPacketMustImmediatelyReleaseTheCompleteNestedPacket()
    {
        PacketDecoder decoder = new();

        IReadOnlyList<byte[]> packets = decoder.Append(Convert.FromHexString("CCAA04CCAA0100FF"));

        Assert.Equal("00", Convert.ToHexString(Assert.Single(packets)));
        Assert.Empty(decoder.Append([]));
    }

    [Fact]
    [Trait("Regression", "DecoderResynchronization")]
    public void ResynchronizationMustDrainAllCompleteNestedPacketsWithoutAnotherByte()
    {
        PacketDecoder decoder = new();

        IReadOnlyList<byte[]> packets = decoder.Append(Convert.FromHexString("CCAA09CCAA0100FFCCAA0101FE00"));

        Assert.Equal(new[] { "00", "01" }, packets.Select(packet => Convert.ToHexString(packet)));
    }

    [Fact]
    public void ResetDiscardsAnyPartialPacketAndAllowsAnotherConnection()
    {
        byte[] frame = Convert.FromHexString(ProtocolSamples.StatusFrame);

        for (int length = 0; length < frame.Length; length++)
        {
            PacketDecoder decoder = new();
            Assert.Empty(decoder.Append(frame.AsSpan(0, length)));
            decoder.Reset();
            decoder.Reset();

            Assert.Empty(decoder.Append([]));
            Assert.Equal("4078DF", Convert.ToHexString(Assert.Single(
                decoder.Append(Convert.FromHexString(ProtocolSamples.TvFrame)))));
        }
    }

    [Fact]
    public void FragmentsAndReturnedPacketsHaveIndependentOwnership()
    {
        byte[] prefix = Convert.FromHexString("CCAA0340");
        PacketDecoder decoder = new();
        Assert.Empty(decoder.Append(prefix));
        Array.Fill(prefix, (byte)0);
        byte[] first = Assert.Single(decoder.Append(Convert.FromHexString("787FC6")));
        Assert.Equal("40787F", Convert.ToHexString(first));
        first[0] = 0;
        byte[] second = Assert.Single(decoder.Append(Convert.FromHexString(ProtocolSamples.PowerOffFrame)));

        Assert.NotSame(first, second);
        Assert.Equal("40787F", Convert.ToHexString(second));
    }

    [Theory]
    [InlineData("00")]
    [InlineData("CC")]
    [InlineData("CCAA00")]
    [InlineData("CCAA7D")]
    [InlineData("CCAA7C")]
    public void LongGarbageKeepsRetainedBufferAndAllocatedCapacityBounded(string patternHex)
    {
        byte[] pattern = Convert.FromHexString(patternHex);
        byte[] fragment = new byte[384];
        for (int offset = 0; offset < fragment.Length; offset++)
            fragment[offset] = pattern[offset % pattern.Length];
        PacketDecoder decoder = new();
        FieldInfo field = Assert.IsType<FieldInfo>(
            typeof(PacketDecoder).GetField("buffer", BindingFlags.Instance | BindingFlags.NonPublic), exactMatch: false);

        for (int iteration = 0; iteration < 4096; iteration++)
        {
            Assert.Empty(decoder.Append(fragment));
            List<byte> retained = Assert.IsType<List<byte>>(field.GetValue(decoder));
            Assert.InRange(retained.Count, 0, 127);
            Assert.InRange(retained.Capacity, 0, 256);
        }

        decoder.Reset();
        Assert.Equal("4078DF", Convert.ToHexString(Assert.Single(
            decoder.Append(Convert.FromHexString(ProtocolSamples.TvFrame)))));
    }

    [Fact]
    public void SingleLargeGarbageFragmentDoesNotAccumulateInput()
    {
        PacketDecoder decoder = new();

        Assert.Empty(decoder.Append(new byte[2 * 1024 * 1024]));
        Assert.Equal("40787F", Convert.ToHexString(Assert.Single(
            decoder.Append(Convert.FromHexString(ProtocolSamples.PowerOffFrame)))));
    }
}

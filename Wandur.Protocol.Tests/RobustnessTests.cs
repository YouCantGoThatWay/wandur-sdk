using System.Text;
using Wandur.Core.Protocol;

namespace Wandur.Protocol.Tests;

public class RobustnessTests
{
    private static readonly byte[] Interesting = [255, 255, 255, 250, 240, 249, 239, 251, 252, 253, 254, 1, 2, 24, 25, 31, 69, 70, 201, 0, 13, 10, 0xC3, 0xA9];

    private static TelnetParser Everything() => new(new TelnetParserOptions { AcceptMssp = true });

    private static void AssertWellFormed(TelnetPacket packet)
    {
        var previous = 0;
        foreach (var mark in packet.PromptMarks)
        {
            Assert.InRange(mark.Offset, previous, packet.Text.Length);
            previous = mark.Offset;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(2026)]
    public void FeedNeverThrowsOnSeededRandomInput(int seed)
    {
        var random = new Random(seed);
        var parser = Everything();
        for (var round = 0; round < 2_000; round++)
        {
            var chunk = new byte[random.Next(0, 64)];
            for (var i = 0; i < chunk.Length; i++)
                chunk[i] = random.Next(3) == 0 ? (byte)random.Next(256) : Interesting[random.Next(Interesting.Length)];
            AssertWellFormed(parser.Feed(chunk));
            if (random.Next(50) == 0) parser.UpdateWindowSize(random.Next(65536), random.Next(65536));
            if (random.Next(50) == 0) parser.RequestMssp();
            if (random.Next(50) == 0) parser.SetLocalPrivateInput(random.Next(2) == 0);
        }
    }

    [Fact]
    public void SubnegotiationWithoutEndSwallowsTextUntilItEnds()
    {
        var parser = Everything();
        parser.Feed([255, 251, 70]);
        Assert.Empty(parser.Feed([255, 250, 70, 1, .. "NAME"u8, 2, .. "x"u8]).Text);
        Assert.Empty(parser.Feed("more text that is still inside"u8).Text);
        var packet = parser.Feed([255, 240, .. "after"u8]);
        Assert.Equal("after", Encoding.ASCII.GetString(packet.Text));
        Assert.Single(packet.Mssp);
    }

    [Fact]
    public void IacAtTheEndOfEveryBufferIsCarriedOver()
    {
        var parser = Everything();
        byte[] stream = [.. "a"u8, 255, 255, .. "b"u8, 255, 249, .. "c"u8, 255, 251, 3, .. "d"u8, 255, 250, 201, 255, 255, 255, 240, .. "e"u8];
        var text = new List<byte>();
        var marks = 0;
        var start = 0;
        // Cut the stream right after every IAC so each buffer ends on one.
        for (var i = 0; i < stream.Length; i++)
        {
            if (stream[i] != 255 && i != stream.Length - 1) continue;
            var packet = parser.Feed(stream.AsSpan(start, i - start + 1));
            AssertWellFormed(packet);
            text.AddRange(packet.Text);
            marks += packet.PromptMarks.Count;
            start = i + 1;
        }
        Assert.Equal(new byte[] { (byte)'a', 255, (byte)'b', (byte)'c', (byte)'d', (byte)'e' }, text);
        Assert.Equal(1, marks);
    }

    [Fact]
    public void DoubledIacInsideSubnegotiationIsOneDataByte()
    {
        var parser = Everything();
        parser.Feed([255, 251, 201]);
        var packet = parser.Feed([255, 250, 201, 65, 255, 255, 66, 255, 240]);
        Assert.Equal(new byte[] { 65, 255, 66 }, Assert.Single(packet.DataMessages).Payload);
    }

    [Fact]
    public void ZeroLengthSubnegotiationIsIgnored()
    {
        var packet = Everything().Feed([255, 250, 255, 240, .. "ok"u8]);
        Assert.Equal("ok", Encoding.ASCII.GetString(packet.Text));
        Assert.Empty(packet.Reply);
        Assert.Empty(packet.DataMessages);
        Assert.Empty(packet.Mssp);
    }

    [Fact]
    public void WontEorAfterRefusingItGetsNoReply()
    {
        var parser = new TelnetParser(new TelnetParserOptions { AcceptEndOfRecord = false });
        Assert.Equal(new byte[] { 255, 254, 25 }, parser.Feed([255, 251, 25]).Reply);
        Assert.Empty(parser.Feed([255, 252, 25]).Reply);
    }
}

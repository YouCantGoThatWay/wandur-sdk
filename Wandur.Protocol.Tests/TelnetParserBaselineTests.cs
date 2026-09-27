using System.Text;
using Wandur.Core.Protocol;

namespace Wandur.Protocol.Tests;

/// <summary>Behaviour the parser had before the cheap-wins branch. These must keep passing unchanged.</summary>
public class TelnetParserBaselineTests
{
    [Fact]
    public void NegotiationsSplitAtEveryByteDoNotLeakIntoText()
    {
        var parser = new TelnetParser();
        var text = new List<byte>();
        var replies = new List<byte>();
        foreach (byte b in new byte[] { 72, 255, 251, 3, 105, 255, 251, 86, 33 })
        {
            var packet = parser.Feed([b]);
            text.AddRange(packet.Text);
            replies.AddRange(packet.Reply);
        }
        Assert.Equal("Hi!", Encoding.UTF8.GetString(text.ToArray()));
        Assert.Equal(new byte[] { 255, 253, 3, 255, 254, 86 }, replies);
    }

    [Fact]
    public void EscapedIacBecomesOneLiteralByte()
    {
        Assert.Equal(new byte[] { 65, 255, 66 }, new TelnetParser().Feed([65, 255, 255, 66]).Text);
    }

    [Fact]
    public void FirstTerminalTypeReplyIsTheClientIdentity()
    {
        var parser = new TelnetParser();
        Assert.Equal(new byte[] { 255, 251, 24 }, parser.Feed([255, 253, 24]).Reply);
        var reply = parser.Feed([255, 250, 24, 1, 255, 240]).Reply;
        Assert.Equal(new byte[] { 255, 250, 24, 0 }.Concat(Encoding.ASCII.GetBytes(ClientIdentity.TerminalType)).Concat(new byte[] { 255, 240 }), reply);
    }

    [Fact]
    public void DoNawsSendsTheDefaultSize()
    {
        var reply = new TelnetParser().Feed([255, 253, 31]).Reply;
        Assert.Equal(new byte[] { 255, 251, 31, 255, 250, 31, 0, 100, 0, 40, 255, 240 }, reply);
    }

    [Fact]
    public void GmcpIsEmittedSeparatelyFromVisibleText()
    {
        var parser = new TelnetParser();
        parser.Feed([255, 251, 201]);
        var input = new byte[] { 255, 250, 201 }.Concat(Encoding.UTF8.GetBytes("Room.Info {\"num\":42}")).Concat(new byte[] { 255, 240, 62 }).ToArray();
        var packet = parser.Feed(input);
        Assert.Equal(">", Encoding.UTF8.GetString(packet.Text));
        Assert.Equal("Room.Info {\"num\":42}", Assert.Single(packet.Gmcp));
    }

    [Fact]
    public void MsspIsRefusedByDefault()
    {
        Assert.Equal(new byte[] { 255, 254, 70 }, new TelnetParser().Feed([255, 251, 70]).Reply);
    }

    [Fact]
    public void OversizedSubnegotiationIsDiscardedUntilItsEnd()
    {
        var parser = new TelnetParser();
        parser.Feed([255, 250, 201]);
        Assert.Empty(parser.Feed(Enumerable.Repeat((byte)65, 100_000).ToArray()).Text);
        var packet = parser.Feed([255, 240, 79, 75]);
        Assert.Empty(packet.Gmcp);
        Assert.Equal("OK", Encoding.UTF8.GetString(packet.Text));
    }

    [Fact]
    public void RepeatedOffersDoNotCreateNegotiationLoops()
    {
        var parser = new TelnetParser();
        parser.Feed([255, 251, 3]);
        Assert.Empty(parser.Feed([255, 251, 3]).Reply);
    }
}

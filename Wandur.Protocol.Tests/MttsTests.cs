using System.Text;
using Wandur.Core.Protocol;

namespace Wandur.Protocol.Tests;

public class MttsTests
{
    private static readonly byte[] Send = [255, 250, 24, 1, 255, 240];

    private static string? Answer(byte[] reply)
    {
        // IAC SB TTYPE IS <ascii> IAC SE
        if (reply.Length < 6 || reply[0] != 255 || reply[1] != 250 || reply[2] != 24 || reply[3] != 0) return null;
        return Encoding.ASCII.GetString(reply, 4, reply.Length - 6);
    }

    private static TelnetParser Agreed(TelnetParserOptions? options = null)
    {
        var parser = options is null ? new TelnetParser() : new TelnetParser(options);
        parser.Feed([255, 253, 24]);
        return parser;
    }

    [Fact]
    public void DefaultCapabilitiesLeaveUtf8ToTheConsumer()
    {
        Assert.Equal(
            MttsCapabilities.Ansi | MttsCapabilities.Vt100 | MttsCapabilities.Colors256 | MttsCapabilities.TrueColor,
            TelnetParserOptions.Default.Capabilities);
        Assert.Equal(267, (int)TelnetParserOptions.Default.Capabilities);
    }

    [Fact]
    public void Utf8ConsumerAdvertisesUtf8()
    {
        var options = new TelnetParserOptions { Capabilities = TelnetParserOptions.Default.Capabilities | MttsCapabilities.Utf8 };
        var parser = Agreed(options);
        parser.Feed(Send);
        parser.Feed(Send);
        Assert.Equal("MTTS 271", Answer(parser.Feed(Send).Reply));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4096)]
    [InlineData(int.MaxValue)]
    public void UndefinedCapabilityBitsAreRejected(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TelnetParser(new TelnetParserOptions { Capabilities = (MttsCapabilities)value }));
    }

    [Fact]
    public void EveryDefinedCapabilityBitIsAccepted()
    {
        _ = new TelnetParser(new TelnetParserOptions { Capabilities = (MttsCapabilities)4095 });
        _ = new TelnetParser(new TelnetParserOptions { Capabilities = MttsCapabilities.None });
    }

    [Fact]
    public void BitValuesFollowTheMttsSpecification()
    {
        Assert.Equal(1, (int)MttsCapabilities.Ansi);
        Assert.Equal(2, (int)MttsCapabilities.Vt100);
        Assert.Equal(4, (int)MttsCapabilities.Utf8);
        Assert.Equal(8, (int)MttsCapabilities.Colors256);
        Assert.Equal(16, (int)MttsCapabilities.MouseTracking);
        Assert.Equal(32, (int)MttsCapabilities.OscColorPalette);
        Assert.Equal(64, (int)MttsCapabilities.ScreenReader);
        Assert.Equal(128, (int)MttsCapabilities.Proxy);
        Assert.Equal(256, (int)MttsCapabilities.TrueColor);
        Assert.Equal(512, (int)MttsCapabilities.Mnes);
        Assert.Equal(1024, (int)MttsCapabilities.Mslp);
        Assert.Equal(2048, (int)MttsCapabilities.Ssl);
    }

    [Fact]
    public void CycleSendsNameThenTerminalThenMttsAndRepeatsTheLast()
    {
        var parser = Agreed();
        Assert.Equal(ClientIdentity.TerminalType, Answer(parser.Feed(Send).Reply));
        Assert.Equal("XTERM-256COLOR", Answer(parser.Feed(Send).Reply));
        Assert.Equal("MTTS 267", Answer(parser.Feed(Send).Reply));
        Assert.Equal("MTTS 267", Answer(parser.Feed(Send).Reply));
        Assert.Equal("MTTS 267", Answer(parser.Feed(Send).Reply));
    }

    [Fact]
    public void ConsumerSuppliesNamesAndCapabilities()
    {
        var parser = Agreed(new TelnetParserOptions
        {
            ClientName = "WANDUR",
            TerminalType = "XTERM-TRUECOLOR",
            Capabilities = MttsCapabilities.Ansi | MttsCapabilities.Colors256 | MttsCapabilities.Ssl
        });
        Assert.Equal("WANDUR", Answer(parser.Feed(Send).Reply));
        Assert.Equal("XTERM-TRUECOLOR", Answer(parser.Feed(Send).Reply));
        Assert.Equal("MTTS 2057", Answer(parser.Feed(Send).Reply));
    }

    [Fact]
    public void DontTerminalTypeResetsTheCycle()
    {
        var parser = Agreed();
        parser.Feed(Send);
        parser.Feed(Send);
        Assert.Equal(new byte[] { 255, 252, 24 }, parser.Feed([255, 254, 24]).Reply);
        Assert.Equal(new byte[] { 255, 251, 24 }, parser.Feed([255, 253, 24]).Reply);
        Assert.Equal(ClientIdentity.TerminalType, Answer(parser.Feed(Send).Reply));
    }

    [Fact]
    public void RequestSplitAcrossReadsIsAnsweredOnce()
    {
        var parser = Agreed();
        var reply = new List<byte>();
        foreach (var b in Send.Concat(Send)) reply.AddRange(parser.Feed([b]).Reply);
        var expected = new List<byte>();
        foreach (var name in new[] { ClientIdentity.TerminalType, "XTERM-256COLOR" })
            expected.AddRange(new byte[] { 255, 250, 24, 0 }.Concat(Encoding.ASCII.GetBytes(name)).Concat(new byte[] { 255, 240 }));
        Assert.Equal(expected, reply);
    }

    [Fact]
    public void RequestBeforeAgreementIsIgnored()
    {
        Assert.Empty(new TelnetParser().Feed(Send).Reply);
    }

    [Fact]
    public void InvalidNamesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new TelnetParser(new TelnetParserOptions { ClientName = "" }));
        Assert.Throws<ArgumentException>(() => new TelnetParser(new TelnetParserOptions { TerminalType = "XTERMé" }));
    }
}

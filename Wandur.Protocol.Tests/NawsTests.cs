using Wandur.Core.Protocol;

namespace Wandur.Protocol.Tests;

public class NawsTests
{
    private static byte[] Naws(params byte[] size) => [255, 250, 31, .. size, 255, 240];

    [Fact]
    public void DoNawsSendsTheConfiguredSize()
    {
        var parser = new TelnetParser(new TelnetParserOptions { WindowColumns = 132, WindowRows = 50 });
        Assert.Equal([255, 251, 31, .. Naws(0, 132, 0, 50)], parser.Feed([255, 253, 31]).Reply);
        Assert.True(parser.NawsEnabled);
    }

    [Fact]
    public void WideSizesUseBothBytes()
    {
        var parser = new TelnetParser(new TelnetParserOptions { WindowColumns = 300, WindowRows = 1000 });
        Assert.Equal([255, 251, 31, .. Naws(1, 44, 3, 232)], parser.Feed([255, 253, 31]).Reply);
    }

    [Fact]
    public void Byte255InWidthOrHeightIsEscaped()
    {
        var parser = new TelnetParser(new TelnetParserOptions { WindowColumns = 255, WindowRows = 0xFF00 | 0xFF });
        Assert.Equal([255, 251, 31, .. Naws(0, 255, 255, 255, 255, 255, 255)], parser.Feed([255, 253, 31]).Reply);
    }

    [Fact]
    public void UpdateBeforeAgreementSendsNothingButIsUsedLater()
    {
        var parser = new TelnetParser();
        Assert.False(parser.NawsEnabled);
        Assert.Empty(parser.UpdateWindowSize(80, 24));
        Assert.Equal(80, parser.WindowColumns);
        Assert.Equal(24, parser.WindowRows);
        Assert.Equal([255, 251, 31, .. Naws(0, 80, 0, 24)], parser.Feed([255, 253, 31]).Reply);
    }

    [Fact]
    public void UpdateAfterAgreementSendsOnlyChanges()
    {
        var parser = new TelnetParser();
        parser.Feed([255, 253, 31]);
        Assert.Empty(parser.UpdateWindowSize(100, 40));
        Assert.Equal(Naws(0, 120, 0, 255, 255), parser.UpdateWindowSize(120, 255));
        Assert.Empty(parser.UpdateWindowSize(120, 255));
        Assert.Equal(Naws(0, 121, 0, 255, 255), parser.UpdateWindowSize(121, 255));
    }

    [Fact]
    public void DontNawsStopsUpdatesUntilAgreedAgain()
    {
        var parser = new TelnetParser();
        parser.Feed([255, 253, 31]);
        Assert.Equal(new byte[] { 255, 252, 31 }, parser.Feed([255, 254, 31]).Reply);
        Assert.False(parser.NawsEnabled);
        Assert.Empty(parser.UpdateWindowSize(90, 30));
        Assert.Equal([255, 251, 31, .. Naws(0, 90, 0, 30)], parser.Feed([255, 253, 31]).Reply);
    }

    [Fact]
    public void RepeatedDoNawsDoesNotResend()
    {
        var parser = new TelnetParser();
        parser.Feed([255, 253, 31]);
        Assert.Empty(parser.Feed([255, 253, 31]).Reply);
    }

    [Fact]
    public void ZeroDimensionIsSentAsIs()
    {
        var parser = new TelnetParser(new TelnetParserOptions { WindowColumns = 0, WindowRows = 24 });
        Assert.Equal([255, 251, 31, .. Naws(0, 0, 0, 24)], parser.Feed([255, 253, 31]).Reply);
        Assert.Equal(Naws(0, 80, 0, 0), parser.UpdateWindowSize(80, 0));
    }

    [Theory]
    [InlineData(-1, 24)]
    [InlineData(80, -1)]
    [InlineData(65536, 24)]
    [InlineData(80, 65536)]
    public void SizesOutsideSixteenBitsAreRejected(int columns, int rows)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TelnetParser().UpdateWindowSize(columns, rows));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TelnetParser(new TelnetParserOptions { WindowColumns = columns, WindowRows = rows }));
    }
}

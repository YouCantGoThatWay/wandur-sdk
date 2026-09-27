using System.Text;
using Wandur.Core.Protocol;

namespace Wandur.Protocol.Tests;

public class MsspTests
{
    private const byte Var = 1, Val = 2;

    private static byte[] Block(params byte[] payload) => [255, 250, 70, .. payload, 255, 240];
    private static byte[] A(string s) => Encoding.UTF8.GetBytes(s);

    private static TelnetParser Agreed()
    {
        var parser = new TelnetParser(new TelnetParserOptions { AcceptMssp = true });
        Assert.Equal(new byte[] { 255, 253, 70 }, parser.Feed([255, 251, 70]).Reply);
        return parser;
    }

    [Fact]
    public void OptInAcceptsWillMsspOnce()
    {
        var parser = Agreed();
        Assert.Empty(parser.Feed([255, 251, 70]).Reply);
        Assert.Equal(new byte[] { 255, 254, 70 }, parser.Feed([255, 252, 70]).Reply);
    }

    [Fact]
    public void WithoutOptInTheBlockIsIgnored()
    {
        var parser = new TelnetParser();
        Assert.Equal(new byte[] { 255, 254, 70 }, parser.Feed([255, 251, 70]).Reply);
        Assert.Empty(parser.Feed(Block([Var, .. A("NAME"), Val, .. A("Somewhere")])).Mssp);
    }

    [Fact]
    public void BlockBeforeAgreementIsIgnored()
    {
        var parser = new TelnetParser(new TelnetParserOptions { AcceptMssp = true });
        Assert.Empty(parser.Feed(Block([Var, .. A("NAME"), Val, .. A("Somewhere")])).Mssp);
    }

    [Fact]
    public void VariablesArraysAndRepeatsAreCollected()
    {
        var packet = Agreed().Feed([.. Block([
            Var, .. A("NAME"), Val, .. A(" Somewhere "),
            Var, .. A("PLAYERS"), Val, .. A("12"),
            Var, .. A("PORT"), Val, .. A("4000"), Val, .. A("4001"),
            Var, .. A("PORT"), Val, .. A("4443"),
            Var, .. A("CODEBASE"), Val, .. A("Mérc")]), .. A("text")]);
        Assert.Equal("text", Encoding.ASCII.GetString(packet.Text));
        var table = Assert.Single(packet.Mssp);
        Assert.Equal(["NAME", "PLAYERS", "PORT", "CODEBASE"], table.Keys);
        Assert.Equal("Somewhere", table.GetFirst("NAME"));
        Assert.Equal("12", table.GetFirst("players"));
        Assert.Equal(["4000", "4001", "4443"], table["PORT"]);
        Assert.Equal("Mérc", table.GetFirst("CODEBASE"));
        Assert.Null(table.GetFirst("UPTIME"));
        Assert.False(table.ContainsKey("UPTIME"));
        Assert.Equal(4, table.Count);
    }

    [Fact]
    public void BlockSplitAcrossReadsIsParsedOnceComplete()
    {
        var parser = Agreed();
        var input = Block([Var, .. A("NAME"), Val, .. A("Split"), Var, .. A("PLAYERS"), Val, .. A("3")]);
        var tables = new List<MsspTable>();
        foreach (var b in input) tables.AddRange(parser.Feed([b]).Mssp);
        var table = Assert.Single(tables);
        Assert.Equal("Split", table.GetFirst("NAME"));
        Assert.Equal("3", table.GetFirst("PLAYERS"));
    }

    [Fact]
    public void EscapedIacInsideAValueIsOneByte()
    {
        var table = Assert.Single(Agreed().Feed([255, 250, 70, Var, .. A("X"), Val, 65, 255, 255, 66, 255, 240]).Mssp);
        Assert.Equal(Encoding.UTF8.GetString([65, 255, 66]), table.GetFirst("X"));
    }

    [Fact]
    public void OversizedBlockIsDroppedAndTextResumes()
    {
        var parser = Agreed();
        parser.Feed([255, 250, 70, Var, .. A("NAME"), Val]);
        Assert.Empty(parser.Feed(Enumerable.Repeat((byte)65, 20_000).ToArray()).Text);
        var packet = parser.Feed([255, 240, .. A("OK")]);
        Assert.Empty(packet.Mssp);
        Assert.Equal("OK", Encoding.ASCII.GetString(packet.Text));
    }

    [Fact]
    public void VariableAndValueCountsAreCapped()
    {
        var payload = new List<byte>();
        for (var i = 0; i < 250; i++) payload.AddRange([Var, .. A($"V{i}"), Val, .. A("x")]);
        payload.AddRange([Var, .. A("V0")]);
        for (var i = 0; i < 100; i++) payload.AddRange([Val, .. A(i.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
        var table = MsspTable.Parse(payload.ToArray());
        Assert.Equal(MsspTable.MaxVariables, table.Count);
        Assert.Equal(MsspTable.MaxValuesPerVariable, table["V0"].Count);
        Assert.False(table.ContainsKey("V200"));
    }

    [Fact]
    public void MalformedPiecesAreSkippedAndTheRestKept()
    {
        var table = MsspTable.Parse([
            .. A("junk"), Val, .. A("orphan"),
            Var, .. A("NOVALUE"),
            Var, Val, .. A("nameless"),
            Var, .. A("  "), Val, .. A("blank name"),
            Var, .. A("EMPTY"), Val,
            Var, .. A("GOOD"), Val, .. A("yes")]);
        Assert.Equal(["EMPTY", "GOOD"], table.Keys);
        Assert.Equal("", table.GetFirst("EMPTY"));
        Assert.Equal("yes", table.GetFirst("GOOD"));
    }

    [Fact]
    public void BlockWithNoVariablesRaisesNothing()
    {
        Assert.Empty(Agreed().Feed(Block(A("nothing here"))).Mssp);
        Assert.Empty(MsspTable.Parse([]));
    }

    [Fact]
    public void PlainTextReplyIsParsed()
    {
        var text = "Welcome\r\nMSSP-REPLY-START\r\nNAME\tSomewhere\r\nPORT\t4000\r\nPORT\t4001\r\nbroken line\r\nMSSP-REPLY-END\r\n";
        var table = MsspTable.ParsePlainText(text);
        Assert.Equal("Somewhere", table.GetFirst("NAME"));
        Assert.Equal(["4000", "4001"], table["PORT"]);
        Assert.Equal(2, table.Count);
        Assert.Empty(MsspTable.ParsePlainText("MSSP-REPLY-END\nNAME\tx\nMSSP-REPLY-START\n"));
    }
}

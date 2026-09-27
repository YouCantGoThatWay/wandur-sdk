using System.Text;
using Wandur.Core.Protocol;

namespace Wandur.Protocol.Tests;

public class PromptMarkTests
{
    [Fact]
    public void GoAheadMarksThePromptAtItsTextOffset()
    {
        var packet = new TelnetParser().Feed([.. "You see.\r\n<100hp> "u8, 255, 249, .. "more"u8]);
        Assert.Equal("You see.\r\n<100hp> more", Encoding.ASCII.GetString(packet.Text));
        var mark = Assert.Single(packet.PromptMarks);
        Assert.Equal(new TelnetPromptMark(18, TelnetPromptKind.GoAhead), mark);
        Assert.Equal("You see.\r\n<100hp> ", Encoding.ASCII.GetString(packet.Text, 0, mark.Offset));
    }

    [Fact]
    public void EndOfRecordMarksThePrompt()
    {
        var packet = new TelnetParser().Feed([.. "> "u8, 255, 239]);
        Assert.Equal([new TelnetPromptMark(2, TelnetPromptKind.EndOfRecord)], packet.PromptMarks);
    }

    [Fact]
    public void MarkSplitFromItsIacLandsAtTheStartOfTheNextRead()
    {
        var parser = new TelnetParser();
        var first = parser.Feed([.. "> "u8, 255]);
        Assert.Equal("> ", Encoding.ASCII.GetString(first.Text));
        Assert.Empty(first.PromptMarks);
        var second = parser.Feed([249, .. "next"u8]);
        Assert.Equal("next", Encoding.ASCII.GetString(second.Text));
        Assert.Equal([new TelnetPromptMark(0, TelnetPromptKind.GoAhead)], second.PromptMarks);
    }

    [Fact]
    public void ByteAtATimeKeepsEveryMarkInOrder()
    {
        var parser = new TelnetParser();
        byte[] input = [.. "a> "u8, 255, 249, .. "b> "u8, 255, 239, 255, 249];
        var text = new List<byte>();
        var marks = new List<(int, TelnetPromptKind)>();
        foreach (var b in input)
        {
            var packet = parser.Feed([b]);
            foreach (var mark in packet.PromptMarks) marks.Add((text.Count + mark.Offset, mark.Kind));
            text.AddRange(packet.Text);
        }
        Assert.Equal("a> b> ", Encoding.ASCII.GetString(text.ToArray()));
        Assert.Equal([(3, TelnetPromptKind.GoAhead), (6, TelnetPromptKind.EndOfRecord), (6, TelnetPromptKind.GoAhead)], marks);
    }

    [Fact]
    public void MarksAfterNegotiationAndEscapedIacCountOnlyTextBytes()
    {
        var packet = new TelnetParser().Feed([65, 255, 255, 255, 251, 3, 66, 255, 249]);
        Assert.Equal(new byte[] { 65, 255, 66 }, packet.Text);
        Assert.Equal([new TelnetPromptMark(3, TelnetPromptKind.GoAhead)], packet.PromptMarks);
    }

    [Fact]
    public void GoAheadInsideSubnegotiationIsNotAPrompt()
    {
        var parser = new TelnetParser();
        parser.Feed([255, 251, 201]);
        Assert.Empty(parser.Feed([255, 250, 201, 65, 255, 249, 66, 255, 240]).PromptMarks);
    }

    [Fact]
    public void PlainTextHasNoMarks()
    {
        Assert.Empty(new TelnetParser().Feed("hello\r\n"u8).PromptMarks);
    }

    [Fact]
    public void WillEndOfRecordIsAcceptedByDefault()
    {
        var parser = new TelnetParser();
        Assert.Equal(new byte[] { 255, 253, 25 }, parser.Feed([255, 251, 25]).Reply);
        Assert.Empty(parser.Feed([255, 251, 25]).Reply);
        Assert.Equal(new byte[] { 255, 254, 25 }, parser.Feed([255, 252, 25]).Reply);
    }

    [Fact]
    public void EndOfRecordCanBeRefused()
    {
        var parser = new TelnetParser(new TelnetParserOptions { AcceptEndOfRecord = false });
        Assert.Equal(new byte[] { 255, 254, 25 }, parser.Feed([255, 251, 25]).Reply);
    }
}

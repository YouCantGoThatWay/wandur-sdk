namespace Wandur.Core.Protocol;

/// <summary>Terminal capabilities advertised in the MTTS reply (the third answer to TTYPE SEND).
/// Values follow https://tintin.mudhalla.net/protocols/mtts/.</summary>
[Flags]
public enum MttsCapabilities
{
    None = 0,
    Ansi = 1,
    Vt100 = 2,
    Utf8 = 4,
    Colors256 = 8,
    MouseTracking = 16,
    OscColorPalette = 32,
    ScreenReader = 64,
    Proxy = 128,
    TrueColor = 256,
    Mnes = 512,
    Mslp = 1024,
    Ssl = 2048
}

/// <summary>What a <see cref="TelnetParser"/> tells the server about the client. Every property has a
/// default that matches the Wandur desktop client, so <c>new TelnetParser()</c> behaves as before apart
/// from the additions documented on each property.</summary>
public sealed record TelnetParserOptions
{
    /// <summary>The defaults: the Wandur client terminal without TLS and without claiming UTF-8.</summary>
    public static TelnetParserOptions Default { get; } = new();

    /// <summary>First TTYPE answer. Printable ASCII, at most 64 characters.</summary>
    public string ClientName { get; init; } = ClientIdentity.TerminalType;

    /// <summary>Second TTYPE answer. Printable ASCII, at most 64 characters.</summary>
    public string TerminalType { get; init; } = "XTERM-256COLOR";

    /// <summary>Third and later TTYPE answers, sent as <c>MTTS &lt;decimal&gt;</c>. The default is ANSI,
    /// VT100, 256 colors and truecolor (267). It does not include <see cref="MttsCapabilities.Utf8"/>: the
    /// consumer sets <see cref="MttsCapabilities.Utf8"/> when it decodes the connection as UTF-8, and
    /// <see cref="MttsCapabilities.Ssl"/> on a TLS connection, so a Latin-1 profile never claims UTF-8.
    /// Only the defined bits (0 to 4095) are allowed.</summary>
    public MttsCapabilities Capabilities { get; init; } =
        MttsCapabilities.Ansi | MttsCapabilities.Vt100 | MttsCapabilities.Colors256 | MttsCapabilities.TrueColor;

    /// <summary>NAWS width in columns sent when the server asks with DO NAWS, 0 to 65535.
    /// <see cref="TelnetParser.UpdateWindowSize"/> changes it later.</summary>
    public int WindowColumns { get; init; } = 100;

    /// <summary>NAWS height in rows, 0 to 65535.</summary>
    public int WindowRows { get; init; } = 40;

    /// <summary>Answer WILL EOR (option 25) with DO, so servers mark prompts with IAC EOR. GA and EOR
    /// marks are reported in <see cref="TelnetPacket.PromptMarks"/> whatever this is set to.</summary>
    public bool AcceptEndOfRecord { get; init; } = true;

    /// <summary>Answer WILL MSSP (option 70) with DO and report the server's tables in
    /// <see cref="TelnetPacket.Mssp"/>. Off by default, so existing consumers keep refusing it.</summary>
    public bool AcceptMssp { get; init; }

    internal void Validate()
    {
        ValidateSize(WindowColumns, WindowRows);
        if ((int)Capabilities is < 0 or > 4095)
            throw new ArgumentOutOfRangeException(nameof(Capabilities), Capabilities, "Capabilities may only use the defined MTTS bits (0 to 4095).");
        ValidateName(ClientName, nameof(ClientName));
        ValidateName(TerminalType, nameof(TerminalType));
    }

    internal static void ValidateSize(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(columns);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, ushort.MaxValue);
    }

    private static void ValidateName(string value, string name)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 || value.Any(c => c is < '!' or > '~'))
            throw new ArgumentException($"{name} must be 1 to 64 printable ASCII characters without spaces.", name);
    }
}

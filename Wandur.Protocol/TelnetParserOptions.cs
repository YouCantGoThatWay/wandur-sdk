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
    /// <summary>The defaults: the Wandur client on a UTF-8 profile without TLS.</summary>
    public static TelnetParserOptions Default { get; } = new();

    /// <summary>First TTYPE answer. Printable ASCII, at most 64 characters.</summary>
    public string ClientName { get; init; } = ClientIdentity.TerminalType;

    /// <summary>Second TTYPE answer. Printable ASCII, at most 64 characters.</summary>
    public string TerminalType { get; init; } = "XTERM-256COLOR";

    /// <summary>Third and later TTYPE answers, sent as <c>MTTS &lt;decimal&gt;</c>. The consumer clears
    /// <see cref="MttsCapabilities.Utf8"/> for a non-UTF-8 profile and sets <see cref="MttsCapabilities.Ssl"/>
    /// on a TLS connection.</summary>
    public MttsCapabilities Capabilities { get; init; } =
        MttsCapabilities.Ansi | MttsCapabilities.Vt100 | MttsCapabilities.Utf8 | MttsCapabilities.Colors256 | MttsCapabilities.TrueColor;

    internal void Validate()
    {
        ValidateName(ClientName, nameof(ClientName));
        ValidateName(TerminalType, nameof(TerminalType));
    }

    private static void ValidateName(string value, string name)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 || value.Any(c => c is < '!' or > '~'))
            throw new ArgumentException($"{name} must be 1 to 64 printable ASCII characters without spaces.", name);
    }
}

namespace Wandur.Core.Protocol;

/// <summary>How Wandur names itself on the wire (TTYPE and GMCP Core.Hello) so world admins can recognise the client.</summary>
public static class ClientIdentity
{
    /// <summary>Short product name.</summary>
    public const string Name = "Wandur";

    /// <summary>Human-readable client label for logs and fingerprints.</summary>
    public const string DisplayName = "Wandur Mud Client (WMC)";

    /// <summary>Public site; included in Core.Hello so admins have a pointer.</summary>
    public const string Website = "https://www.wandur.net";

    /// <summary>Compact telnet terminal-type reply (option 24). Prefer ASCII and keep it short for older servers.</summary>
    public const string TerminalType = "Wandur-WMC";

    /// <summary>Advertised build version in Core.Hello.</summary>
    public const string Version = "0.1.0";

    /// <summary>Full GMCP Core.Hello message as sent after GMCP is enabled.</summary>
    public const string GmcpHelloBody =
        "Core.Hello {\"client\":\"Wandur Mud Client (WMC)\",\"version\":\"0.1.0\",\"url\":\"https://www.wandur.net\"}";
}

using System.Text;

namespace Wandur.Core.Protocol;

internal static class TelnetParserMessages
{
    public const string SendOneCommandAtATime = "Send one command at a time.";
    public const string CommandIsTooLong = "Command is too long.";
}

public enum TelnetOptionState { Unknown, Enabled, Disabled }
public sealed record TelnetProtocolState(TelnetOptionState Gmcp = TelnetOptionState.Unknown, TelnetOptionState Msdp = TelnetOptionState.Unknown);

public sealed record TelnetDataMessage(byte Option, byte[] Payload)
{
    public bool MayContainPrivateText { get; init; }
}

/// <summary>Which telnet command ended a prompt.</summary>
public enum TelnetPromptKind
{
    /// <summary>IAC GA (249).</summary>
    GoAhead,
    /// <summary>IAC EOR (239), usually sent once EOR (option 25) is agreed.</summary>
    EndOfRecord
}

/// <summary>A prompt boundary. <paramref name="Offset"/> counts the bytes of <see cref="TelnetPacket.Text"/>
/// that came before the mark, so <c>Text[..Offset]</c> is the text up to the prompt end. A mark whose IAC
/// arrived in an earlier read has offset 0: everything before it was in earlier packets. Several marks can
/// share an offset (a server may send GA and EOR together, or GA on every write); consumers decide which
/// ones matter.</summary>
public readonly record struct TelnetPromptMark(int Offset, TelnetPromptKind Kind);

public sealed record TelnetPacket(byte[] Text, byte[] Reply, IReadOnlyList<string> Gmcp)
{
    /// <summary>GA and EOR prompt boundaries in <see cref="Text"/>, in stream order.</summary>
    public IReadOnlyList<TelnetPromptMark> PromptMarks { get; init; } = [];
    /// <summary>MSSP tables received in this read, when <see cref="TelnetParserOptions.AcceptMssp"/> is on.
    /// A block with no usable variable is not reported.</summary>
    public IReadOnlyList<MsspTable> Mssp { get; init; } = [];
    public bool MayContainPrivateText { get; init; }
    public IReadOnlyList<byte[]> Msdp { get; init; } = [];
    public IReadOnlyList<TelnetDataMessage> DataMessages { get; init; } = [];
}

public sealed class TelnetParser
{
    private enum State { Text, Iac, Option, Sub, SubIac }
    private State _state;
    private byte _verb;
    private readonly List<byte> _sub = [];
    private readonly HashSet<byte> _remote = [];
    private readonly HashSet<byte> _local = [];
    private readonly ProtocolDiscovery _discovery = new();
    private readonly TelnetParserOptions _options;
    private int _terminalTypeRequests;
    private (int Columns, int Rows)? _sentWindowSize;
    private bool _overflow;
    private bool _subPrivate;
    private bool _localPrivate;
    public bool ServerEcho { get; private set; }
    public TelnetProtocolState ProtocolState { get; private set; } = new();

    public TelnetParser() : this(null) { }

    /// <param name="options">What to tell the server about the client; null means <see cref="TelnetParserOptions.Default"/>.</param>
    public TelnetParser(TelnetParserOptions? options)
    {
        _options = options ?? TelnetParserOptions.Default;
        _options.Validate();
        WindowColumns = _options.WindowColumns;
        WindowRows = _options.WindowRows;
    }

    /// <summary>The window width the parser reports through NAWS.</summary>
    public int WindowColumns { get; private set; }

    /// <summary>The window height the parser reports through NAWS.</summary>
    public int WindowRows { get; private set; }

    /// <summary>Whether the server asked for NAWS (DO NAWS) and the parser agreed.</summary>
    public bool NawsEnabled => _local.Contains(31);

    /// <summary>Record a new window size and return the NAWS subnegotiation to send, or an empty array
    /// when NAWS is not agreed yet or the server already has this size. The size is kept either way and
    /// sent when the server later asks. Call it under the same lock as <see cref="Feed"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is outside 0 to 65535.</exception>
    public byte[] UpdateWindowSize(int columns, int rows)
    {
        TelnetParserOptions.ValidateSize(columns, rows);
        WindowColumns = columns;
        WindowRows = rows;
        if (!NawsEnabled || _sentWindowSize == (columns, rows)) return [];
        return WindowSizeSubnegotiation();
    }

    private byte[] WindowSizeSubnegotiation()
    {
        _sentWindowSize = (WindowColumns, WindowRows);
        return Subnegotiation(31, [(byte)(WindowColumns >> 8), (byte)WindowColumns, (byte)(WindowRows >> 8), (byte)WindowRows]);
    }

    /// <summary>The options this parser was created with.</summary>
    public TelnetParserOptions Options => _options;

    /// <summary>Latch privacy on an unfinished message, even if no bytes arrive during the interval.</summary>
    public void SetLocalPrivateInput(bool enabled)
    {
        _localPrivate = enabled;
        if (enabled && _state is State.Sub or State.SubIac) _subPrivate = true;
    }

    public TelnetPacket Feed(ReadOnlySpan<byte> bytes)
    {
        var text = new List<byte>();
        var replies = new List<byte>();
        var gmcp = new List<string>();
        var msdp = new List<byte[]>();
        var dataMessages = new List<TelnetDataMessage>();
        List<TelnetPromptMark>? promptMarks = null;
        List<MsspTable>? mssp = null;
        // A single read can enter and leave server echo mode. Preserve any private
        // interval instead of inferring privacy from only the final echo state.
        var mayContainPrivateText = ServerEcho || _localPrivate;
        foreach (byte b in bytes)
        {
            switch (_state)
            {
                case State.Text:
                    if (b == 255) _state = State.Iac;
                    else text.Add(b);
                    break;
                case State.Iac:
                    if (b == 255) { text.Add(b); _state = State.Text; }
                    else if (b is >= 251 and <= 254) { _verb = b; _state = State.Option; }
                    else if (b == 250) { _sub.Clear(); _overflow = false; _subPrivate = ServerEcho || _localPrivate; _state = State.Sub; }
                    else
                    {
                        if (b is 249 or 239)
                            (promptMarks ??= []).Add(new(text.Count, b == 249 ? TelnetPromptKind.GoAhead : TelnetPromptKind.EndOfRecord));
                        _state = State.Text;
                    }
                    break;
                case State.Option:
                    Negotiate(b, replies);
                    mayContainPrivateText |= ServerEcho;
                    _state = State.Text;
                    break;
                case State.Sub:
                    if (b == 255) _state = State.SubIac;
                    else AddSub(b);
                    break;
                case State.SubIac:
                    if (b == 240)
                    {
                        if (!_overflow && _sub.Count > 0)
                        {
                            if (_sub[0] == 24 && _local.Contains(24) && _sub.Count > 1 && _sub[1] == 1)
                                replies.AddRange(Subnegotiation(24, new byte[] { 0 }.Concat(Encoding.ASCII.GetBytes(NextTerminalType())).ToArray()));
                            if (_sub[0] == 201 && _remote.Contains(201))
                            {
                                var payload = _sub.Skip(1).ToArray();
                                gmcp.Add(Encoding.UTF8.GetString(payload));
                                dataMessages.Add(new(201, payload) { MayContainPrivateText = _subPrivate || GmcpLoginProtocol.IsPrivate(gmcp[^1]) });
                                foreach (var request in _discovery.Receive(201, payload)) replies.AddRange(Subnegotiation(201, request));
                            }
                            if (_sub[0] == 70 && _remote.Contains(70)
                                && MsspTable.Parse(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_sub)[1..]) is { Count: > 0 } table)
                                (mssp ??= []).Add(table);
                            if (_sub[0] == 69 && _remote.Contains(69))
                            {
                                var payload = _sub.Skip(1).ToArray();
                                msdp.Add(payload);
                                dataMessages.Add(new(69, payload) { MayContainPrivateText = _subPrivate });
                                foreach (var request in _discovery.Receive(69, payload)) replies.AddRange(Subnegotiation(69, request));
                            }
                        }
                        _sub.Clear();
                        _state = State.Text;
                    }
                    else { if (b == 255) AddSub(b); _state = State.Sub; }
                    break;
            }
        }
        return new(text.ToArray(), replies.ToArray(), gmcp)
        {
            Msdp = msdp,
            DataMessages = dataMessages,
            PromptMarks = promptMarks ?? (IReadOnlyList<TelnetPromptMark>)[],
            Mssp = mssp ?? (IReadOnlyList<MsspTable>)[],
            MayContainPrivateText = mayContainPrivateText
        };
    }

    private void AddSub(byte b)
    {
        if (_sub.Count < 16_384 && !_overflow) _sub.Add(b);
        else { _overflow = true; _sub.Clear(); }
    }

    private void Negotiate(byte option, List<byte> reply)
    {
        if (_verb is 251 or 252)
        {
            var state = _verb == 251 ? TelnetOptionState.Enabled : TelnetOptionState.Disabled;
            if (option == 201) ProtocolState = ProtocolState with { Gmcp = state };
            if (option == 69) ProtocolState = ProtocolState with { Msdp = state };
        }
        switch (_verb)
        {
            case 251: // WILL: accept server echo, suppress-go-ahead, MSDP, GMCP and, by option, EOR and MSSP.
                if (option is 1 or 3 or 69 or 201 || (option == 25 && _options.AcceptEndOfRecord) || (option == 70 && _options.AcceptMssp))
                {
                    if (_remote.Add(option))
                    {
                        reply.AddRange([255, 253, option]);
                        if (option == 201)
                        {
                            reply.AddRange(Subnegotiation(201, Encoding.UTF8.GetBytes(ClientIdentity.GmcpHelloBody)));
                            reply.AddRange(Subnegotiation(201, Encoding.UTF8.GetBytes(ProtocolDiscovery.GmcpSupports)));
                            reply.AddRange(Subnegotiation(201, Encoding.UTF8.GetBytes(ProtocolDiscovery.GmcpDiscovery)));
                        }
                        if (option == 69)
                            reply.AddRange(Subnegotiation(69, Encoding.UTF8.GetBytes("\u0001LIST\u0002REPORTABLE_VARIABLES")));
                    }
                    if (option == 1) ServerEcho = true;
                }
                else reply.AddRange([255, 254, option]);
                break;
            case 252: // WONT
                if (_remote.Remove(option)) reply.AddRange([255, 254, option]);
                if (option == 1) ServerEcho = false;
                _discovery.Reset(option);
                break;
            case 253: // DO: suppress-go-ahead, terminal type and window size.
                if (option is 3 or 24 or 31)
                {
                    if (_local.Add(option))
                    {
                        reply.AddRange([255, 251, option]);
                        if (option == 31) reply.AddRange(WindowSizeSubnegotiation());
                    }
                }
                else reply.AddRange([255, 252, option]);
                break;
            case 254:
                if (_local.Remove(option)) reply.AddRange([255, 252, option]);
                if (option == 24) _terminalTypeRequests = 0; // MTTS: DONT TTYPE restarts the cycle.
                if (option == 31) _sentWindowSize = null;
                break;
        }
    }

    /// <summary>MTTS cycle: client name, terminal type, then <c>MTTS n</c>, repeated so the server sees
    /// the end of the list.</summary>
    private string NextTerminalType()
    {
        if (_terminalTypeRequests < 3) _terminalTypeRequests++;
        return _terminalTypeRequests switch
        {
            1 => _options.ClientName,
            2 => _options.TerminalType,
            _ => "MTTS " + ((int)_options.Capabilities).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    internal static byte[] Subnegotiation(byte option, byte[] payload)
    {
        var result = new List<byte> { 255, 250, option };
        foreach (var b in payload) { result.Add(b); if (b == 255) result.Add(b); }
        result.AddRange([255, 240]);
        return result.ToArray();
    }

    public static byte[] EncodeCommand(string command, Encoding encoding)
    {
        if (command.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new ArgumentException(TelnetParserMessages.SendOneCommandAtATime, nameof(command));
        if (command.Length > 8192) throw new ArgumentException(TelnetParserMessages.CommandIsTooLong, nameof(command));
        var result = new List<byte>();
        foreach (var b in encoding.GetBytes(command)) { result.Add(b); if (b == 255) result.Add(b); }
        result.AddRange([13, 10]);
        return result.ToArray();
    }
}

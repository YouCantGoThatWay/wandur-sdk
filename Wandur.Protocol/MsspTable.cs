using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Wandur.Core.Protocol;

/// <summary>A server's MSSP self-description (telnet option 70): variable names, case-insensitive, each
/// with one or more values in the order they arrived. Read-only.
///
/// The telnet form is <c>(MSSP_VAR name (MSSP_VAL value)+)*</c>. A variable with several MSSP_VAL parts,
/// or a variable that appears more than once, collects every value. Pieces that do not fit the shape are
/// skipped and the rest is kept: a world that answers badly still answered. At most
/// <see cref="MaxVariables"/> variables and <see cref="MaxValuesPerVariable"/> values each are kept; the
/// telnet parser already drops any subnegotiation over 16 KiB.</summary>
public sealed class MsspTable : IReadOnlyDictionary<string, IReadOnlyList<string>>
{
    public const int MaxVariables = 200;
    public const int MaxValuesPerVariable = 64;
    private const byte MsspVar = 1, MsspVal = 2;

    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _names = [];

    private MsspTable() { }

    public static MsspTable Empty { get; } = new();

    /// <summary>Parse the payload of <c>IAC SB MSSP ... IAC SE</c>, without the option byte and with IAC
    /// escapes already removed. Names and values are UTF-8 and trimmed.</summary>
    public static MsspTable Parse(ReadOnlySpan<byte> payload)
    {
        var table = new MsspTable();
        var name = new List<byte>();
        var value = new List<byte>();
        string? current = null;
        byte reading = 0;
        foreach (var b in payload)
        {
            if (b is MsspVar or MsspVal)
            {
                if (reading == MsspVal && current is not null) table.Add(current, Decode(value));
                if (b == MsspVar) { name.Clear(); current = null; }
                else if (reading == MsspVar) current = Decode(name) is { Length: > 0 } n ? n : null;
                // MSSP_VAL right after a value keeps the current name: an array value.
                value.Clear();
                reading = b;
                continue;
            }
            if (reading == MsspVar) name.Add(b);
            else if (reading == MsspVal) value.Add(b);
            // Bytes before the first MSSP_VAR belong to no variable and are dropped.
        }
        if (reading == MsspVal && current is not null) table.Add(current, Decode(value));
        return table;
    }

    /// <summary>Parse the plain-text form some worlds print: a <c>MSSP-REPLY-START</c> line, then
    /// <c>NAME&lt;tab&gt;VALUE</c> lines, then <c>MSSP-REPLY-END</c>. Lines without a tab are skipped.</summary>
    public static MsspTable ParsePlainText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var table = new MsspTable();
        var start = text.IndexOf("MSSP-REPLY-START", StringComparison.Ordinal);
        if (start < 0) return table;
        var end = text.IndexOf("MSSP-REPLY-END", start, StringComparison.Ordinal);
        if (end < 0) return table;
        foreach (var line in text[start..end].Split('\n').Skip(1))
        {
            var parts = line.TrimEnd('\r').Split('\t', 2);
            if (parts.Length == 2 && parts[0].Trim() is { Length: > 0 } name) table.Add(name, parts[1].Trim());
        }
        return table;
    }

    /// <summary>The first value of <paramref name="name"/>, or null when the server did not send it.</summary>
    public string? GetFirst(string name) => _values.TryGetValue(name, out var values) ? values[0] : null;

    private static string Decode(List<byte> bytes) => Encoding.UTF8.GetString([.. bytes]).Trim();

    private void Add(string name, string value)
    {
        if (!_values.TryGetValue(name, out var values))
        {
            if (_names.Count >= MaxVariables) return;
            _values[name] = values = [];
            _names.Add(name);
        }
        if (values.Count < MaxValuesPerVariable) values.Add(value);
    }

    public int Count => _names.Count;

    /// <summary>Names in the order the server first sent them, with the server's spelling.</summary>
    public IEnumerable<string> Keys => _names;

    public IEnumerable<IReadOnlyList<string>> Values => _names.Select(n => (IReadOnlyList<string>)_values[n]);

    public IReadOnlyList<string> this[string key] => _values[key];

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out IReadOnlyList<string> value)
    {
        var found = _values.TryGetValue(key, out var list);
        value = list;
        return found;
    }

    public IEnumerator<KeyValuePair<string, IReadOnlyList<string>>> GetEnumerator() =>
        _names.Select(n => new KeyValuePair<string, IReadOnlyList<string>>(n, _values[n])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

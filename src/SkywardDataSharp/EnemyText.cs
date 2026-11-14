using System.Text;

namespace SkywardDataSharp;

/// <summary>
/// An enemy parameter sheet (<c>.entxt</c>): Shift-JIS comma-separated text with CRLF lines, in sections separated by
/// blank lines (an id and name, a row of colour and float values, a body-part table, ...). Every row ends with a
/// comma, so its last cell is empty.
/// </summary>
public sealed class EnemyTextFile
{
    static readonly Encoding ShiftJis = CreateShiftJis();

    public List<List<string[]>> Sections { get; } = [];
    public bool EndsWithNewline { get; set; } = true;

    public static EnemyTextFile FromBinary(byte[] data)
    {
        string text = ShiftJis.GetString(data);
        var f = new EnemyTextFile { EndsWithNewline = text.EndsWith("\r\n") };
        if (f.EndsWithNewline) text = text[..^2];
        foreach (string section in text.Split("\r\n\r\n"))
            f.Sections.Add(section.Split("\r\n").Select(row => row.Split(',')).ToList());
        return f;
    }

    public byte[] Write()
    {
        string text = string.Join("\r\n\r\n", Sections.Select(s => string.Join("\r\n", s.Select(row => string.Join(',', row)))));
        return ShiftJis.GetBytes(EndsWithNewline ? text + "\r\n" : text);
    }

    public string Name => Sections.Count > 0 && Sections[0].Count > 0 && Sections[0][0].Length > 1 ? Sections[0][0][1] : "";

    static Encoding CreateShiftJis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}

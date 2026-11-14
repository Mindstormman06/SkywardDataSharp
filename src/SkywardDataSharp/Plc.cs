namespace SkywardDataSharp;

/// <summary>
/// A polygon-code table (<c>.plc</c>, <c>SPLC</c>): five 32-bit bitfields per collision code, which KCL/DZB polygons
/// pick by index. The file is padded to 0x20 bytes with 0xFF.
/// </summary>
public sealed class PlcFile
{
    const int HeaderSize = 8, EntrySize = 0x14;

    public List<PlcEntry> Entries { get; } = [];

    public static bool IsPlc(ReadOnlySpan<byte> d) => d.Length >= HeaderSize && d[..4].SequenceEqual("SPLC"u8);

    public static PlcFile FromBinary(ReadOnlySpan<byte> d)
    {
        if (!IsPlc(d)) throw new InvalidDataException("not an SPLC file");
        if (Be.U16(d, 4) != EntrySize) throw new InvalidDataException($"unexpected SPLC entry size {Be.U16(d, 4)}");
        int count = Be.U16(d, 6);
        Be.Need(d, HeaderSize + count * EntrySize, "SPLC entries");
        var f = new PlcFile();
        for (int i = 0; i < count; i++)
        {
            var e = new PlcEntry();
            for (int k = 0; k < PlcEntry.WordCount; k++) e.Words[k] = Be.U32(d, HeaderSize + i * EntrySize + k * 4);
            f.Entries.Add(e);
        }
        return f;
    }

    public byte[] Write()
    {
        var w = new BeWriter();
        w.Bytes("SPLC"u8); w.U16(EntrySize); w.U16(Checked.Count(Entries.Count, "codes"));
        foreach (PlcEntry e in Entries) foreach (uint v in e.Words) w.U32(v);
        while (w.Position % 0x20 != 0) w.U8(0xFF);
        return w.ToArray();
    }
}

public sealed class PlcEntry
{
    public const int WordCount = 5;
    public uint[] Words { get; } = new uint[WordCount];

    /// <summary>Reads <paramref name="width"/> bits of word <paramref name="word"/> starting at bit <paramref name="shift"/>.</summary>
    public uint Bits(int word, int shift, int width) => (Words[word] >> shift) & (width >= 32 ? uint.MaxValue : (1u << width) - 1);

    public void SetBits(int word, int shift, int width, uint value)
    {
        uint mask = (width >= 32 ? uint.MaxValue : (1u << width) - 1) << shift;
        Words[word] = (Words[word] & ~mask) | ((value << shift) & mask);
    }

    public override string ToString() => string.Join(" ", Words.Select(v => v.ToString("X8")));
}

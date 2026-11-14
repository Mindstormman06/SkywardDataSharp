namespace SkywardDataSharp;

/// <summary>
/// <c>navi_table.dat</c> (<c>V001</c>): two tables of u16 pairs, a table of named entries and a table of named
/// records, with the names in a string table at the end.
/// </summary>
public sealed class NaviTable
{
    const int HeaderSize = 0x1C, LabelSize = 8, EntrySize = 24;

    public List<NaviPair> Pairs1 { get; } = [];
    public List<NaviLabel> Labels { get; } = [];
    public List<NaviPair> Pairs2 { get; } = [];
    public List<NaviEntry> Entries { get; } = [];

    public static bool IsNaviTable(ReadOnlySpan<byte> d) => d.Length >= HeaderSize && d[..4].SequenceEqual("V001"u8);

    public static NaviTable FromBinary(ReadOnlySpan<byte> d)
    {
        if (!IsNaviTable(d)) throw new InvalidDataException("not a V001 navi table");
        int[] count = [Be.U16(d, 4), Be.U16(d, 6), Be.U16(d, 8), Be.U16(d, 10)];
        int[] at = [(int)Be.U32(d, 12), (int)Be.U32(d, 16), (int)Be.U32(d, 20), (int)Be.U32(d, 24)];
        int[] size = [4, LabelSize, 4, EntrySize];
        for (int i = 0; i < 4; i++)
            if (at[i] < HeaderSize) throw new InvalidDataException($"section {i} offset is inside the header");
            else Be.Need(d, (long)at[i] + (long)count[i] * size[i], $"section {i}");

        var t = new NaviTable();
        for (int i = 0; i < count[0]; i++) t.Pairs1.Add(new NaviPair(Be.U16(d, at[0] + i * 4), Be.U16(d, at[0] + i * 4 + 2)));
        for (int i = 0; i < count[1]; i++)
        {
            int a = at[1] + i * LabelSize;
            t.Labels.Add(new NaviLabel { Name = Be.CString(d, (int)Be.U32(d, a)), Value0 = Be.U16(d, a + 4), Value1 = Be.U16(d, a + 6) });
        }
        for (int i = 0; i < count[2]; i++) t.Pairs2.Add(new NaviPair(Be.U16(d, at[2] + i * 4), Be.U16(d, at[2] + i * 4 + 2)));
        for (int i = 0; i < count[3]; i++)
        {
            int a = at[3] + i * EntrySize;
            var e = new NaviEntry { Name = Be.CString(d, (int)Be.U32(d, a)), Value0 = Be.U16(d, a + 4), Value1 = Be.U16(d, a + 6) };
            d.Slice(a + 8, 4).CopyTo(e.Code);
            for (int k = 0; k < 4; k++) e.Values[k] = Be.U16(d, a + 12 + k * 2);
            e.Value2 = Be.U16(d, a + 20); e.Value3 = Be.U16(d, a + 22);
            t.Entries.Add(e);
        }
        return t;
    }

    public byte[] Write()
    {
        var w = new BeWriter();
        w.Bytes("V001"u8);
        w.U16(Checked.Count(Pairs1.Count, "pairs")); w.U16(Checked.Count(Labels.Count, "labels"));
        w.U16(Checked.Count(Pairs2.Count, "pairs")); w.U16(Checked.Count(Entries.Count, "entries"));
        int labelsAt = HeaderSize + Pairs1.Count * 4, pairs2At = labelsAt + Labels.Count * LabelSize, entriesAt = pairs2At + Pairs2.Count * 4;
        int stringsAt = entriesAt + Entries.Count * EntrySize;
        w.U32(HeaderSize); w.U32((uint)labelsAt); w.U32((uint)pairs2At); w.U32((uint)entriesAt);

        int next = stringsAt;
        int Place(string s) { int at = next; next += System.Text.Encoding.Latin1.GetByteCount(s) + 1; return at; }

        foreach (NaviPair p in Pairs1) { w.U16(p.A); w.U16(p.B); }
        foreach (NaviLabel l in Labels) { w.U32((uint)Place(l.Name)); w.U16(l.Value0); w.U16(l.Value1); }
        foreach (NaviPair p in Pairs2) { w.U16(p.A); w.U16(p.B); }
        foreach (NaviEntry e in Entries)
        {
            w.U32((uint)Place(e.Name)); w.U16(e.Value0); w.U16(e.Value1); w.Bytes(e.Code);
            foreach (ushort v in e.Values) w.U16(v);
            w.U16(e.Value2); w.U16(e.Value3);
        }
        foreach (NaviLabel l in Labels) w.CString(l.Name);
        foreach (NaviEntry e in Entries) w.CString(e.Name);
        return w.ToArray();
    }
}

public readonly record struct NaviPair(ushort A, ushort B);

public sealed class NaviLabel
{
    public string Name { get; set; } = "";
    public ushort Value0 { get; set; }
    public ushort Value1 { get; set; }
    public override string ToString() => $"{Name} {Value0:X4} {Value1:X4}";
}

public sealed class NaviEntry
{
    public string Name { get; set; } = "";
    public ushort Value0 { get; set; }
    public ushort Value1 { get; set; }
    /// <summary>Four bytes; sometimes ASCII (<c>2FP2</c>, <c>____</c>), sometimes small numbers.</summary>
    public byte[] Code { get; } = new byte[4];
    public ushort[] Values { get; } = new ushort[4];
    public ushort Value2 { get; set; }
    public ushort Value3 { get; set; }
    public override string ToString() => Name;
}

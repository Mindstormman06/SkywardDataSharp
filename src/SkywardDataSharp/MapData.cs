namespace SkywardDataSharp;

/// <summary><c>MapGradation.dat</c> (<c>CSVD</c>): per-map colour gradients, each a name, two heights and a colour.</summary>
public sealed class MapGradationFile
{
    const int EntrySize = 44;

    public List<MapGradation> Entries { get; } = [];

    public static bool IsMapGradation(ReadOnlySpan<byte> d) => d.Length >= 8 && d[..4].SequenceEqual("CSVD"u8);

    public static MapGradationFile FromBinary(ReadOnlySpan<byte> d)
    {
        if (!IsMapGradation(d)) throw new InvalidDataException("not a CSVD file");
        uint count = Be.U32(d, 4);
        if (count != (uint)(d.Length - 8) / EntrySize || (d.Length - 8) % EntrySize != 0) throw new InvalidDataException("CSVD count does not match the file size");
        var f = new MapGradationFile();
        for (int i = 0; i < count; i++)
        {
            int at = 8 + i * EntrySize;
            f.Entries.Add(new MapGradation { Name = Be.Name(d, at, 32), Upper = Be.F32(d, at + 32), Lower = Be.F32(d, at + 36), Color = Be.U32(d, at + 40) });
        }
        return f;
    }

    public byte[] Write()
    {
        var w = new BeWriter();
        w.Bytes("CSVD"u8); w.U32((uint)Entries.Count);
        foreach (MapGradation e in Entries) { w.Name(e.Name, 32); w.F32(e.Upper); w.F32(e.Lower); w.U32(e.Color); }
        return w.ToArray();
    }
}

public sealed class MapGradation
{
    public string Name { get; set; } = "";
    public float Upper { get; set; }
    public float Lower { get; set; }
    /// <summary>RGBA8.</summary>
    public uint Color { get; set; }
    public override string ToString() => $"{Name} {Upper}..{Lower} {Color:X8}";
}

/// <summary><c>mapScrollTex_00.dat</c>: headerless 28-byte records of an id, two floats and 16 further bytes.</summary>
public sealed class MapScrollFile
{
    public const int EntrySize = 28;

    public List<MapScrollEntry> Entries { get; } = [];

    public static MapScrollFile FromBinary(ReadOnlySpan<byte> d)
    {
        if (d.Length % EntrySize != 0) throw new InvalidDataException("map scroll file is not a whole number of records");
        var f = new MapScrollFile();
        for (int at = 0; at < d.Length; at += EntrySize)
        {
            var e = new MapScrollEntry { Id = Be.U32(d, at), Value0 = Be.F32(d, at + 4), Value1 = Be.F32(d, at + 8) };
            d.Slice(at + 12, 16).CopyTo(e.Rest);
            f.Entries.Add(e);
        }
        return f;
    }

    public byte[] Write()
    {
        var w = new BeWriter();
        foreach (MapScrollEntry e in Entries) { w.U32(e.Id); w.F32(e.Value0); w.F32(e.Value1); w.Bytes(e.Rest); }
        return w.ToArray();
    }
}

public sealed class MapScrollEntry
{
    public uint Id { get; set; }
    public float Value0 { get; set; }
    public float Value1 { get; set; }
    public byte[] Rest { get; } = new byte[16];
    public override string ToString() => $"{Id}: {Value0}, {Value1}";
}

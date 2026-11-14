using System.Text;

namespace SkywardDataSharp;

/// <summary>
/// A JSystem JStudio cutscene (<c>.stb</c>): a header and blocks. Object blocks (<c>JCMR</c>, <c>JACT</c>, <c>JMSG</c>,
/// <c>JPTC</c>, <c>JLIT</c>, and the 0xFFFFFFFF control object) hold a command sequence; <c>JFVB</c> holds the
/// function curves those commands use; anything else is kept raw.
/// </summary>
public sealed class StbFile
{
    const int HeaderSize = 0x20;

    public ushort Version { get; set; } = 3;
    /// <summary>The 16 bytes after the block count: the target name (<c>jstudio</c>) and its version.</summary>
    public byte[] Target { get; } = new byte[16];
    public List<StbBlock> Blocks { get; } = [];

    public static bool IsStb(ReadOnlySpan<byte> d) => d.Length >= HeaderSize && d[..4].SequenceEqual("STB\0"u8);

    public static StbFile FromBinary(ReadOnlySpan<byte> d)
    {
        if (!IsStb(d)) throw new InvalidDataException("not an STB file");
        if (Be.U16(d, 4) != 0xFEFF) throw new InvalidDataException("STB is not big-endian");
        if (Be.U32(d, 8) != d.Length) throw new InvalidDataException("STB size field does not match the file");
        var f = new StbFile { Version = Be.U16(d, 6) };
        d.Slice(0x10, 16).CopyTo(f.Target);
        uint count = Be.U32(d, 12);
        int at = HeaderSize;
        for (uint i = 0; i < count; i++)
        {
            Be.Need(d, at + 8, "block header");
            int size = (int)Be.U32(d, at);
            if (size < 8 || size % 4 != 0) throw new InvalidDataException($"block at 0x{at:X} has bad size {size}");
            Be.Need(d, (long)at + size, "block");
            f.Blocks.Add(StbBlock.Read(d.Slice(at, size)));
            at += size;
        }
        if (at != d.Length) throw new InvalidDataException("STB has bytes after its last block");
        return f;
    }

    public byte[] Write()
    {
        var w = new BeWriter();
        w.Bytes("STB\0"u8); w.U16(0xFEFF); w.U16(Version); w.U32(0); w.U32((uint)Blocks.Count); w.Bytes(Target);
        foreach (StbBlock b in Blocks) b.Write(w);
        w.PatchU32(8, (uint)w.Position);
        return w.ToArray();
    }
}

public abstract class StbBlock
{
    /// <summary>The 4-byte block type, as Latin-1 (the control object is "\xFF\xFF\xFF\xFF").</summary>
    public string Type { get; set; } = "";

    internal static StbBlock Read(ReadOnlySpan<byte> b)
    {
        string type = Encoding.Latin1.GetString(b.Slice(4, 4));
        return type switch
        {
            "JFVB" => new StbFvbBlock { Type = type, Fvb = FvbFile.FromBinary(b[8..]) },
            "JCTB" => new StbRawBlock { Type = type, Data = b[8..].ToArray() },
            _ => StbObject.ReadObject(type, b),
        };
    }

    internal void Write(BeWriter w)
    {
        int start = w.Position;
        w.U32(0); w.Bytes(Encoding.Latin1.GetBytes(Type));
        WriteBody(w);
        w.PatchU32(start, (uint)(w.Position - start));
    }

    private protected abstract void WriteBody(BeWriter w);
    public override string ToString() => Type;
}

public sealed class StbRawBlock : StbBlock
{
    public byte[] Data { get; set; } = [];
    private protected override void WriteBody(BeWriter w) => w.Bytes(Data);
}

public sealed class StbFvbBlock : StbBlock
{
    public FvbFile Fvb { get; set; } = new();
    private protected override void WriteBody(BeWriter w) => w.Bytes(Fvb.Write());
}

/// <summary>A cutscene object: a camera, actor, message, particle, light or the control object, with its sequence.</summary>
public sealed class StbObject : StbBlock
{
    public string Id { get; set; } = "";
    public List<StbCommand> Sequence { get; } = [];
    public bool IsControl => Type == "\xFF\xFF\xFF\xFF";

    internal static StbObject ReadObject(string type, ReadOnlySpan<byte> b)
    {
        int idLength = (int)Be.U32(b, 8);
        int at = 12 + ((idLength + 3) & ~3);
        Be.Need(b, at, "object id");
        var o = new StbObject { Type = type, Id = idLength == 0 ? "" : Be.Name(b, 12, idLength) };
        if (idLength != 0 && Encoding.Latin1.GetByteCount(o.Id) + 1 != idLength) throw new InvalidDataException($"object {o.Id} has an odd id length");
        while (at < b.Length)
        {
            uint head = Be.U32(b, at);
            at += 4;
            var c = new StbCommand { Op = (byte)(head >> 24), Value = head & 0xFFFFFF };
            if (c.Op == StbCommand.Paragraph)
            {
                Be.Need(b, (long)at + c.Value, "paragraph");
                c.Items = StbItem.ReadAll(b.Slice(at, (int)c.Value));
                at += (int)c.Value;
            }
            o.Sequence.Add(c);
            if (c.Op == StbCommand.End) break;
        }
        if (at != b.Length) throw new InvalidDataException($"object {o.Id} has bytes after its end command");
        return o;
    }

    private protected override void WriteBody(BeWriter w)
    {
        byte[] id = Id.Length == 0 ? [] : [.. Encoding.Latin1.GetBytes(Id), 0];
        w.U32((uint)id.Length); w.Bytes(id);
        for (int i = id.Length; i % 4 != 0; i++) w.U8(0);
        foreach (StbCommand c in Sequence)
        {
            if (c.Op == StbCommand.Paragraph)
            {
                int start = w.Position;
                w.U32(0);
                StbItem.WriteAll(w, c.Items);
                int length = w.Position - start - 4;
                if (length > 0xFFFFFF) throw new InvalidOperationException("paragraph is too long");
                w.PatchU32(start, (uint)(StbCommand.Paragraph << 24 | length));
            }
            else
            {
                if (c.Value > 0xFFFFFF) throw new InvalidOperationException($"command value 0x{c.Value:X} does not fit in 24 bits");
                w.U32((uint)(c.Op << 24) | c.Value);
            }
        }
    }

    public override string ToString() => IsControl ? $"control {Id}" : $"{Type} {Id}";
}

public sealed class StbCommand
{
    public const byte End = 0x00, Wait = 0x02, Paragraph = 0x80;

    public byte Op { get; set; }
    /// <summary>The 24-bit operand (frames for <see cref="Wait"/>); for a paragraph it is rebuilt from <see cref="Items"/>.</summary>
    public uint Value { get; set; }
    public List<StbItem> Items { get; set; } = [];

    public override string ToString() => Op switch
    {
        End => "end",
        Wait => $"wait {Value}",
        Paragraph => $"paragraph [{string.Join(", ", Items)}]",
        _ => $"op 0x{Op:X2} {Value}",
    };
}

/// <summary>A (u16 size, u16 type, data) item, padded to 4 bytes; used by sequence paragraphs and FVB entries.</summary>
public sealed class StbItem
{
    public ushort Type { get; set; }
    public byte[] Data { get; set; } = [];

    public uint U32(int index) => Be.U32(Data, index * 4);
    public float F32(int index) => Be.F32(Data, index * 4);
    public string Text => Be.Name(Data, 0, Data.Length);

    internal static List<StbItem> ReadAll(ReadOnlySpan<byte> b)
    {
        List<StbItem> items = [];
        int at = 0;
        while (at < b.Length)
        {
            Be.Need(b, at + 4, "item header");
            int size = Be.U16(b, at);
            var item = new StbItem { Type = Be.U16(b, at + 2) };
            Be.Need(b, at + 4 + ((size + 3) & ~3), "item");
            item.Data = b.Slice(at + 4, size).ToArray();
            items.Add(item);
            at += 4 + ((size + 3) & ~3);
        }
        return items;
    }

    internal static void WriteAll(BeWriter w, IEnumerable<StbItem> items)
    {
        foreach (StbItem i in items)
        {
            w.U16(Checked.Count(i.Data.Length, "item bytes")); w.U16(i.Type); w.Bytes(i.Data);
            for (int k = i.Data.Length; k % 4 != 0; k++) w.U8(0);
        }
    }

    public override string ToString() => $"{Type:X4}:{Data.Length}";
}

/// <summary>A JStudio function-value file (<c>FVB</c>): curves and constants that cutscene commands sample.</summary>
public sealed class FvbFile
{
    const int HeaderSize = 0x10;

    public ushort Version { get; set; } = 0x0100;
    public List<FvbEntry> Entries { get; } = [];

    public static FvbFile FromBinary(ReadOnlySpan<byte> d)
    {
        if (d.Length < HeaderSize || !d[..4].SequenceEqual("FVB\0"u8) || Be.U16(d, 4) != 0xFEFF) throw new InvalidDataException("not a big-endian FVB");
        if (Be.U32(d, 8) != d.Length) throw new InvalidDataException("FVB size field does not match its block");
        var f = new FvbFile { Version = Be.U16(d, 6) };
        uint count = Be.U32(d, 12);
        int at = HeaderSize;
        for (uint i = 0; i < count; i++)
        {
            Be.Need(d, at + 12, "FVB entry");
            int size = (int)Be.U32(d, at);
            if (size < 12 || size % 4 != 0) throw new InvalidDataException($"FVB entry at 0x{at:X} has bad size {size}");
            Be.Need(d, (long)at + size, "FVB entry");
            if (Be.U32(d, at + size - 4) != 0) throw new InvalidDataException($"FVB entry at 0x{at:X} is not terminated");
            f.Entries.Add(new FvbEntry { Type = Be.U16(d, at + 4), Unknown = Be.U16(d, at + 6), Items = StbItem.ReadAll(d.Slice(at + 8, size - 12)) });
            at += size;
        }
        if (at != d.Length) throw new InvalidDataException("FVB has bytes after its last entry");
        return f;
    }

    public byte[] Write()
    {
        var w = new BeWriter();
        w.Bytes("FVB\0"u8); w.U16(0xFEFF); w.U16(Version); w.U32(0); w.U32((uint)Entries.Count);
        foreach (FvbEntry e in Entries)
        {
            int start = w.Position;
            w.U32(0); w.U16(e.Type); w.U16(e.Unknown);
            StbItem.WriteAll(w, e.Items);
            w.U32(0);
            w.PatchU32(start, (uint)(w.Position - start));
        }
        w.PatchU32(8, (uint)w.Position);
        return w.ToArray();
    }
}

public sealed class FvbEntry
{
    /// <summary>The function kind (2 constant, 6 and others for curves and lists, by JStudio's numbering).</summary>
    public ushort Type { get; set; }
    public ushort Unknown { get; set; }
    public List<StbItem> Items { get; set; } = [];
    public override string ToString() => $"type {Type} [{string.Join(", ", Items)}]";
}

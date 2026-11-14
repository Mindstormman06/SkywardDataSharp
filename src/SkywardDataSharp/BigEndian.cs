using System.Buffers.Binary;
using System.Text;

namespace SkywardDataSharp;

internal static class Be
{
    public static ushort U16(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadUInt16BigEndian(d[at..]);
    public static uint U32(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadUInt32BigEndian(d[at..]);
    public static int S32(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadInt32BigEndian(d[at..]);
    public static float F32(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadSingleBigEndian(d[at..]);

    /// <summary>A fixed-width, NUL-padded name.</summary>
    public static string Name(ReadOnlySpan<byte> d, int at, int width)
    {
        ReadOnlySpan<byte> s = d.Slice(at, width);
        int end = s.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? s : s[..end]);
    }

    /// <summary>A NUL-terminated string at an offset.</summary>
    public static string CString(ReadOnlySpan<byte> d, int at)
    {
        if ((uint)at >= (uint)d.Length) throw new InvalidDataException($"string offset 0x{at:X} is outside the file");
        int end = d[at..].IndexOf((byte)0);
        if (end < 0) throw new InvalidDataException($"string at 0x{at:X} is not terminated");
        return Encoding.Latin1.GetString(d.Slice(at, end));
    }

    public static void Need(ReadOnlySpan<byte> d, long end, string what)
    {
        if (end > d.Length) throw new InvalidDataException($"{what} runs past the end of the file");
    }
}

internal sealed class BeWriter
{
    readonly MemoryStream _s = new();
    readonly byte[] _b = new byte[8];

    public int Position => (int)_s.Position;

    public void U8(byte v) => _s.WriteByte(v);
    public void U16(ushort v) { BinaryPrimitives.WriteUInt16BigEndian(_b, v); _s.Write(_b, 0, 2); }
    public void U32(uint v) { BinaryPrimitives.WriteUInt32BigEndian(_b, v); _s.Write(_b, 0, 4); }
    public void S32(int v) { BinaryPrimitives.WriteInt32BigEndian(_b, v); _s.Write(_b, 0, 4); }
    public void F32(float v) { BinaryPrimitives.WriteSingleBigEndian(_b, v); _s.Write(_b, 0, 4); }
    public void Bytes(ReadOnlySpan<byte> v) => _s.Write(v);

    public void Name(string name, int width)
    {
        byte[] b = Encoding.Latin1.GetBytes(name);
        if (b.Length > width) throw new InvalidOperationException($"'{name}' is longer than {width} bytes");
        _s.Write(b);
        for (int i = b.Length; i < width; i++) _s.WriteByte(0);
    }

    public void CString(string s) { _s.Write(Encoding.Latin1.GetBytes(s)); _s.WriteByte(0); }

    public void PatchU32(int at, uint v)
    {
        long p = _s.Position;
        _s.Position = at;
        U32(v);
        _s.Position = p;
    }

    public byte[] ToArray() => _s.ToArray();
}

internal static class Checked
{
    public static ushort Count(int n, string what) =>
        n <= ushort.MaxValue ? (ushort)n : throw new InvalidOperationException($"too many {what} ({n})");
}

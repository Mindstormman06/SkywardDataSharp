namespace SkywardDataSharp;

/// <summary>
/// A map-edge setting (<c>.bedge</c>, <c>BEDG</c>): a mode byte, six floats and an RGBA colour, in a fixed 0x50-byte
/// file. Edited in place.
/// </summary>
public sealed class BedgeFile
{
    public const int FloatCount = 6;

    public byte[] Data { get; }

    BedgeFile(byte[] data) => Data = data;

    public static bool IsBedge(ReadOnlySpan<byte> d) => d.Length >= 0x30 && d[..4].SequenceEqual("BEDG"u8);

    public static BedgeFile FromBinary(byte[] data)
    {
        if (!IsBedge(data)) throw new InvalidDataException("not a BEDG file");
        if (Be.U32(data, 4) != data.Length) throw new InvalidDataException("BEDG size field does not match the file");
        return new BedgeFile(data);
    }

    public byte Mode { get => Data[0x10]; set => Data[0x10] = value; }
    public float Float(int index) => Be.F32(Data, 0x14 + Check(index) * 4);
    public void SetFloat(int index, float value) => System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(Data.AsSpan(0x14 + Check(index) * 4), value);
    /// <summary>RGBA8.</summary>
    public uint Color { get => Be.U32(Data, 0x2C); set => System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(Data.AsSpan(0x2C), value); }

    public byte[] Write() => Data;

    static int Check(int index) => (uint)index < FloatCount ? index : throw new ArgumentOutOfRangeException(nameof(index));
}

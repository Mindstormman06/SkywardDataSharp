namespace SkywardDataSharp;

/// <summary>
/// <c>camparam.dat</c>: a float pool, a u16 per parameter set, tagged parameter sets (<c>CH00</c>, <c>SU00</c>, ...)
/// that start in the float pool, and named camera styles that index the sets.
/// </summary>
public sealed class CameraParamFile
{
    const int HeaderSize = 0x0C, SetSize = 10, StyleSize = 36;

    public ushort Unknown { get; set; }
    public List<float> Floats { get; } = [];
    public List<ushort> Flags { get; } = [];
    public List<CameraParamSet> Sets { get; } = [];
    public List<CameraStyle> Styles { get; } = [];

    public static bool IsCameraParam(ReadOnlySpan<byte> d) =>
        d.Length >= HeaderSize && d[0] == 'w' && d[1] == 'Z' && !ZevFile.IsZev(d) &&
        HeaderSize + Be.U16(d, 4) * 4 + Be.U16(d, 6) * 2 + Be.U16(d, 8) * SetSize + Be.U16(d, 10) * StyleSize == d.Length;

    /// <summary>The floats a set owns: from its start to the next set's start (or the end of the pool).</summary>
    public ArraySegment<float> FloatsOf(int set)
    {
        int start = Sets[set].FloatStart;
        int end = Sets.Select(s => (int)s.FloatStart).Where(s => s > start).DefaultIfEmpty(Floats.Count).Min();
        return new ArraySegment<float>([.. Floats], start, end - start);
    }

    public static CameraParamFile FromBinary(ReadOnlySpan<byte> d)
    {
        if (!IsCameraParam(d)) throw new InvalidDataException("not a camera parameter file");
        var f = new CameraParamFile { Unknown = Be.U16(d, 2) };
        int nFloat = Be.U16(d, 4), nFlag = Be.U16(d, 6), nSet = Be.U16(d, 8), nStyle = Be.U16(d, 10);
        int at = HeaderSize;
        for (int i = 0; i < nFloat; i++, at += 4) f.Floats.Add(Be.F32(d, at));
        for (int i = 0; i < nFlag; i++, at += 2) f.Flags.Add(Be.U16(d, at));
        for (int i = 0; i < nSet; i++, at += SetSize)
        {
            if (Be.U16(d, at + 8) != i) throw new InvalidDataException($"parameter set {i} is out of order");
            var s = new CameraParamSet { Tag = Be.Name(d, at, 4), Type = Be.U16(d, at + 4), FloatStart = Be.U16(d, at + 6) };
            if (s.FloatStart > nFloat) throw new InvalidDataException($"parameter set {s.Tag} starts past the float pool");
            f.Sets.Add(s);
        }
        for (int i = 0; i < nStyle; i++, at += StyleSize)
        {
            var s = new CameraStyle { Name = Be.Name(d, at, 16) };
            for (int k = 0; k < CameraStyle.ValueCount; k++) s.Values[k] = Be.U16(d, at + 16 + k * 2);
            f.Styles.Add(s);
        }
        return f;
    }

    public byte[] Write()
    {
        var w = new BeWriter();
        w.U8((byte)'w'); w.U8((byte)'Z'); w.U16(Unknown);
        w.U16(Checked.Count(Floats.Count, "floats")); w.U16(Checked.Count(Flags.Count, "flags"));
        w.U16(Checked.Count(Sets.Count, "sets")); w.U16(Checked.Count(Styles.Count, "styles"));
        foreach (float v in Floats) w.F32(v);
        foreach (ushort v in Flags) w.U16(v);
        for (int i = 0; i < Sets.Count; i++) { w.Name(Sets[i].Tag, 4); w.U16(Sets[i].Type); w.U16(Sets[i].FloatStart); w.U16((ushort)i); }
        foreach (CameraStyle s in Styles) { w.Name(s.Name, 16); foreach (ushort v in s.Values) w.U16(v); }
        return w.ToArray();
    }
}

public sealed class CameraParamSet
{
    public string Tag { get; set; } = "";
    public ushort Type { get; set; }
    public ushort FloatStart { get; set; }
    public override string ToString() => $"{Tag} (type {Type}) @ {FloatStart}";
}

public sealed class CameraStyle
{
    public const int ValueCount = 10;
    public string Name { get; set; } = "";
    /// <summary>Ten u16s; most are parameter-set indices, 0xFFFF for none.</summary>
    public ushort[] Values { get; } = new ushort[ValueCount];
    public override string ToString() => Name;
}

using System.Text;

namespace SkywardDataSharp;

public enum ZevValueKind : ushort { Int = 0, Float = 1, String = 2 }

/// <summary>
/// An event list (<c>zev.dat</c>): named events, each naming a range of staff (Camera, Link, @player, ...); every staff
/// member has cuts, every cut has 4-character actions (<c>move</c>, <c>talk</c>, ...), and every action has
/// 4-character properties holding ints, floats or a string.
/// </summary>
public sealed class ZevFile
{
    const int HeaderSize = 0x14, EventSize = 0x28, StaffSize = 0x28, CutSize = 0x1C, ActionSize = 0x0C, ParamSize = 0x0C;

    public List<ZevEvent> Events { get; } = [];
    /// <summary>Every staff member, in file order. Events refer to them by <see cref="ZevEvent.StaffStart"/>.</summary>
    public List<ZevStaff> Staff { get; } = [];

    public static bool IsZev(ReadOnlySpan<byte> data) =>
        data.Length >= HeaderSize && data[0] == 'w' && data[1] == 'Z' && data[12] == 'E' && data[13] == 'v';

    public IEnumerable<ZevStaff> StaffOf(ZevEvent e) => Staff.Skip(e.StaffStart).Take(e.StaffCount);

    public static ZevFile FromBinary(ReadOnlySpan<byte> d)
    {
        if (!IsZev(d)) throw new InvalidDataException("not an event list (wZ/Ev)");
        int nEvent = Be.U16(d, 2), nStaff = Be.U16(d, 4), nCut = Be.U16(d, 6), nAction = Be.U16(d, 8), nParam = Be.U16(d, 10);
        int nInt = Be.U16(d, 14), nFloat = Be.U16(d, 16), nString = Be.U16(d, 18);

        int staffAt = HeaderSize + nEvent * EventSize, cutAt = staffAt + nStaff * StaffSize, actionAt = cutAt + nCut * CutSize;
        int paramAt = actionAt + nAction * ActionSize, intAt = paramAt + nParam * ParamSize, floatAt = intAt + nInt * 4;
        int stringAt = floatAt + nFloat * 4;
        Be.Need(d, stringAt + nString, "event tables");

        var f = new ZevFile();
        for (int i = 0; i < nEvent; i++)
        {
            int at = HeaderSize + i * EventSize;
            var e = new ZevEvent
            {
                Name = Be.Name(d, at, 32), Unknown0 = Be.U16(d, at + 32), Unknown1 = Be.U16(d, at + 34),
                StaffStart = Be.U16(d, at + 36), StaffCount = Be.U16(d, at + 38),
            };
            if (e.StaffStart + e.StaffCount > nStaff) throw new InvalidDataException($"event {e.Name} points past the staff table");
            f.Events.Add(e);
        }

        int nextCut = 0, nextAction = 0, nextParam = 0;
        int[] pool = new int[3];
        for (int i = 0; i < nStaff; i++)
        {
            int at = staffAt + i * StaffSize;
            var s = new ZevStaff { Name = Be.Name(d, at, 32), Unknown0 = Be.U16(d, at + 32), Unknown1 = Be.U16(d, at + 34) };
            Expect(Be.U16(d, at + 36), ref nextCut, Be.U16(d, at + 38), nCut, "cut");
            for (int c = nextCut - Be.U16(d, at + 38); c < nextCut; c++)
            {
                int ca = cutAt + c * CutSize;
                var cut = new ZevCut { Name = Be.Name(d, ca, 16), Link = Be.U16(d, ca + 16), Reserved = Be.U16(d, ca + 22) };
                if (Be.U16(d, ca + 18) != i || Be.U16(d, ca + 20) != s.Cuts.Count) throw new InvalidDataException($"cut {c} has the wrong parent");
                int actions = Be.U16(d, ca + 26);
                Expect(Be.U16(d, ca + 24), ref nextAction, actions, nAction, "action");
                for (int a = nextAction - actions; a < nextAction; a++)
                {
                    int aa = actionAt + a * ActionSize;
                    var action = new ZevAction { Tag = Tag(d, aa), Id = Be.U16(d, aa + 4) };
                    if (Be.U16(d, aa + 6) != c) throw new InvalidDataException($"action {a} has the wrong parent");
                    int ps = Be.U16(d, aa + 10);
                    Expect(Be.U16(d, aa + 8), ref nextParam, ps, nParam, "property");
                    for (int p = nextParam - ps; p < nextParam; p++)
                    {
                        int pa = paramAt + p * ParamSize;
                        var kind = (ZevValueKind)Be.U16(d, pa + 6);
                        int start = Be.U16(d, pa + 8), count = Be.U16(d, pa + 10);
                        var prop = new ZevParam { Tag = Tag(d, pa), Id = Be.U16(d, pa + 4), Kind = kind };
                        switch (kind)
                        {
                            case ZevValueKind.Int:
                                Expect(start, ref pool[0], count, nInt, "int");
                                prop.Ints = new int[count];
                                for (int k = 0; k < count; k++) prop.Ints[k] = Be.S32(d, intAt + (start + k) * 4);
                                break;
                            case ZevValueKind.Float:
                                Expect(start, ref pool[1], count, nFloat, "float");
                                prop.Floats = new float[count];
                                for (int k = 0; k < count; k++) prop.Floats[k] = Be.F32(d, floatAt + (start + k) * 4);
                                break;
                            case ZevValueKind.String:
                                Expect(start, ref pool[2], count, nString, "string");
                                prop.StringData = d.Slice(stringAt + start, count).ToArray();
                                break;
                            default:
                                throw new InvalidDataException($"property {p} has unknown kind {(ushort)kind}");
                        }
                        action.Params.Add(prop);
                    }
                    cut.Actions.Add(action);
                }
                s.Cuts.Add(cut);
            }
            f.Staff.Add(s);
        }
        if (nextCut != nCut || nextAction != nAction || nextParam != nParam || pool[0] != nInt || pool[1] != nFloat || pool[2] != nString)
            throw new InvalidDataException("event tables have entries no staff member owns");
        return f;
    }

    public byte[] Write()
    {
        List<ZevCut> cuts = Staff.SelectMany(s => s.Cuts).ToList();
        List<ZevAction> actions = cuts.SelectMany(c => c.Actions).ToList();
        List<ZevParam> props = actions.SelectMany(a => a.Params).ToList();

        var w = new BeWriter();
        w.U8((byte)'w'); w.U8((byte)'Z');
        w.U16(Checked.Count(Events.Count, "events")); w.U16(Checked.Count(Staff.Count, "staff"));
        w.U16(Checked.Count(cuts.Count, "cuts")); w.U16(Checked.Count(actions.Count, "actions")); w.U16(Checked.Count(props.Count, "properties"));
        w.U8((byte)'E'); w.U8((byte)'v');
        w.U16(Checked.Count(props.Sum(p => p.Kind == ZevValueKind.Int ? p.Ints.Length : 0), "ints"));
        w.U16(Checked.Count(props.Sum(p => p.Kind == ZevValueKind.Float ? p.Floats.Length : 0), "floats"));
        w.U16(Checked.Count(props.Sum(p => p.Kind == ZevValueKind.String ? p.StringData.Length : 0), "string bytes"));

        foreach (ZevEvent e in Events)
        {
            w.Name(e.Name, 32); w.U16(e.Unknown0); w.U16(e.Unknown1); w.U16(e.StaffStart); w.U16(e.StaffCount);
        }
        int next = 0;
        foreach (ZevStaff s in Staff)
        {
            w.Name(s.Name, 32); w.U16(s.Unknown0); w.U16(s.Unknown1); w.U16((ushort)next); w.U16((ushort)s.Cuts.Count);
            next += s.Cuts.Count;
        }
        next = 0;
        for (int i = 0; i < Staff.Count; i++)
            for (int c = 0; c < Staff[i].Cuts.Count; c++)
            {
                ZevCut cut = Staff[i].Cuts[c];
                w.Name(cut.Name, 16); w.U16(cut.Link); w.U16((ushort)i); w.U16((ushort)c); w.U16(cut.Reserved);
                w.U16((ushort)next); w.U16((ushort)cut.Actions.Count);
                next += cut.Actions.Count;
            }
        next = 0;
        for (int c = 0; c < cuts.Count; c++)
            foreach (ZevAction a in cuts[c].Actions)
            {
                w.Bytes(TagBytes(a.Tag)); w.U16(a.Id); w.U16((ushort)c); w.U16((ushort)next); w.U16((ushort)a.Params.Count);
                next += a.Params.Count;
            }
        int[] pool = new int[3];
        foreach (ZevParam p in props)
        {
            int count = p.Kind switch
            {
                ZevValueKind.Int => p.Ints.Length,
                ZevValueKind.Float => p.Floats.Length,
                ZevValueKind.String => p.StringData.Length,
                _ => throw new InvalidOperationException($"property {p.Tag} has unknown kind {p.Kind}"),
            };
            w.Bytes(TagBytes(p.Tag)); w.U16(p.Id); w.U16((ushort)p.Kind); w.U16((ushort)pool[(int)p.Kind]); w.U16((ushort)count);
            pool[(int)p.Kind] += count;
        }
        foreach (ZevParam p in props) if (p.Kind == ZevValueKind.Int) foreach (int v in p.Ints) w.S32(v);
        foreach (ZevParam p in props) if (p.Kind == ZevValueKind.Float) foreach (float v in p.Floats) w.F32(v);
        foreach (ZevParam p in props) if (p.Kind == ZevValueKind.String) w.Bytes(p.StringData);
        return w.ToArray();
    }

    static void Expect(int start, ref int next, int count, int total, string what)
    {
        if (start != next || start + count > total) throw new InvalidDataException($"{what} range {start}+{count} is out of order");
        next = start + count;
    }

    static string Tag(ReadOnlySpan<byte> d, int at) => Encoding.Latin1.GetString(d.Slice(at, 4));

    static byte[] TagBytes(string tag)
    {
        byte[] b = Encoding.Latin1.GetBytes(tag);
        return b.Length == 4 ? b : throw new InvalidOperationException($"tag '{tag}' is not 4 characters");
    }
}

public sealed class ZevEvent
{
    public string Name { get; set; } = "";
    public ushort Unknown0 { get; set; }
    public ushort Unknown1 { get; set; }
    public ushort StaffStart { get; set; }
    public ushort StaffCount { get; set; }
    public override string ToString() => Name;
}

public sealed class ZevStaff
{
    public string Name { get; set; } = "";
    public ushort Unknown0 { get; set; }
    public ushort Unknown1 { get; set; }
    public List<ZevCut> Cuts { get; } = [];
    public override string ToString() => Name;
}

public sealed class ZevCut
{
    public string Name { get; set; } = "";
    /// <summary>0xFFFF in most cuts; otherwise a small index.</summary>
    public ushort Link { get; set; } = 0xFFFF;
    public ushort Reserved { get; set; }
    public List<ZevAction> Actions { get; } = [];
    public override string ToString() => Name;
}

public sealed class ZevAction
{
    public string Tag { get; set; } = "    ";
    public ushort Id { get; set; }
    public List<ZevParam> Params { get; } = [];
    public ZevParam? this[string tag] => Params.FirstOrDefault(p => p.Tag == tag);
    public override string ToString() => Tag;
}

public sealed class ZevParam
{
    public string Tag { get; set; } = "    ";
    public ushort Id { get; set; }
    public ZevValueKind Kind { get; set; }
    public int[] Ints { get; set; } = [];
    public float[] Floats { get; set; } = [];
    /// <summary>The string's bytes as stored, terminator included.</summary>
    public byte[] StringData { get; set; } = [];

    public string Text
    {
        get { int end = Array.IndexOf(StringData, (byte)0); return Encoding.Latin1.GetString(StringData, 0, end < 0 ? StringData.Length : end); }
        set => StringData = [.. Encoding.Latin1.GetBytes(value), 0];
    }

    public override string ToString() => Kind switch
    {
        ZevValueKind.Int => $"{Tag} = {string.Join(", ", Ints)}",
        ZevValueKind.Float => $"{Tag} = {string.Join(", ", Floats)}",
        _ => $"{Tag} = \"{Text}\"",
    };
}

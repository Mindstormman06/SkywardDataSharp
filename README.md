# SkywardDataSharp

*Skyward Sword HD*'s game data: event lists (`zev.dat`), camera parameters (`camparam.dat`), the navi table
(`navi_table.dat`), the map tables (`MapGradation.dat`, `mapScrollTex_00.dat`), JStudio cutscenes (`.stb`), collision
polygon codes (`.plc`), enemy parameter sheets (`.entxt`) and map-edge settings (`.bedge`).

```csharp
ZevFile zev = ZevFile.FromBinary(arc["dat/zev.dat"]!.Data);
foreach (ZevEvent e in zev.Events)
    foreach (ZevStaff staff in zev.StaffOf(e))                         // Camera, Link, Director, @player, ...
        foreach (ZevCut cut in staff.Cuts)
            foreach (ZevAction action in cut.Actions)                  // move, fixd, talk, wait, ...
                Console.WriteLine($"{e}/{staff}/{cut}/{action}: {string.Join("; ", action.Params)}");

zev.Staff[0].Cuts[0].Actions[0]["fovy"]!.Floats[0] = 50;
zev.Staff[0].Cuts[0].Actions[0].Params.Add(new ZevParam { Tag = "tgt ", Kind = ZevValueKind.String, Text = "Link" });
byte[] packed = zev.Write();
```

An event list (`wZ`/`Ev`) is a tree: named events, each naming a range of staff, then cuts, 4-character actions and
4-character properties. Each property's values are ints, floats or a string, and live in three pools after the
tables. The writer rebuilds every table and pool from the tree, so properties and nodes can be added or removed
freely. Events point at staff by `StaffStart`/`StaffCount`, because the game's ranges are not in staff order.

`CameraParamFile` is a float pool, a u16 per parameter set, tagged sets (`CH00`, `SU00`, ...) with a type and a start
in the pool, and 235 named camera styles (`Dungeon`, `Field`, `LadderTube`, ...) of ten u16 indices. `NaviTable`
(`V001`) has two tables of u16 pairs, named labels and named 24-byte entries, with the names rebuilt into the string
table. `MapGradationFile` (`CSVD`) holds a name, two heights and a colour per map, and `MapScrollFile` holds five
28-byte records. Fields whose meaning isn't known are named `Unknown`/`Value` and kept as-is.

```csharp
StbFile demo = StbFile.FromBinary(arc["dat/demo01.stb"]!.Data);
foreach (StbObject obj in demo.Blocks.OfType<StbObject>())           // JCMR $cam, JACT $act03, JMSG $msg, control ...
    foreach (StbCommand c in obj.Sequence) Console.WriteLine($"{obj}: {c}");   // wait 90, paragraph [0858:18], end
FvbFile curves = demo.Blocks.OfType<StbFvbBlock>().First().Fvb;
```

A cutscene is JStudio's STB: object blocks with a command sequence (op 0x00 end, 0x02 wait, 0x04, and 0x80
paragraphs of typed items), a `JFVB` block holding the `FVB` function curves the commands sample, and `JCTB` kept raw.
Items are kept as their type and bytes, with `U32`, `F32` and `Text` readers; paragraph lengths, block sizes and padding
are rebuilt on write.

`PlcFile` is `SPLC`: five u32 bitfields per collision code, with `Bits`/`SetBits` helpers, padded to 0x20 with 0xFF.
`EnemyTextFile` reads the Shift-JIS CSV sheets into sections of rows of cells (the first section is the id and name,
e.g. `2,デクババ`). `BedgeFile` edits a `BEDG` file's mode, six floats and colour in place.

All 752 distinct files in the dump rebuild to the same bytes: 137 event lists, 114 cutscenes (2,231 objects, 25,285
commands, 10,075 FVB functions), 481 polygon-code tables, 14 enemy sheets, 2 map edges and one of each other `.dat`.
Event lists still round-trip after adding a property and lengthening a string, and cutscenes after changing a wait and
lengthening a message id.

## Build

```bash
dotnet build SkywardDataSharp.slnx -c Release
```

## Licence

AGPL-3.0-or-later. See [license.md](license.md).

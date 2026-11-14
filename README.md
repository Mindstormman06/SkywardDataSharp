# SkywardDataSharp

*Skyward Sword HD*'s game data `.dat` files: event lists (`zev.dat`), camera parameters (`camparam.dat`), the navi
table (`navi_table.dat`) and the map tables (`MapGradation.dat`, `mapScrollTex_00.dat`).

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

All 141 distinct `.dat` files in the dump (137 event lists, plus one of each other kind) rebuild to the same bytes,
and event lists still round-trip after adding a property and lengthening a string.

## Build

```bash
dotnet build SkywardDataSharp.slnx -c Release
```

## Licence

AGPL-3.0-or-later. See [license.md](license.md).

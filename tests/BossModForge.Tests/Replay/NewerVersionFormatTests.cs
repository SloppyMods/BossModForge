using System.IO.Compression;
using System.Linq;
using System.Text;
using BossModForge.Core.Replay;
using Xunit;

namespace BossModForge.Tests.Replay;

// Synthetic v25-v31 fixtures. Field layouts mirror ReplayParserLog.cs at vbm 6e410ef8 / bmr 577675d8
// (docs/UPSTREAM.md §12); payloads are hand-written so a single wrong byte desyncs the binary stream.
public sealed class NewerVersionFormatTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 20, 0, 0, DateTimeKind.Unspecified);

    private sealed class Bin
    {
        private readonly MemoryStream _ms = new();
        public readonly BinaryWriter W;

        public Bin(int version)
        {
            W = new BinaryWriter(_ms, Encoding.UTF8, leaveOpen: true);
            Tag("VER ");
            W.Write(version);
            W.Write(10_000_000UL);
            W.Write("2026.10.06.0000.0000");
            W.Write(Start.Ticks);
        }

        public Bin Tag(string four)
        {
            W.Write(Core.Replay.Tag.Parse(four).Value);
            return this;
        }

        public Bin Frame(ulong qpc, uint index)
        {
            Tag("FRAM");
            W.Write(16.6); W.Write(0UL); W.Write(0UL);
            W.Write(qpc); W.Write(index); W.Write(0.016f); W.Write(0.016f); W.Write(1f); W.Write(0f);
            return this;
        }

        public Bin ActorCreate(ulong id, uint layoutId, int? renderflags)
        {
            Tag("ACT+");
            W.Write(id); W.Write(0x233Cu); W.Write(1); W.Write(layoutId); W.Write("Boss"); W.Write(12345u);
            W.Write((ushort)ActorType.Enemy); W.Write((byte)Class.None); W.Write(100);
            W.Write(1f); W.Write(2f); W.Write(3f); W.Write(0.5f); W.Write(5f);
            W.Write(100u); W.Write(100u); W.Write(0u); W.Write(0u); W.Write(10000u);
            W.Write(true); W.Write(false); W.Write(0UL); W.Write(0u);
            if (renderflags is { } rf)
                W.Write(rf);
            return this;
        }

        public Bin Party(int slot, ulong cid, ulong iid, string? name)
        {
            Tag("PAR "); W.Write(slot); W.Write(cid); W.Write(iid); W.Write(false);
            if (name is not null)
                W.Write(name);
            return this;
        }

        public Bin Zone() { Tag("ZONE"); W.Write((ushort)1234); W.Write((ushort)826); return this; }

        public Stream Open(bool brotli)
        {
            W.Flush();
            var outMs = new MemoryStream();
            if (brotli)
            {
                outMs.Write(BitConverter.GetBytes(Core.Replay.Tag.Parse("BLCB").Value));
                using (var bs = new BrotliStream(outMs, CompressionLevel.Fastest, leaveOpen: true))
                    bs.Write(_ms.ToArray());
            }
            else
            {
                outMs.Write(BitConverter.GetBytes(Core.Replay.Tag.Parse("BLOG").Value));
                outMs.Write(_ms.ToArray());
            }
            outMs.Position = 0;
            return outMs;
        }
    }

    // Every post-v24 vbm tag with a well-formed payload, in upstream reader field order.
    private static void WriteAllNewVbmTags(Bin b, int version)
    {
        var w = b.W;
        b.Tag("PVP+"); b.Tag("PVP-"); b.Tag("FLY+"); b.Tag("FLY-");
        b.Tag("LEME"); w.Write((byte)1); w.Write((byte)2); w.Write(3); w.Write(new byte[] { 9, 8, 7 });
        b.Tag("SGN+"); w.Write((byte)1); w.Write(0x1000UL);
        b.Tag("SGN-"); w.Write((byte)1);
        b.Tag("ARO+"); w.Write(0x1000UL); w.Write(1.5f);
        b.Tag("ARO-"); w.Write(0x1000UL);
        b.Tag("VFX "); w.Write(0x1000UL); w.Write(77u); w.Write(0x10UL);
        b.Tag("PATS"); w.Write(0x1000UL); w.Write(2); w.Write(0x1000UL); w.Write((ushort)5); w.Write(0x10UL); w.Write((ushort)6);
        b.Tag("AVIS"); w.Write(0x1000UL); w.Write((ushort)'V');
        b.Tag("ALGI"); w.Write(1);
        b.Tag("CBST"); w.Write((byte)3); w.Write((byte)1); w.Write((byte)2); w.Write((byte)3);
        b.Tag("CCRU"); w.Write((byte)2); w.Write((ushort)10); w.Write((ushort)20);
        b.Tag("CHOC"); w.Write(0x2000UL); w.Write((byte)1); w.Write(30f); if (version >= 30) w.Write(true);
        b.Tag("HATE"); w.Write(0x1000UL); w.Write(2); w.Write(0x10UL); w.Write(100); w.Write(0x11UL); w.Write(50);
        b.Tag("CLPR"); w.Write(1f); w.Write(2f); w.Write(3f); w.Write(4f);
        b.Tag("INVT"); w.Write(1u); w.Write(2u);
        b.Tag("FLOS"); w.Write(3u); w.Write(0x1000UL);
        b.Tag("IPCX"); for (var i = 0; i < 5; i++) w.Write((uint)i);
        b.Tag("RTT "); w.Write(0.05f);
        b.Tag("RPRE"); var json = Encoding.UTF8.GetBytes("[]"); w.Write(json.Length); w.Write(json);
        b.Tag("RMOD"); w.Write((byte)1);
        b.Tag("RROL"); for (var i = 0; i < 8; i++) w.Write((byte)i);
        b.Tag("RMOV"); w.Write(0.5f); w.Write(-0.5f);
        b.Tag("RACT"); w.Write(1); w.Write(ActionId.Spell(1).Raw); w.Write(0x1000UL); w.Write(1f); w.Write(2f); w.Write(1f); w.Write(2f); w.Write(3f);
        b.Tag("RPRI"); w.Write(1); w.Write(0x1000UL); w.Write(3); w.Write(1); w.Write(7); w.Write("reason");
        // changed layouts
        b.Tag("CLDA"); w.Write((byte)3); for (var i = 0; i < 3; i++) { w.Write(ActionId.Spell((uint)i).Raw); w.Write((byte)1); w.Write((byte)2); }
        b.Tag("CLAF"); w.Write(5u); w.Write(1f); w.Write(2f); w.Write(3f); w.Write(10f); w.Write((byte)50); w.Write((byte)3); w.Write(0xABCDu);
        b.Tag("DDPG"); for (var i = 0; i < 9; i++) w.Write((byte)i); w.Write(true);
        b.Tag("DDMP"); for (var i = 0; i < 25; i++) w.Write((ushort)0x1FF);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Vbm_V31_Binary_AllNewTags_ParseWithoutDesync(bool brotli)
    {
        var b = new Bin(31).Frame(1000, 1).Zone().ActorCreate(0x1000, 0xABCD, renderflags: null).Party(0, 0xC1, 0x10, name: null);
        WriteAllNewVbmTags(b, 31);
        b.Frame(1000 + 10_000_000, 2).Tag("ACT-").W.Write(0x1000UL);

        var rec = ReplayParser.Parse(b.Open(brotli), "vbm31.log");

        Assert.False(rec.Degraded, string.Join("; ", rec.Issues.Select(i => i.Code + ":" + i.Message)));
        Assert.DoesNotContain(rec.Issues, i => i.Severity >= IssueSeverity.Warning);
        Assert.Equal(31, rec.Meta.Version);
        var create = Assert.Single(rec.Ops.OfType<ActorCreateOp>());
        Assert.Equal(0xABCDu, create.LayoutId);
        Assert.Equal("Boss", create.Name);
        Assert.Equal(0, create.Renderflags);
        Assert.Equal("", Assert.Single(rec.Ops.OfType<PartyModifyOp>()).Name);
        Assert.Single(rec.Ops.OfType<ActorDestroyOp>());
        Assert.Equal(2, rec.Ops.OfType<FrameStartOp>().Count());
        Assert.Contains(rec.Issues, i => i.Code == "LineageDetected" && i.Message.Contains("vbm"));
    }

    [Fact]
    public void Bmr_V29_Binary_RenderflagsAndRflg_DetectedAsBmr()
    {
        var b = new Bin(29).Frame(1000, 1).Zone().ActorCreate(0x1000, 0x55, renderflags: 0).Party(0, 0xC1, 0x10, name: "Player");
        var w = b.W;
        b.Tag("RFLG"); w.Write(0x1000UL); w.Write(0x4000);
        // bmr v29: CLAF objective-npc uint present (gate 29), DDPG/DDMP v29 layouts, CHOC without bool (<30)
        b.Tag("CLAF"); w.Write(5u); w.Write(1f); w.Write(2f); w.Write(3f); w.Write(10f); w.Write((byte)50); w.Write((byte)3); w.Write(7u);
        b.Tag("CHOC"); w.Write(0x2000UL); w.Write((byte)1); w.Write(30f);
        b.ActorCreate(0x1001, 0, renderflags: 0x10);
        b.Frame(1000 + 10_000_000, 2);

        var rec = ReplayParser.Parse(b.Open(brotli: true), "bmr29.log");

        Assert.False(rec.Degraded, string.Join("; ", rec.Issues.Select(i => i.Code + ":" + i.Message)));
        Assert.Contains(rec.Issues, i => i.Code == "LineageDetected" && i.Message.Contains("BossmodReborn"));
        var creates = rec.Ops.OfType<ActorCreateOp>().ToArray();
        Assert.Equal(2, creates.Length);
        Assert.Equal(0x10, creates[1].Renderflags);
        Assert.Equal("Player", Assert.Single(rec.Ops.OfType<PartyModifyOp>()).Name);
        Assert.Equal(2, rec.Ops.OfType<FrameStartOp>().Count());
    }

    [Theory]
    [InlineData(ReplayLineage.Vbm, true)]
    [InlineData(ReplayLineage.Bmr, false)]
    public void V28_Claf_ObjectiveNpcGate_DiffersByLineage(ReplayLineage lineage, bool hasNpc)
    {
        var b = new Bin(28).Frame(1000, 1);
        var w = b.W;
        b.Tag("CLAF"); w.Write(5u); w.Write(1f); w.Write(2f); w.Write(3f); w.Write(10f); w.Write((byte)50); w.Write((byte)3);
        if (hasNpc)
            w.Write(7u);
        b.Zone();

        var rec = ReplayParser.Parse(b.Open(brotli: false), "v28.log", new ParseOptions(Lineage: lineage));

        Assert.False(rec.Degraded);
        Assert.Single(rec.Ops.OfType<ZoneChangeOp>());
    }

    [Fact]
    public void V25_DutyActions_CountPrefixed_V26_LayoutId()
    {
        var b25 = new Bin(25).Frame(1000, 1);
        b25.Tag("CLDA"); b25.W.Write((byte)5);
        for (var i = 0; i < 5; i++) { b25.W.Write(ActionId.Spell((uint)i).Raw); b25.W.Write((byte)0); b25.W.Write((byte)1); }
        b25.Zone();
        var r25 = ReplayParser.Parse(b25.Open(false), "v25.log");
        Assert.False(r25.Degraded);
        Assert.Single(r25.Ops.OfType<ZoneChangeOp>());

        var b26 = new Bin(26).Frame(1000, 1).ActorCreate(0x1000, 0x99, renderflags: null).Zone();
        var r26 = ReplayParser.Parse(b26.Open(false), "v26.log");
        Assert.False(r26.Degraded);
        Assert.Equal(0x99u, Assert.Single(r26.Ops.OfType<ActorCreateOp>()).LayoutId);
        Assert.DoesNotContain(r26.Issues, i => i.Code == "LineageDetected"); // lineages agree below v27
    }

    private static string TextActorCreate(string ts, string? renderflags)
        => $"{ts}|ACT+|00001000|233C|1|ABCD|Boss|12345|0205|None|100|1.000/2.000/3.000|28.648|5.000|100|100|0|0|10000|True|False|00000000|0"
           + (renderflags is null ? "" : "|" + renderflags);

    [Theory]
    [InlineData(31, null, "")]
    [InlineData(29, "16", "Player")]
    public void Text_NewVersions_ActorAndParty(int version, string? renderflags, string partyName)
    {
        const string ts = "2026-10-01T20:00:00.0000000";
        var party = version >= 31 ? $"{ts}|PAR |0|00000000000000C1|00001000|False" : $"{ts}|PAR |0|00000000000000C1|00001000|False|{partyName}";
        var text = string.Join('\n',
            $"{ts}|VER |{version}|10000000|2026.10.06.0000.0000",
            $"{ts}|FRAM|16.600||0000000000000000|0000000000000000|1000|1|0.016|0.016|1|0.000",
            TextActorCreate(ts, renderflags),
            party,
            $"{ts}|CLDA|1|Spell 1 'x'|1|2",
            $"{ts}|AVIS|00001000|86",
            $"{ts}|ACT-|00001000") + "\n";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var rec = ReplayParser.Parse(ms, "t.log");

        Assert.DoesNotContain(rec.Issues, i => i.Severity >= IssueSeverity.Warning);
        var create = Assert.Single(rec.Ops.OfType<ActorCreateOp>());
        Assert.Equal(0xABCDu, create.LayoutId);
        Assert.Equal(renderflags is null ? 0 : 16, create.Renderflags);
        Assert.Equal(partyName, Assert.Single(rec.Ops.OfType<PartyModifyOp>()).Name);
        Assert.Contains(rec.Issues, i => i.Code == "LineageDetected");
    }
}

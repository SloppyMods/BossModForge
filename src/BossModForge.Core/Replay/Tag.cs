using System.Text;

namespace BossModForge.Core.Replay;

public readonly record struct Tag(uint Value) : IComparable<Tag>
{
    public Tag(ReadOnlySpan<byte> fourAscii) : this(BitConverter.ToUInt32(fourAscii))
    {
    }

    public static Tag Parse(string fourChars)
    {
        if (fourChars.Length != 4)
            throw new ArgumentException("Tag must be exactly 4 characters.", nameof(fourChars));
        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = (byte)fourChars[0];
        bytes[1] = (byte)fourChars[1];
        bytes[2] = (byte)fourChars[2];
        bytes[3] = (byte)fourChars[3];
        return new Tag(bytes);
    }

    public override string ToString()
    {
        Span<byte> bytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(bytes, Value);
        return Encoding.ASCII.GetString(bytes);
    }

    public int CompareTo(Tag other) => Value.CompareTo(other.Value);

    public static readonly Tag Ver = Parse("VER ");
    public static readonly Tag Fram = Parse("FRAM");
    public static readonly Tag Umrk = Parse("UMRK");
    public static readonly Tag Rsv = Parse("RSV ");
    public static readonly Tag Zone = Parse("ZONE");
    public static readonly Tag Diru = Parse("DIRU");
    public static readonly Tag Envc = Parse("ENVC");
    public static readonly Tag Slog = Parse("SLOG");
    public static readonly Tag WayPlus = Parse("WAY+");
    public static readonly Tag WayMinus = Parse("WAY-");
    public static readonly Tag ActPlus = Parse("ACT+");
    public static readonly Tag ActMinus = Parse("ACT-");
    public static readonly Tag Name = Parse("NAME");
    public static readonly Tag Clsr = Parse("CLSR");
    public static readonly Tag Move = Parse("MOVE");
    public static readonly Tag Acsz = Parse("ACSZ");
    public static readonly Tag Hp = Parse("HP  ");
    public static readonly Tag AtgPlus = Parse("ATG+");
    public static readonly Tag AtgMinus = Parse("ATG-");
    public static readonly Tag Ally = Parse("ALLY");
    public static readonly Tag DiePlus = Parse("DIE+");
    public static readonly Tag DieMinus = Parse("DIE-");
    public static readonly Tag ComPlus = Parse("COM+");
    public static readonly Tag ComMinus = Parse("COM-");
    public static readonly Tag Nenp = Parse("NENP");
    public static readonly Tag Mdls = Parse("MDLS");
    public static readonly Tag Evts = Parse("EVTS");
    public static readonly Tag Targ = Parse("TARG");
    public static readonly Tag Mntd = Parse("MNTD");
    public static readonly Tag Fora = Parse("FORA");
    public static readonly Tag Teth = Parse("TETH");
    public static readonly Tag TetPlus = Parse("TET+");
    public static readonly Tag TetMinus = Parse("TET-");
    public static readonly Tag CstPlus = Parse("CST+");
    public static readonly Tag CstMinus = Parse("CST-");
    public static readonly Tag CstBang = Parse("CST!");
    public static readonly Tag Er = Parse("ER  ");
    public static readonly Tag StaPlus = Parse("STA+");
    public static readonly Tag StaMinus = Parse("STA-");
    public static readonly Tag StaBang = Parse("STA!");
    public static readonly Tag AiePlus = Parse("AIE+");
    public static readonly Tag AieMinus = Parse("AIE-");
    public static readonly Tag Icon = Parse("ICON");
    public static readonly Tag Esta = Parse("ESTA");
    public static readonly Tag Eanm = Parse("EANM");
    public static readonly Tag Pate = Parse("PATE");
    public static readonly Tag Nyel = Parse("NYEL");
    public static readonly Tag Opnt = Parse("OPNT");
    public static readonly Tag Par = Parse("PAR ");
    public static readonly Tag ParPlus = Parse("PAR+");
    public static readonly Tag ParBang = Parse("PAR!");
    public static readonly Tag ParMinus = Parse("PAR-");
    public static readonly Tag Lb = Parse("LB  ");
    public static readonly Tag Clar = Parse("CLAR");
    public static readonly Tag Clrj = Parse("CLRJ");
    public static readonly Tag CdnPlus = Parse("CDN+");
    public static readonly Tag CdnMinus = Parse("CDN-");
    public static readonly Tag Clal = Parse("CLAL");
    public static readonly Tag Clcb = Parse("CLCB");
    public static readonly Tag Clst = Parse("CLST");
    public static readonly Tag Clmv = Parse("CLMV");
    public static readonly Tag Clcd = Parse("CLCD");
    public static readonly Tag Clda = Parse("CLDA");
    public static readonly Tag Clbh = Parse("CLBH");
    public static readonly Tag Cblu = Parse("CBLU");
    public static readonly Tag Clvl = Parse("CLVL");
    public static readonly Tag Claf = Parse("CLAF");
    public static readonly Tag Cpet = Parse("CPET");
    public static readonly Tag Clft = Parse("CLFT");
    public static readonly Tag Clfd = Parse("CLFD");
    public static readonly Tag Clkv = Parse("CLKV");
    public static readonly Tag Fate = Parse("FATE");
    public static readonly Tag Ddpg = Parse("DDPG");
    public static readonly Tag Ddmp = Parse("DDMP");
    public static readonly Tag Ddpt = Parse("DDPT");
    public static readonly Tag Ddit = Parse("DDIT");
    public static readonly Tag Ddct = Parse("DDCT");
    public static readonly Tag Ddmg = Parse("DDMG");
    public static readonly Tag Ipci = Parse("IPCI");
    public static readonly Tag Ipcs = Parse("IPCS");

    // v25-v31 era tags (vbm 6e410ef8 / bmr 577675d8, docs/UPSTREAM.md §12)
    public static readonly Tag PvpPlus = Parse("PVP+");
    public static readonly Tag PvpMinus = Parse("PVP-");
    public static readonly Tag Leme = Parse("LEME");
    public static readonly Tag SgnPlus = Parse("SGN+");
    public static readonly Tag SgnMinus = Parse("SGN-");
    public static readonly Tag AroPlus = Parse("ARO+");
    public static readonly Tag AroMinus = Parse("ARO-");
    public static readonly Tag Vfx = Parse("VFX ");
    public static readonly Tag Pats = Parse("PATS");
    public static readonly Tag Avis = Parse("AVIS");
    public static readonly Tag Rflg = Parse("RFLG");
    public static readonly Tag Algi = Parse("ALGI");
    public static readonly Tag FlyPlus = Parse("FLY+");
    public static readonly Tag FlyMinus = Parse("FLY-");
    public static readonly Tag Cbst = Parse("CBST");
    public static readonly Tag Ccru = Parse("CCRU");
    public static readonly Tag Choc = Parse("CHOC");
    public static readonly Tag Hate = Parse("HATE");
    public static readonly Tag Clpr = Parse("CLPR");
    public static readonly Tag Invt = Parse("INVT");
    public static readonly Tag Flos = Parse("FLOS");
    public static readonly Tag Ipcx = Parse("IPCX");
    public static readonly Tag Rtt = Parse("RTT ");
    public static readonly Tag Rpre = Parse("RPRE");
    public static readonly Tag Rmod = Parse("RMOD");
    public static readonly Tag Rrol = Parse("RROL");
    public static readonly Tag Rmov = Parse("RMOV");
    public static readonly Tag Ract = Parse("RACT");
    public static readonly Tag Rpri = Parse("RPRI");
}

using System.Collections.Immutable;

namespace BossModForge.Core.Inference.Timeline;

public sealed record AlignmentColumn(ImmutableArray<AnchorEvent> Members)
{
    public AnchorKind Kind
    {
        get
        {
            if (Members.IsDefaultOrEmpty)
                return AnchorKind.Cast;
            var cast = 0;
            var off = 0;
            var on = 0;
            foreach (var m in Members)
            {
                switch (m.Kind)
                {
                    case AnchorKind.Cast: cast++; break;
                    case AnchorKind.TargetableOff: off++; break;
                    default: on++; break;
                }
            }

            if (cast >= off && cast >= on)
                return AnchorKind.Cast;
            if (off >= on)
                return AnchorKind.TargetableOff;
            return AnchorKind.TargetableOn;
        }
    }

    public uint ConsensusAid
    {
        get
        {
            if (Members.IsDefaultOrEmpty)
                return 0;
            var counts = new Dictionary<uint, int>();
            foreach (var m in Members)
            {
                if (m.Kind != AnchorKind.Cast)
                    continue;
                counts[m.Aid] = counts.GetValueOrDefault(m.Aid) + 1;
            }

            if (counts.Count == 0)
                return 0;
            var bestCount = counts.Values.Max();
            return counts.Where(kv => kv.Value == bestCount).Select(kv => kv.Key).Min();
        }
    }

    public int Support => Members.IsDefault ? 0 : Members.Length;
}

public static class ProfileAligner
{
    private const int Scale = 1000;
    private const int NegInf = int.MinValue / 4;

    public static ImmutableArray<AlignmentColumn> AlignInto(
        ImmutableArray<AlignmentColumn> profile,
        ImmutableArray<AnchorEvent> sequence,
        TimelineAlignerOptions options)
        => AlignInto(profile, sequence, options, null, null);

    public static ImmutableArray<AlignmentColumn> AlignInto(
        ImmutableArray<AlignmentColumn> profile,
        ImmutableArray<AnchorEvent> sequence,
        TimelineAlignerOptions options,
        DateTime? sequenceStart,
        IReadOnlyDictionary<int, DateTime>? pullStarts)
    {
        if (sequence.IsDefaultOrEmpty)
            return profile.IsDefault ? [] : profile;
        if (profile.IsDefaultOrEmpty)
            return sequence.Select(a => new AlignmentColumn([a])).ToImmutableArray();

        var n = sequence.Length;
        var m = profile.Length;
        var gSeq = PrecomputeSequenceGaps(sequence, sequenceStart);
        var gCol = PrecomputeColumnGaps(profile, pullStarts);

        var match = new int[n + 1, m + 1];
        var gapSeq = new int[n + 1, m + 1];
        var gapProf = new int[n + 1, m + 1];
        var ptrM = new byte[n + 1, m + 1];
        var ptrS = new byte[n + 1, m + 1];
        var ptrP = new byte[n + 1, m + 1];

        var gapOpen = (int)Math.Round(options.GapOpen * Scale);
        var gapExtend = (int)Math.Round(options.GapExtend * Scale);

        for (var i = 0; i <= n; i++)
        {
            for (var j = 0; j <= m; j++)
            {
                match[i, j] = NegInf;
                gapSeq[i, j] = NegInf;
                gapProf[i, j] = NegInf;
            }
        }

        match[0, 0] = 0;
        for (var i = 1; i <= n; i++)
            gapProf[i, 0] = gapOpen + (i - 1) * gapExtend;
        for (var j = 1; j <= m; j++)
            gapSeq[0, j] = gapOpen + (j - 1) * gapExtend;

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var s = PairScore(sequence[i - 1], profile[j - 1], gSeq[i - 1], gCol[j - 1], options);
                var diag = Best(match[i - 1, j - 1], gapSeq[i - 1, j - 1], gapProf[i - 1, j - 1], out var diagSrc);
                match[i, j] = diag + s;
                ptrM[i, j] = diagSrc;

                var fromMSeq = match[i, j - 1] == NegInf ? NegInf : match[i, j - 1] + gapOpen;
                var fromSSeq = gapSeq[i, j - 1] == NegInf ? NegInf : gapSeq[i, j - 1] + gapExtend;
                gapSeq[i, j] = MaxTie(fromMSeq, fromSSeq, out var seqSrc);
                ptrS[i, j] = seqSrc;

                var fromMProf = match[i - 1, j] == NegInf ? NegInf : match[i - 1, j] + gapOpen;
                var fromPProf = gapProf[i - 1, j] == NegInf ? NegInf : gapProf[i - 1, j] + gapExtend;
                gapProf[i, j] = MaxTie(fromMProf, fromPProf, out var profSrc);
                ptrP[i, j] = profSrc;
            }
        }

        var ops = Traceback(n, m, match, gapSeq, gapProf, ptrM, ptrS, ptrP);
        return Apply(profile, sequence, ops);
    }

    private static double[] PrecomputeSequenceGaps(ImmutableArray<AnchorEvent> sequence, DateTime? sequenceStart)
    {
        var g = new double[sequence.Length];
        for (var i = 0; i < sequence.Length; i++)
        {
            var prev = i == 0 ? sequenceStart ?? sequence[0].T : sequence[i - 1].T;
            g[i] = (sequence[i].T - prev).TotalSeconds;
        }

        return g;
    }

    private static double[] PrecomputeColumnGaps(
        ImmutableArray<AlignmentColumn> profile,
        IReadOnlyDictionary<int, DateTime>? pullStarts)
    {
        var g = new double[profile.Length];
        for (var j = 0; j < profile.Length; j++)
        {
            var gaps = new List<double>();
            foreach (var member in profile[j].Members)
            {
                DateTime prev = member.T;
                var found = false;
                for (var k = j - 1; k >= 0; k--)
                {
                    foreach (var earlier in profile[k].Members)
                    {
                        if (earlier.PullIndex == member.PullIndex)
                        {
                            prev = earlier.T;
                            found = true;
                            break;
                        }
                    }

                    if (found)
                        break;
                }

                if (!found)
                {
                    if (pullStarts is not null && pullStarts.TryGetValue(member.PullIndex, out var start))
                        prev = start;
                    else
                        prev = member.T;
                }

                gaps.Add((member.T - prev).TotalSeconds);
            }

            g[j] = TimelineStats.Median(gaps);
        }

        return g;
    }

    private static int PairScore(
        AnchorEvent anchor,
        AlignmentColumn column,
        double gSeq,
        double gCol,
        TimelineAlignerOptions options)
    {
        var colKind = column.Kind;
        var castVsTargetable = (anchor.Kind == AnchorKind.Cast) != (colKind == AnchorKind.Cast);
        int scaled;
        if (castVsTargetable)
        {
            scaled = -1000 * Scale;
        }
        else
        {
            var isMatch = anchor.Kind == colKind && (anchor.Kind != AnchorKind.Cast || anchor.Aid == column.ConsensusAid);
            scaled = (int)Math.Round((isMatch ? options.Match : options.Mismatch) * Scale);
            var bonus = options.TimingBonusMax * Math.Max(0, 1 - Math.Abs(gSeq - gCol) / options.TimingBonusScaleSeconds);
            scaled += (int)Math.Round(bonus * Scale);
            if (!isMatch && Math.Abs(gSeq - gCol) >= options.TimingBonusScaleSeconds * 3)
                scaled = -1000 * Scale;
        }

        return scaled;
    }

    private static int Best(int m, int s, int p, out byte src)
    {
        var best = m;
        src = 0;
        if (s > best)
        {
            best = s;
            src = 1;
        }

        if (p > best)
        {
            best = p;
            src = 2;
        }

        return best;
    }

    private static int MaxTie(int a, int b, out byte src)
    {
        if (a >= b)
        {
            src = 0;
            return a;
        }

        src = 1;
        return b;
    }

    private static List<byte> Traceback(
        int n,
        int m,
        int[,] match,
        int[,] gapSeq,
        int[,] gapProf,
        byte[,] ptrM,
        byte[,] ptrS,
        byte[,] ptrP)
    {
        var i = n;
        var j = m;
        var state = BestState(match[i, j], gapSeq[i, j], gapProf[i, j]);
        var ops = new List<byte>();
        while (i > 0 || j > 0)
        {
            if (i == 0)
            {
                ops.Add(1);
                j--;
                continue;
            }

            if (j == 0)
            {
                ops.Add(2);
                i--;
                continue;
            }

            switch (state)
            {
                case 0:
                    ops.Add(0);
                    state = ptrM[i, j];
                    i--;
                    j--;
                    break;
                case 1:
                    ops.Add(1);
                    state = ptrS[i, j] == 0 ? (byte)0 : (byte)1;
                    j--;
                    break;
                default:
                    ops.Add(2);
                    state = ptrP[i, j] == 0 ? (byte)0 : (byte)2;
                    i--;
                    break;
            }
        }

        ops.Reverse();
        return ops;
    }

    private static byte BestState(int m, int s, int p)
    {
        var best = m;
        byte state = 0;
        if (s > best)
        {
            best = s;
            state = 1;
        }

        if (p > best)
            state = 2;
        return state;
    }

    private static ImmutableArray<AlignmentColumn> Apply(
        ImmutableArray<AlignmentColumn> profile,
        ImmutableArray<AnchorEvent> sequence,
        List<byte> ops)
    {
        var columns = new List<List<AnchorEvent>>();
        foreach (var col in profile)
            columns.Add([.. col.Members]);

        var i = 0;
        var j = 0;
        foreach (var op in ops)
        {
            switch (op)
            {
                case 0:
                    columns[j].Add(sequence[i]);
                    i++;
                    j++;
                    break;
                case 1:
                    j++;
                    break;
                default:
                    columns.Insert(j, [sequence[i]]);
                    i++;
                    j++;
                    break;
            }
        }

        while (i < sequence.Length)
        {
            columns.Add([sequence[i]]);
            i++;
        }

        return columns.Select(c => new AlignmentColumn(
            c.OrderBy(e => e.PullIndex).ThenBy(e => e.T).ToImmutableArray())).ToImmutableArray();
    }
}

internal static class TimelineStats
{
    public static double Median(List<double> values)
    {
        if (values.Count == 0)
            return 0;
        values.Sort();
        var mid = values.Count / 2;
        if ((values.Count & 1) == 1)
            return values[mid];
        return (values[mid - 1] + values[mid]) / 2.0;
    }

    public static float Median(List<float> values)
    {
        if (values.Count == 0)
            return 0;
        values.Sort();
        var mid = values.Count / 2;
        if ((values.Count & 1) == 1)
            return values[mid];
        return (values[mid - 1] + values[mid]) / 2f;
    }

    public static float Round1(float value)
        => MathF.Round(MathF.Max(0, value), 1, MidpointRounding.AwayFromZero);
}

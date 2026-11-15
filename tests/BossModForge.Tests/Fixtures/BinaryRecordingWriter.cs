using System.IO.Compression;
using System.Numerics;
using BossModForge.Core.Replay;

namespace BossModForge.Tests.Fixtures;

public static class BinaryRecordingWriter
{
    // Text drops zero effect slots, so after a text round trip the effects are compacted
    // to the front. This writer also writes compacted effects (non-zero first, zero-padded)
    // so both encodings yield identical ActionEffects.Raw.
    public static void Write(Stream dest, RecordingBuilder builder, bool brotli)
    {
        if (brotli)
        {
            WriteMagic(dest, "BLCB");
            using var compressed = new BrotliStream(dest, CompressionLevel.Optimal, leaveOpen: true);
            using var writer = new BinaryWriter(compressed);
            WriteBody(writer, builder);
        }
        else
        {
            WriteMagic(dest, "BLOG");
            using var writer = new BinaryWriter(dest, System.Text.Encoding.UTF8, leaveOpen: true);
            WriteBody(writer, builder);
        }
    }

    private static void WriteMagic(Stream dest, string four)
    {
        Span<byte> bytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(bytes, Tag.Parse(four).Value);
        dest.Write(bytes);
    }

    private static void WriteBody(BinaryWriter w, RecordingBuilder builder)
    {
        WriteTag(w, Tag.Ver);
        w.Write(builder.Version);
        w.Write(builder.Qpf);
        w.Write(builder.GameVersion);
        w.Write(builder.Start.Ticks);

        foreach (var entry in builder.Entries)
        {
            if (entry is RecordingBuilder.RawEntry)
                continue;
            if (entry is not RecordingBuilder.OpEntry { Op: var op })
                continue;
            WriteOp(w, op);
        }
        w.Flush();
    }

    private static void WriteOp(BinaryWriter w, Op op)
    {
        WriteTag(w, op.Tag);
        switch (op)
        {
            case FrameStartOp fram:
                w.Write(fram.PrevUpdateTime.TotalMilliseconds);
                w.Write(fram.GaugeLow);
                w.Write(fram.GaugeHigh);
                w.Write(fram.Frame.Qpc);
                w.Write(fram.Frame.Index);
                w.Write(fram.Frame.DurationRaw);
                w.Write(fram.Frame.Duration);
                w.Write(fram.Frame.TickSpeedMultiplier);
                w.Write(fram.CameraAzimuth.Rad);
                break;
            case ZoneChangeOp zone:
                w.Write(zone.Zone);
                w.Write(zone.CfcId);
                break;
            case DirectorUpdateOp dir:
                w.Write(dir.DirectorId);
                w.Write(dir.UpdateId);
                w.Write(dir.Param1);
                w.Write(dir.Param2);
                w.Write(dir.Param3);
                w.Write(dir.Param4);
                break;
            case EnvControlOp env:
                w.Write(env.Index);
                w.Write(env.State);
                break;
            case ActorCreateOp a:
                w.Write(a.InstanceId);
                w.Write(a.Oid);
                w.Write(a.SpawnIndex);
                w.Write(a.Name);
                w.Write(a.NameId);
                w.Write((ushort)a.Type);
                w.Write((byte)a.Class);
                w.Write(a.Level);
                WriteVec(w, a.PosRot.Pos);
                w.Write(a.PosRot.Rot.Rad);
                w.Write(a.HitboxRadius);
                WriteHpMp(w, a.HpMp);
                w.Write(a.IsTargetable);
                w.Write(a.IsAlly);
                w.Write(a.OwnerId);
                w.Write(a.FateId);
                break;
            case ActorDestroyOp destroy:
                w.Write(destroy.InstanceId);
                break;
            case ActorRenameOp n:
                w.Write(n.InstanceId);
                w.Write(n.Name);
                w.Write(n.NameId);
                break;
            case ActorMoveOp m:
                w.Write(m.InstanceId);
                WriteVec(w, m.PosRot.Pos);
                w.Write(m.PosRot.Rot.Rad);
                break;
            case ActorHpMpOp h:
                w.Write(h.InstanceId);
                WriteHpMp(w, h.HpMp);
                break;
            case ActorTargetableOp atg:
                w.Write(atg.InstanceId);
                break;
            case ActorDeadOp dead:
                w.Write(dead.InstanceId);
                break;
            case ActorCombatOp combat:
                w.Write(combat.InstanceId);
                break;
            case ActorTargetOp targ:
                w.Write(targ.InstanceId);
                w.Write(targ.TargetId);
                break;
            case ActorCastStartOp castStart:
                w.Write(castStart.InstanceId);
                w.Write(castStart.Action.Raw);
                w.Write(castStart.TargetId);
                WriteVec(w, castStart.Location);
                w.Write(castStart.ElapsedTime);
                w.Write(castStart.TotalTime);
                w.Write(castStart.Interruptible);
                w.Write(castStart.Rotation.Rad);
                break;
            case ActorCastStopOp castStop:
                w.Write(castStop.InstanceId);
                break;
            case ActorCastEventOp e:
                w.Write(e.InstanceId);
                w.Write(e.Action.Raw);
                w.Write(e.MainTargetId);
                w.Write(e.AnimationLock);
                w.Write(e.MaxTargets);
                WriteVec(w, e.TargetPos);
                w.Write(e.GlobalSequence);
                w.Write(e.SourceSequence);
                w.Write(e.Rotation.Rad);
                w.Write(e.Targets.Length);
                foreach (var target in e.Targets)
                {
                    w.Write(target.InstanceId);
                    WriteEffects(w, Compact(target.Effects));
                }
                break;
            case ActorStatusOp s:
                w.Write(s.InstanceId);
                w.Write(s.Index);
                if (s.Status is { } st)
                {
                    w.Write(st.Id);
                    w.Write(st.Extra);
                    w.Write(st.ExpireAt.Ticks);
                    w.Write(st.SourceId);
                }
                break;
            case ActorTetherOp te:
                w.Write(te.InstanceId);
                if (op.Tag.Value != Tag.TetMinus.Value)
                {
                    w.Write(te.TetherId);
                    w.Write(te.TargetId);
                }
                break;
            case ActorIconOp icon:
                w.Write(icon.InstanceId);
                w.Write(icon.IconId);
                w.Write(icon.TargetId);
                break;
            case PartyModifyOp p:
                w.Write(p.Slot);
                if (op.Tag.Value != Tag.ParMinus.Value)
                {
                    w.Write(p.ContentId);
                    w.Write(p.InstanceId);
                    w.Write(p.InCutscene);
                    w.Write(p.Name);
                }
                break;
            default:
                throw new NotSupportedException($"Binary writer does not handle {op.GetType().Name}.");
        }
    }

    private static void WriteHpMp(BinaryWriter w, HpMp hp)
    {
        w.Write(hp.CurHp);
        w.Write(hp.MaxHp);
        w.Write(hp.Shield);
        w.Write(hp.CurMp);
        w.Write(hp.MaxMp);
    }

    private static void WriteVec(BinaryWriter w, Vector3 v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    private static void WriteEffects(BinaryWriter w, ActionEffects effects)
    {
        var raw = effects.Raw.IsDefaultOrEmpty ? ActionEffects.Empty.Raw : effects.Raw;
        for (var i = 0; i < ActionEffects.MaxCount; i++)
            w.Write(i < raw.Length ? raw[i] : 0UL);
    }

    private static ActionEffects Compact(ActionEffects effects)
    {
        Span<ulong> compacted = stackalloc ulong[ActionEffects.MaxCount];
        var i = 0;
        foreach (var effect in effects.NonEmpty())
            compacted[i++] = effect.Raw;
        return ActionEffects.From(compacted);
    }

    private static void WriteTag(BinaryWriter w, Tag tag) => w.Write(tag.Value);
}

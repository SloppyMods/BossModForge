using System.Globalization;
using System.Numerics;
using BossModForge.Core.Replay;

namespace BossModForge.Tests.Fixtures;

public static class TextRecordingWriter
{
    public static void Write(TextWriter dest, RecordingBuilder builder)
    {
        dest.Write(Ts(builder.Start));
        dest.Write("|VER |");
        dest.Write(builder.Version.ToString(CultureInfo.InvariantCulture));
        if (builder.Version >= 10)
        {
            dest.Write('|');
            dest.Write(builder.Qpf.ToString(CultureInfo.InvariantCulture));
        }
        if (builder.Version >= 11)
        {
            dest.Write('|');
            dest.Write(builder.GameVersion);
        }
        dest.WriteLine();

        foreach (var entry in builder.Entries)
        {
            switch (entry)
            {
                case RecordingBuilder.RawEntry raw:
                    dest.Write(Ts(raw.Timestamp));
                    dest.Write('|');
                    dest.Write(raw.Tag.ToString());
                    foreach (var field in raw.Fields)
                    {
                        dest.Write('|');
                        dest.Write(field);
                    }
                    dest.WriteLine();
                    break;
                case RecordingBuilder.OpEntry { Op: var op }:
                    WriteOp(dest, op);
                    break;
            }
        }
    }

    private static void WriteOp(TextWriter dest, Op op)
    {
        dest.Write(Ts(op.Timestamp));
        dest.Write('|');
        dest.Write(op.Tag.ToString());
        switch (op)
        {
            case FrameStartOp fram:
                Field(dest, fram.PrevUpdateTime.TotalMilliseconds.ToString("f3", CultureInfo.InvariantCulture));
                dest.Write('|');
                Field(dest, fram.GaugeLow.ToString("X16"));
                Field(dest, fram.GaugeHigh.ToString("X16"));
                Field(dest, fram.Frame.Qpc.ToString(CultureInfo.InvariantCulture));
                Field(dest, fram.Frame.Index.ToString(CultureInfo.InvariantCulture));
                Field(dest, F(fram.Frame.DurationRaw));
                Field(dest, F(fram.Frame.Duration));
                Field(dest, F(fram.Frame.TickSpeedMultiplier));
                Field(dest, Deg(fram.CameraAzimuth));
                break;
            case ZoneChangeOp zone:
                Field(dest, zone.Zone.ToString(CultureInfo.InvariantCulture));
                Field(dest, zone.CfcId.ToString(CultureInfo.InvariantCulture));
                break;
            case DirectorUpdateOp dir:
                Field(dest, dir.DirectorId.ToString("X8"));
                Field(dest, dir.UpdateId.ToString("X8"));
                Field(dest, dir.Param1.ToString("X8"));
                Field(dest, dir.Param2.ToString("X8"));
                Field(dest, dir.Param3.ToString("X8"));
                Field(dest, dir.Param4.ToString("X8"));
                break;
            case EnvControlOp env:
                Field(dest, env.Index.ToString("X2"));
                Field(dest, env.State.ToString("X8"));
                break;
            case ActorCreateOp a:
                Field(dest, a.InstanceId.ToString("X8"));
                Field(dest, a.Oid.ToString("X"));
                Field(dest, a.SpawnIndex.ToString(CultureInfo.InvariantCulture));
                Field(dest, a.Name);
                Field(dest, a.NameId.ToString(CultureInfo.InvariantCulture));
                Field(dest, ((ushort)a.Type).ToString("X4"));
                Field(dest, a.Class.ToString());
                Field(dest, a.Level.ToString(CultureInfo.InvariantCulture));
                Field(dest, Vec(a.PosRot.Pos));
                Field(dest, Deg(a.PosRot.Rot));
                Field(dest, a.HitboxRadius.ToString("f3", CultureInfo.InvariantCulture));
                WriteHpMp(dest, a.HpMp);
                Field(dest, a.IsTargetable.ToString());
                Field(dest, a.IsAlly.ToString());
                Field(dest, a.OwnerId.ToString("X8"));
                Field(dest, a.FateId.ToString(CultureInfo.InvariantCulture));
                break;
            case ActorDestroyOp d:
                Field(dest, d.InstanceId.ToString("X8"));
                break;
            case ActorRenameOp n:
                Field(dest, n.InstanceId.ToString("X8"));
                Field(dest, n.Name);
                Field(dest, n.NameId.ToString(CultureInfo.InvariantCulture));
                break;
            case ActorMoveOp m:
                Field(dest, m.InstanceId.ToString("X8"));
                Field(dest, Vec(m.PosRot.Pos));
                Field(dest, Deg(m.PosRot.Rot));
                break;
            case ActorHpMpOp h:
                Field(dest, h.InstanceId.ToString("X8"));
                WriteHpMp(dest, h.HpMp);
                break;
            case ActorTargetableOp or ActorDeadOp or ActorCombatOp or ActorCastStopOp or OpenTreasureOp:
                Field(dest, ActorId(op));
                break;
            case ActorTargetOp t:
                Field(dest, t.InstanceId.ToString("X8"));
                Field(dest, t.TargetId.ToString("X8"));
                break;
            case ActorCastStartOp c:
                Field(dest, c.InstanceId.ToString("X8"));
                Field(dest, c.Action.ToString());
                Field(dest, c.TargetId.ToString("X8"));
                Field(dest, Vec(c.Location));
                Field(dest, $"{c.ElapsedTime:f3}/{c.TotalTime:f3}");
                Field(dest, c.Interruptible.ToString());
                Field(dest, Deg(c.Rotation));
                break;
            case ActorCastEventOp e:
                Field(dest, e.InstanceId.ToString("X8"));
                Field(dest, e.Action.ToString());
                Field(dest, e.MainTargetId.ToString("X8"));
                Field(dest, e.AnimationLock.ToString("f2", CultureInfo.InvariantCulture));
                Field(dest, e.MaxTargets.ToString(CultureInfo.InvariantCulture));
                Field(dest, Vec(e.TargetPos));
                Field(dest, e.GlobalSequence.ToString(CultureInfo.InvariantCulture));
                Field(dest, e.SourceSequence.ToString(CultureInfo.InvariantCulture));
                Field(dest, Deg(e.Rotation));
                foreach (var target in e.Targets)
                {
                    dest.Write('|');
                    dest.Write(target.InstanceId.ToString("X8"));
                    foreach (var effect in target.Effects.NonEmpty())
                    {
                        dest.Write('!');
                        dest.Write(effect.Raw.ToString("X16"));
                    }
                }
                break;
            case ActorStatusOp s:
                Field(dest, s.InstanceId.ToString("X8"));
                Field(dest, s.Index.ToString(CultureInfo.InvariantCulture));
                if (s.Status is { } st)
                {
                    Field(dest, st.Id.ToString(CultureInfo.InvariantCulture));
                    Field(dest, st.Extra.ToString("X4"));
                    Field(dest, (st.ExpireAt - op.Timestamp).TotalSeconds.ToString("f3", CultureInfo.InvariantCulture));
                    Field(dest, st.SourceId.ToString("X8"));
                }
                break;
            case ActorTetherOp te:
                Field(dest, te.InstanceId.ToString("X8"));
                if (op.Tag.Value != Tag.TetMinus.Value)
                {
                    Field(dest, te.TetherId.ToString(CultureInfo.InvariantCulture));
                    Field(dest, te.TargetId.ToString("X8"));
                }
                break;
            case ActorIconOp icon:
                Field(dest, icon.InstanceId.ToString("X8"));
                Field(dest, icon.IconId.ToString(CultureInfo.InvariantCulture));
                Field(dest, icon.TargetId.ToString("X8"));
                break;
            case PartyModifyOp p:
                Field(dest, p.Slot.ToString(CultureInfo.InvariantCulture));
                if (op.Tag.Value != Tag.ParMinus.Value)
                {
                    Field(dest, p.ContentId.ToString("X"));
                    Field(dest, p.InstanceId.ToString("X8"));
                    Field(dest, p.InCutscene.ToString());
                    Field(dest, p.Name);
                }
                break;
            default:
                throw new NotSupportedException($"Text writer does not handle {op.GetType().Name}.");
        }
        dest.WriteLine();
    }

    private static void WriteHpMp(TextWriter dest, HpMp hp)
    {
        Field(dest, hp.CurHp.ToString(CultureInfo.InvariantCulture));
        Field(dest, hp.MaxHp.ToString(CultureInfo.InvariantCulture));
        Field(dest, hp.Shield.ToString(CultureInfo.InvariantCulture));
        Field(dest, hp.CurMp.ToString(CultureInfo.InvariantCulture));
        Field(dest, hp.MaxMp.ToString(CultureInfo.InvariantCulture));
    }

    private static string ActorId(Op op) => op switch
    {
        ActorTargetableOp x => x.InstanceId.ToString("X8"),
        ActorDeadOp x => x.InstanceId.ToString("X8"),
        ActorCombatOp x => x.InstanceId.ToString("X8"),
        ActorCastStopOp x => x.InstanceId.ToString("X8"),
        OpenTreasureOp x => x.InstanceId.ToString("X8"),
        _ => throw new InvalidOperationException(),
    };

    private static void Field(TextWriter dest, string value)
    {
        dest.Write('|');
        dest.Write(value);
    }

    private static string Ts(DateTime t) => t.ToString("O", CultureInfo.InvariantCulture);
    private static string F(float v) => v.ToString("g9", CultureInfo.InvariantCulture);
    private static string Deg(Angle a) => a.Deg.ToString("f3", CultureInfo.InvariantCulture);
    private static string Vec(Vector3 v)
        => string.Create(CultureInfo.InvariantCulture, $"{v.X:f3}/{v.Y:f3}/{v.Z:f3}");
}

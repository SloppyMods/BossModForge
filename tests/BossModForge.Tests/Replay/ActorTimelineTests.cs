using System.Numerics;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Replay;

public sealed class ActorTimelineTests
{
    [Fact]
    public void PosRotAt_BeforeBetweenAndAfter()
    {
        var start = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        const ulong id = 0x1000;
        var a = new Vector3(1, 0, 0);
        var b = new Vector3(2, 0, 0);
        var c = new Vector3(3, 0, 0);
        var builder = new RecordingBuilder(start)
            .Frame()
            .Actor(id, 0x4321, "Boss", ActorType.Enemy, a, default)
            .Frame()
            .Move(id, b, Angle.FromDeg(45))
            .Frame()
            .Move(id, c, Angle.FromDeg(90))
            .Frame()
            .Destroy(id);

        using var stream = builder.Open(ReplayEncoding.Text);
        var recording = ReplayParser.Parse(stream, "timeline.log");
        var actor = Assert.Single(recording.Actors);
        Assert.Equal(0, actor.Incarnation);

        var created = actor.Created;
        var firstMove = actor.PosRotSamples[1].T;
        var secondMove = actor.PosRotSamples[2].T;
        var destroyed = actor.Destroyed!.Value;

        AssertVec(a, actor.PosRotAt(created.AddMilliseconds(-1)).Pos);
        AssertVec(a, actor.PosRotAt(created).Pos);
        AssertVec(a, actor.PosRotAt(firstMove.AddTicks(-1)).Pos);
        AssertVec(b, actor.PosRotAt(firstMove).Pos);
        AssertVec(b, actor.PosRotAt(secondMove.AddTicks(-1)).Pos);
        AssertVec(c, actor.PosRotAt(secondMove).Pos);
        AssertVec(c, actor.PosRotAt(destroyed.AddHours(1)).Pos);
        Assert.False(actor.IsAliveAt(destroyed));
        Assert.True(actor.IsAliveAt(destroyed.AddTicks(-1)));
    }

    [Fact]
    public void InstanceIdReuse_AfterDestroy_NewIncarnation()
    {
        var start = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        const ulong id = 0x20;
        var builder = new RecordingBuilder(start)
            .Frame()
            .Player(id, "Alice", Class.PLD, new Vector3(1, 0, 0))
            .Frame()
            .Destroy(id)
            .Frame()
            .Player(id, "Alice", Class.WAR, new Vector3(9, 0, 0));

        using var stream = builder.Open(ReplayEncoding.Text);
        var recording = ReplayParser.Parse(stream, "reuse.log");
        Assert.Equal(2, recording.Actors.Count);
        Assert.Equal(0, recording.Actors[0].Incarnation);
        Assert.Equal(1, recording.Actors[1].Incarnation);
        Assert.Equal(Class.PLD, recording.Actors[0].Class);
        Assert.Equal(Class.WAR, recording.Actors[1].Class);

        var firstLife = recording.Actors[0].Created;
        var gap = recording.Actors[0].Destroyed!.Value;
        var secondLife = recording.Actors[1].Created;
        Assert.Same(recording.Actors[0], recording.ActorAt(id, firstLife));
        Assert.Null(recording.ActorAt(id, gap));
        Assert.Same(recording.Actors[1], recording.ActorAt(id, secondLife));
        AssertVec(new Vector3(9, 0, 0), recording.Actors[1].PosRotAt(secondLife).Pos);
    }

    private static void AssertVec(Vector3 expected, Vector3 actual)
        => Assert.True(Vector3.Distance(expected, actual) < 5e-4, $"{expected} vs {actual}");
}

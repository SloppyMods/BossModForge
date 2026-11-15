namespace BossMod;

public class StateMachineBuilder(BossModule module)
{
    public class Phase(StateMachine.Phase raw, BossModule module)
    {
        public StateMachine.Phase Raw => raw;

        public Phase OnEnter(Action action, bool condition = true) => this;
        public Phase OnExit(Action action, bool condition = true) => this;
        public Phase ActivateOnEnter<C>(bool condition = true) where C : BossComponent => this;
        public Phase DeactivateOnEnter<C>(bool condition = true) where C : BossComponent => this;
        public Phase DeactivateOnExit<C>(bool condition = true) where C : BossComponent => this;
    }

    public class State(StateMachine.State raw, BossModule module)
    {
        public StateMachine.State Raw => raw;

        public State OnEnter(Action action, bool condition = true) => this;
        public State OnExit(Action action, bool condition = true) => this;
        public State ActivateOnEnter<C>(bool condition = true) where C : BossComponent => this;
        public State DeactivateOnExit<C>(bool condition = true) where C : BossComponent => this;
        public State ExecOnEnter<C>(Action<C> fn, bool condition = true) where C : BossComponent => this;
        public State ExecOnExit<C>(Action<C> fn, bool condition = true) where C : BossComponent => this;
    }

    protected BossModule Module = module;
    private readonly Phase _phase = new(new StateMachine.Phase(), module);
    private readonly State _state = new(new StateMachine.State(), module);

    public Phase SimplePhase(uint seqID, Action<uint> buildState, string name, float dur = -1)
    {
        buildState(seqID << 24);
        return _phase;
    }

    public Phase DeathPhase(uint seqID, Action<uint> buildState) => SimplePhase(seqID, buildState, "Boss death");

    public Phase TrivialPhase(uint seqID = 0, float enrage = 10000) => DeathPhase(seqID, id => SimpleState(id, enrage, "Enrage"));

    public State SimpleState(uint id, float duration, string name) => _state;

    public State Timeout(uint id, float duration, string name = "") => _state;

    public State ComponentCondition<T>(uint id, float expected, Func<T, bool> condition, string name = "", float maxOverdue = 1, float checkDelay = 0) where T : BossComponent => _state;

    public State CastStart<AID>(uint id, AID aid, float delay, string name = "") where AID : Enum => _state;

    public State CastEnd(uint id, float castTime, string name = "", bool interruptible = false) => _state;

    public State Cast<AID>(uint id, AID aid, float delay, float castTime, string name = "", bool interruptible = false) where AID : Enum => _state;

    public State CastStartMulti<AID>(uint id, IEnumerable<AID> aids, float delay, string name = "") where AID : Enum => _state;

    public State CastMulti<AID>(uint id, IEnumerable<AID> aids, float delay, float castTime, string name = "", bool interruptible = false) where AID : Enum => _state;

    public State Targetable(uint id, bool targetable, float delay, string name = "", float checkDelay = 0) => _state;
}

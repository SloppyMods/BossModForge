namespace BossMod;

public class StateMachine
{
    public class State
    {
        public Func<float, int>? Update;
    }

    public class Phase
    {
        public Func<bool>? Update;
    }
}

using System.CommandLine;

namespace BossModForge.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        var root = new RootCommand("BossModForge — generate BossMod modules from replay recordings");
        root.Subcommands.Add(Commands.IngestCommand.Create());
        root.Subcommands.Add(Commands.InferCommand.Create());
        root.Subcommands.Add(Commands.EmitCommand.Create());
        root.Subcommands.Add(Commands.ValidateCommand.Create());
        root.Subcommands.Add(Commands.MergeCommand.Create());
        root.Subcommands.Add(Commands.PolishCommand.Create());
        root.Subcommands.Add(Commands.ConformCommand.Create());
        return root.Parse(args).Invoke();
    }
}

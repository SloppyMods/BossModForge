using System.Text.Json;
using System.Text.Json.Nodes;

namespace BossModForge.Core.Polish;

public interface IPolishProvider
{
    string Name { get; }
    JsonElement Complete(JsonObject prompt);
}

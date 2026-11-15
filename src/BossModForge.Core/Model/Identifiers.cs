using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace BossModForge.Core.Model;

public static class CSharpIdentifier
{
    private static readonly FrozenSet<string> Keywords = FrozenSet.ToFrozenSet(
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
        "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        "add", "allows", "alias", "and", "args", "ascending", "async", "await", "by", "descending",
        "dynamic", "equals", "file", "from", "get", "global", "group", "init", "into", "join", "let",
        "managed", "nameof", "nint", "not", "notnull", "nuint", "on", "or", "orderby", "partial",
        "record", "required", "scoped", "select", "set", "unmanaged", "value", "var", "when", "where",
        "with", "yield",
    ], StringComparer.Ordinal);

    public static string Sanitise(string name, string fallbackPrefix)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
            }
            else if (current.Length > 0)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
            parts.Add(current.ToString());

        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
                sb.Append(part, 1, part.Length - 1);
        }

        var ident = sb.ToString();
        if (ident.Length == 0)
            ident = fallbackPrefix;

        if (ident.Length > 0 && char.IsDigit(ident[0]))
            ident = "_" + ident;

        if (Keywords.Contains(ident))
            ident = "@" + ident;

        return ident;
    }

    public static ImmutableArray<string> Dedupe(IEnumerable<string> names)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = ImmutableArray.CreateBuilder<string>();
        foreach (var name in names)
        {
            var candidate = name;
            var n = 1;
            while (!used.Add(candidate))
            {
                candidate = name + n.ToString(CultureInfo.InvariantCulture);
                n++;
            }

            result.Add(candidate);
        }

        return result.ToImmutable();
    }
}

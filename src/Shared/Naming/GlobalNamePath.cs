using System.Text;

// Formats names into a StringBuilder, also in the demangle tool that has no SourceWriter, so StringBuilder is the intended tool here.
#pragma warning disable RS0030

namespace Kassyi.GSharp.Naming;

/// <summary>A C# path that becomes one global C++ name: namespaces, containing types and the symbol itself.</summary>
internal sealed class GlobalNamePath : IEquatable<GlobalNamePath>
{
    public GlobalNamePath(IReadOnlyList<GlobalNameSegment> segments, IReadOnlyList<int>? modifiers = null)
    {
        if (segments.Count == 0)
        {
            throw new ArgumentException("A global name path needs at least one segment.", nameof(segments));
        }

        Segments = segments;
        Modifiers = modifiers ?? Array.Empty<int>();
        foreach (var modifier in Modifiers)
        {
            if (modifier < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(modifiers), modifier,
                    "A modifier is 0 for a pointer or the rank of an array.");
            }
        }
    }

    public IReadOnlyList<GlobalNameSegment> Segments { get; }

    /// <summary>Element-type modifiers after the last segment, innermost first: 0 for a pointer, n for an array of rank n.</summary>
    public IReadOnlyList<int> Modifiers { get; }

    /// <summary>Formats the path like a C# name, for example <c>N.G&lt;System.Int32, System.Single[]&gt;</c>.</summary>
    public string ToDisplayString()
    {
        var builder = new StringBuilder();
        AppendDisplay(builder);
        return builder.ToString();
    }

    public bool Equals(GlobalNamePath? other)
    {
        if (other is null || other.Segments.Count != Segments.Count || other.Modifiers.Count != Modifiers.Count)
        {
            return false;
        }

        for (var i = 0; i < Segments.Count; i++)
        {
            if (!Segments[i].Equals(other.Segments[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < Modifiers.Count; i++)
        {
            if (Modifiers[i] != other.Modifiers[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is GlobalNamePath other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var segment in Segments)
            {
                hash = hash * 31 + segment.GetHashCode();
            }

            foreach (var modifier in Modifiers)
            {
                hash = hash * 31 + modifier;
            }

            return hash;
        }
    }

    public override string ToString() => ToDisplayString();

    internal void AppendDisplay(StringBuilder builder)
    {
        for (var i = 0; i < Segments.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('.');
            }

            Segments[i].AppendDisplay(builder);
        }

        foreach (var modifier in Modifiers)
        {
            if (modifier == 0)
            {
                builder.Append('*');
            }
            else
            {
                builder.Append('[').Append(',', modifier - 1).Append(']');
            }
        }
    }
}

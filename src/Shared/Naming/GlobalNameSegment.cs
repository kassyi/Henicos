using System.Globalization;
using System.Text;

// Formats names into a StringBuilder, also in the demangle tool that has no SourceWriter, so StringBuilder is the intended tool here.
#pragma warning disable RS0030

namespace Kassyi.GSharp.Naming;

/// <summary>One segment of a <see cref="GlobalNamePath"/> with its generic, file and overload markers.</summary>
internal sealed class GlobalNameSegment : IEquatable<GlobalNameSegment>
{
    public GlobalNameSegment(string name, IReadOnlyList<GlobalNamePath>? typeArguments = null, int arity = 0,
        string? fileId = null, int overloadOrdinal = -1)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("A segment needs a name.", nameof(name));
        }

        if (arity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(arity), arity, "The arity must not be negative.");
        }

        if (overloadOrdinal < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(overloadOrdinal), overloadOrdinal,
                "The overload ordinal is -1 (none) or a non-negative index.");
        }

        if (fileId is not null)
        {
            foreach (var c in fileId)
            {
                if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                {
                    throw new ArgumentException("The file identity must be lowercase hex.", nameof(fileId));
                }
            }

            if (fileId.Length == 0)
            {
                throw new ArgumentException("The file identity must not be empty.", nameof(fileId));
            }
        }

        Name = name;
        TypeArguments = typeArguments ?? Array.Empty<GlobalNamePath>();
        Arity = arity;
        FileId = fileId;
        OverloadOrdinal = overloadOrdinal;
    }

    /// <summary>The C# name without the verbatim <c>@</c>.</summary>
    public string Name { get; }

    /// <summary>The type arguments of a constructed generic type, or empty.</summary>
    public IReadOnlyList<GlobalNamePath> TypeArguments { get; }

    /// <summary>The number of type parameters of a generic definition, or 0.</summary>
    public int Arity { get; }

    /// <summary>A lowercase hex identity of the declaring file for a <c>file</c> type, or <see langword="null"/>.</summary>
    public string? FileId { get; }

    /// <summary>The index of the method within its overload group, or -1 when the method is not overloaded.</summary>
    public int OverloadOrdinal { get; }

    public bool Equals(GlobalNameSegment? other)
    {
        if (other is null || !string.Equals(Name, other.Name, StringComparison.Ordinal) || Arity != other.Arity ||
            OverloadOrdinal != other.OverloadOrdinal ||
            !string.Equals(FileId, other.FileId, StringComparison.Ordinal) ||
            TypeArguments.Count != other.TypeArguments.Count)
        {
            return false;
        }

        for (var i = 0; i < TypeArguments.Count; i++)
        {
            if (!TypeArguments[i].Equals(other.TypeArguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is GlobalNameSegment other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = StringComparer.Ordinal.GetHashCode(Name);
            hash = hash * 31 + Arity;
            hash = hash * 31 + OverloadOrdinal;
            hash = hash * 31 + (FileId is null ? 0 : StringComparer.Ordinal.GetHashCode(FileId));
            foreach (var argument in TypeArguments)
            {
                hash = hash * 31 + argument.GetHashCode();
            }

            return hash;
        }
    }

    internal void AppendDisplay(StringBuilder builder)
    {
        builder.Append(Name);
        if (TypeArguments.Count > 0)
        {
            builder.Append('<');
            for (var i = 0; i < TypeArguments.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                TypeArguments[i].AppendDisplay(builder);
            }

            builder.Append('>');
        }

        if (Arity > 0)
        {
            builder.Append('`').Append(Arity.ToString(CultureInfo.InvariantCulture));
        }

        if (FileId is not null)
        {
            builder.Append("(file ").Append(FileId).Append(')');
        }

        if (OverloadOrdinal >= 0)
        {
            builder.Append('#').Append(OverloadOrdinal.ToString(CultureInfo.InvariantCulture));
        }
    }
}

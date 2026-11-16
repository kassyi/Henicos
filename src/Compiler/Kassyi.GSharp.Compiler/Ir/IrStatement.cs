using System;
using System.Collections.Immutable;

namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>A statement.</summary>
/// <param name="source">Where the node comes from.</param>
internal abstract class IrStatement(IrSource source) : IrNode(source)
{
    /// <summary>
    /// The comments written before the statement in the source, as written, in order (<c>// …</c>, <c>/* … */</c>,
    /// <c>/// …</c> without its line break); empty for a statement a pass made. Only the managed writer prints them.
    /// </summary>
    public ImmutableArray<string> CommentsBefore { get; private set; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// <paramref name="rebuilt"/>, which an <c>Update</c> or <c>With...</c> of this node has just created, with the
    /// spelling of this node.
    /// </summary>
    protected T KeepSpelling<T>(T rebuilt) where T : IrStatement
    {
        IrStatement node = rebuilt;
        node.CommentsBefore = CommentsBefore;
        return rebuilt;
    }

    /// <summary>
    /// This statement with <paramref name="comments"/> before it; this statement itself when they are the same.
    /// </summary>
    public IrStatement WithCommentsBefore(ImmutableArray<string> comments)
    {
        if (SameComments(comments, CommentsBefore))
        {
            return this;
        }

        // [WHY] Every other field is read-only, so the copy shares nothing that changes
        var copy = (IrStatement)MemberwiseClone();
        copy.CommentsBefore = comments;
        return copy;
    }

    /// <summary>
    /// <see cref="WithCommentsBefore(ImmutableArray{string})"/> that keeps the type of <paramref name="statement"/>.
    /// </summary>
    internal static T WithCommentsBefore<T>(T statement, ImmutableArray<string> comments) where T : IrStatement =>
        (T)statement.WithCommentsBefore(comments);

    private static bool SameComments(ImmutableArray<string> left, ImmutableArray<string> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

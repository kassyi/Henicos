namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>How a statement takes part in the removal of the unreachable statements of a list.</summary>
/// <param name="EndsUnconditionally">
/// Its end is reached by no path: a <c>goto</c>, <c>return</c>, <c>break</c> or <c>continue</c>, a labeled statement
/// whose statement is one, or a non-empty block whose last statement ends unconditionally.
/// </param>
/// <param name="StopsRemoval">
/// A labeled statement (a jump may reach it) or a local declaration (a later label may use the local): the removal
/// stops before it.
/// </param>
internal readonly record struct StatementReach(bool EndsUnconditionally, bool StopsRemoval);

/// <summary>
/// The statements of a list that C# reports as unreachable (CS0162) and that the managed state machine leaves out,
/// decided from the facts of each statement.
/// </summary>
internal static class UnreachableStatements
{
    /// <summary>
    /// The positions of the statements of a list that stay when the statements C# calls unreachable are removed, in
    /// order.
    /// </summary>
    public static ImmutableArray<int> FindKept(ImmutableArray<StatementReach> statements)
    {
        if (statements.IsEmpty)
        {
            return ImmutableArray<int>.Empty;
        }

        var current = ImmutableArray.CreateBuilder<int>(statements.Length);
        for (var i = 0; i < statements.Length; i++)
        {
            current.Add(i);
        }

        return Remove(current.ToImmutable(), statements);

        static ImmutableArray<int> Remove(ImmutableArray<int> indices, ImmutableArray<StatementReach> statements)
        {
            for (var i = 0; i < indices.Length - 1; i++)
            {
                if (!statements[indices[i]].EndsUnconditionally)
                {
                    continue;
                }

                var resume = i + 1;
                while (resume < indices.Length && !statements[indices[resume]].StopsRemoval)
                {
                    resume++;
                }

                if (resume == i + 1)
                {
                    continue;
                }

                var kept = ImmutableArray.CreateBuilder<int>(indices.Length - (resume - (i + 1)));
                for (var j = 0; j <= i; j++)
                {
                    kept.Add(indices[j]);
                }

                for (var j = resume; j < indices.Length; j++)
                {
                    kept.Add(indices[j]);
                }

                return Remove(kept.ToImmutable(), statements);
            }

            return indices;
        }
    }
}

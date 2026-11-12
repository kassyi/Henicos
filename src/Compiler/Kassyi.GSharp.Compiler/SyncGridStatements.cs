namespace Kassyi.GSharp.Compiler;

/// <summary>The statements of a <c>Gpu.SyncGrid(body)</c>, the bodies of nested <c>Gpu.SyncGrid(body)</c> statements flattened in.</summary>
/// <param name="Elements">The statements in source order.</param>
/// <param name="BodyParents">
/// For each body, the index of the body it is nested in; -1 for the outermost body (index 0).
/// </param>
internal readonly record struct SyncGridStatements(
    ImmutableArray<SyncGridElement> Elements,
    ImmutableArray<int> BodyParents)
{
    /// <summary>Whether the statements hold anything other than empty statements.</summary>
    public bool HasExecutableStatements => Elements
        .AsStackEnumerable()
        .Any(static element => element.Node is not EmptyStatementSyntax);

    /// <summary>Whether a statement of body <paramref name="body"/> itself holds a <c>return</c> of the body.</summary>
    public bool BodyHasReturn(int body) => Elements
        .AsStackEnumerable()
        .Any(body, static (element, b) => element.BodyIndex == b && element.HasBodyReturn);

    /// <summary>Whether <paramref name="ancestor"/> is <paramref name="descendant"/> or a body it is nested in.</summary>
    public bool IsWithin(int descendant, int ancestor)
    {
        for (var body = descendant; body >= 0; body = BodyParents[body])
        {
            if (body == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The body directly nested in <paramref name="ancestor"/> on the way to <paramref name="descendant"/>.</summary>
    public int ChildOnPath(int descendant, int ancestor)
    {
        var body = descendant;
        while (BodyParents[body] != ancestor)
        {
            body = BodyParents[body];
        }

        return body;
    }

    /// <summary>
    /// The bodies whose done flag guards statement <paramref name="index"/>, outermost first: each body that the statement
    /// is in and that returned in an earlier statement of its own. Empty for a statement that is never guarded
    /// (<see cref="SyncGridBody.IsNeverGuarded"/>).
    /// </summary>
    public ImmutableArray<int> GetGuardBodies(int index, SemanticModel? model)
    {
        var element = Elements[index];
        if (SyncGridBody.IsNeverGuarded(element.Node, model))
        {
            return ImmutableArray<int>.Empty;
        }

        var guards = ImmutableArray.CreateBuilder<int>();
        for (var body = element.BodyIndex; body >= 0; body = BodyParents[body])
        {
            for (var earlier = 0; earlier < index; earlier++)
            {
                if (Elements[earlier].BodyIndex == body && Elements[earlier].HasBodyReturn)
                {
                    guards.Insert(0, body);
                    break;
                }
            }
        }

        return guards.ToImmutable();
    }
}

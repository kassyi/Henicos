namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The jumps that leave the body of a <c>using</c> on the CPU, found before the body is written (Plan / Emit): each
/// becomes a <c>goto</c> to an exit label after the <c>catch</c> of the <c>using</c> (<see cref="UsingCpuEmitter"/>).
/// </summary>
/// <remarks>
/// A jump leaves the body when its target is outside it: the function of a <c>return</c> (not a lambda or local
/// function in the body), the loop or switch of a <c>break</c> or <c>continue</c>, the label of a <c>goto</c>, the
/// switch of a <c>goto case</c>. The jumps that lowering writes (state machines, flattened switches, inlined
/// iterators) are syntax of the lowered tree like those of the user and follow the same rule.
/// </remarks>
internal sealed class UsingExitPlan
{
    private UsingExitPlan(SyntaxTree tree, TextSpan body, SyntaxNode? function, ImmutableArray<UsingExit> exits)
    {
        Tree = tree;
        Body = body;
        Function = function;
        Exits = exits;
    }

    /// <summary>The tree of the body.</summary>
    public SyntaxTree Tree { get; }

    /// <summary>The span of the body.</summary>
    public TextSpan Body { get; }

    /// <summary>The function that holds the <c>using</c>, or <see langword="null"/> outside one.</summary>
    public SyntaxNode? Function { get; }

    /// <summary>The ways out of the body, in the order their first jump is written.</summary>
    public ImmutableArray<UsingExit> Exits { get; }

    /// <summary>Whether a <c>return</c> with a value leaves the body.</summary>
    public bool ReturnsValue => Exits
        .AsStackEnumerable()
        .Any(static exit => exit.Jump is ReturnStatementSyntax { Expression: not null });

    /// <summary>
    /// Finds the jumps out of the statements from <paramref name="first"/> to <paramref name="last"/>.
    /// </summary>
    /// <param name="owner">The <c>using</c> statement or declaration, whose function holds the body.</param>
    /// <param name="first">The first statement of the body, or <see langword="null"/> for an empty body.</param>
    /// <param name="last">The last statement of the body.</param>
    public static UsingExitPlan Create(SyntaxNode owner, StatementSyntax? first, StatementSyntax? last)
    {
        var function = JumpTargets.FunctionOf(owner);
        if (first is null || last is null)
        {
            return new UsingExitPlan(owner.SyntaxTree, new TextSpan(owner.Span.End, 0), function,
                ImmutableArray<UsingExit>.Empty);
        }

        var body = TextSpan.FromBounds(first.SpanStart, last.Span.End);
        var exits = ImmutableArray.CreateBuilder<UsingExit>();
        var parent = first.Parent ?? first;
        foreach (var node in parent.DescendantNodes(body))
        {
            if (node is StatementSyntax jump && body.Contains(jump.Span) && TryGetExit(jump, body, out var exit) &&
                !exits
                    .AsStackEnumerable()
                    .Any(exit, static (candidate, e) => IsSameExit(candidate, e)))
            {
                exits.Add(exit);
            }
        }

        return new UsingExitPlan(owner.SyntaxTree, body, function, exits.ToImmutable());
    }

    /// <summary>
    /// The index of the exit that <paramref name="jump"/> takes, or -1 when it is not in the body or stays in it.
    /// </summary>
    public int FindExit(StatementSyntax jump)
    {
        if (jump.SyntaxTree != Tree || !Body.Contains(jump.Span) || !TryGetExit(jump, Body, out var exit))
        {
            return -1;
        }

        var index = Exits
            .AsStackEnumerable()
            .IndexOf(exit, static (candidate, e) => IsSameExit(candidate, e));
        return index >= 0
            ? index
            : throw new InvalidOperationException("A jump out of the body of a using has no exit in its plan.");
    }

    // The exit of a jump whose target is outside the body
    private static bool TryGetExit(StatementSyntax jump, TextSpan body, out UsingExit exit)
    {
        exit = default;
        if (!JumpTargets.TryGetTarget(jump, out var kind, out var target) ||
            (body.Contains(target.Span) && target.SyntaxTree == jump.SyntaxTree))
        {
            return false;
        }

        var key = kind == UsingExitKind.GotoCase ? jump : target;
        var stateMachineReturn = jump.HasAnnotations(LoweringAnnotations.StateMachineReturnAnnotationKind);
        exit = new UsingExit(kind, key, stateMachineReturn, jump);
        return true;
    }

    private static bool IsSameExit(UsingExit candidate, UsingExit exit) =>
        candidate.Kind == exit.Kind && candidate.Key == exit.Key &&
        candidate.StateMachineReturn == exit.StateMachineReturn;
}

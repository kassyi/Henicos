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
    private readonly Dictionary<SyntaxNode, int> _exitOf;

    private UsingExitPlan(TextSpan body, SyntaxNode? function, ImmutableArray<UsingExit> exits,
        Dictionary<SyntaxNode, int> exitOf, ITypeSymbol? returnType, bool returnsByRef, bool declaresReturnValue)
    {
        Body = body;
        Function = function;
        Exits = exits;
        _exitOf = exitOf;
        ReturnType = returnType;
        ReturnsByRef = returnsByRef;
        DeclaresReturnValue = declaresReturnValue;
    }

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
    /// The return type of the function, which keeps the value of a <c>return</c> that leaves the body
    /// (<c>__gsharp_using_return</c>); <see langword="null"/> when no such <c>return</c> leaves it or the function
    /// has no known symbol.
    /// </summary>
    public ITypeSymbol? ReturnType { get; }

    /// <summary>Whether the function returns by reference, so that the kept value is a pointer.</summary>
    public bool ReturnsByRef { get; }

    /// <summary>
    /// Whether this <c>using</c> declares <c>__gsharp_using_return</c>: a <c>return</c> with a value leaves it, and no
    /// <c>using</c> of the same function encloses it (that one declares it).
    /// </summary>
    public bool DeclaresReturnValue { get; }

    /// <summary>
    /// Finds the jumps out of the statements from <paramref name="first"/> to <paramref name="last"/>.
    /// </summary>
    /// <param name="owner">The <c>using</c> statement or declaration, whose function holds the body.</param>
    /// <param name="first">The first statement of the body, or <see langword="null"/> for an empty body.</param>
    /// <param name="last">The last statement of the body.</param>
    /// <param name="semanticModel">Binds the function of the <c>using</c>.</param>
    /// <param name="method">
    /// The method being transpiled, the function of a lowered body outside any declaration.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// A <c>goto</c> in the body names no label of its function.
    /// </exception>
    public static UsingExitPlan Create(SyntaxNode owner, StatementSyntax? first, StatementSyntax? last,
        SemanticModel semanticModel, IMethodSymbol? method)
    {
        var function = JumpTargets.FunctionOf(owner);
        var exits = ImmutableArray.CreateBuilder<UsingExit>();
        var exitOf = new Dictionary<SyntaxNode, int>();
        var body = first is null || last is null
            ? new TextSpan(owner.Span.End, 0)
            : TextSpan.FromBounds(first.SpanStart, last.Span.End);
        if (first is not null && last is not null)
        {
            foreach (var node in (first.Parent ?? first).DescendantNodes(body))
            {
                if (node is StatementSyntax jump && body.Contains(jump.Span) && TryGetExit(jump, body, out var exit))
                {
                    exitOf[jump] = AddExit(exits, exit);
                }
            }
        }

        var returnsValue = exits
            .AsStackEnumerable()
            .Any(static exit => exit.Jump is ReturnStatementSyntax { Expression: not null });
        var symbol = returnsValue ? FunctionSymbol(function, semanticModel, method) : null;
        return new UsingExitPlan(body, function, exits.ToImmutable(), exitOf, symbol?.ReturnType,
            symbol is { ReturnsByRef: true } or { ReturnsByRefReadonly: true },
            returnsValue && !IsInsideUsing(owner, function));
    }

    /// <summary>
    /// The index of the exit that <paramref name="jump"/> takes, or -1 when it does not leave the body.
    /// </summary>
    public int FindExit(StatementSyntax jump) => _exitOf.TryGetValue(jump, out var index) ? index : -1;

    // The exit of a jump whose target is outside the body
    private static bool TryGetExit(StatementSyntax jump, TextSpan body, out UsingExit exit)
    {
        exit = default;
        if (!JumpTargets.TryGetTarget(jump, out var kind, out var target))
        {
            // [WHY] A goto written as it is would leave the try without the Dispose: no answer is a failure
            return jump is GotoStatementSyntax { RawKind: (int)SyntaxKind.GotoStatement }
                ? throw new InvalidOperationException(
                    "A goto in the body of a using names a label that is not in its function.")
                : false;
        }

        if (body.Contains(target.Span) && target.SyntaxTree == jump.SyntaxTree)
        {
            return false;
        }

        var key = kind == UsingExitKind.GotoCase ? jump : target;
        var stateMachineReturn = jump.HasAnnotations(LoweringAnnotations.StateMachineReturnAnnotationKind);
        exit = new UsingExit(kind, key, stateMachineReturn, jump);
        return true;
    }

    // The index of the exit that has the target of exit, added when it is new
    private static int AddExit(ImmutableArray<UsingExit>.Builder exits, UsingExit exit)
    {
        var index = exits
            .AsStackEnumerable()
            .IndexOf(exit, static (candidate, e) => IsSameExit(candidate, e));
        if (index >= 0)
        {
            return index;
        }

        exits.Add(exit);
        return exits.Count - 1;
    }

    private static bool IsSameExit(UsingExit candidate, UsingExit exit) =>
        candidate.Kind == exit.Kind && candidate.Key == exit.Key &&
        candidate.StateMachineReturn == exit.StateMachineReturn;

    // The function of the using; a lowered body may be outside any declaration of its tree: its function is the
    // method being transpiled
    private static IMethodSymbol? FunctionSymbol(SyntaxNode? function, SemanticModel semanticModel,
        IMethodSymbol? method) => function switch
    {
        null => method,
        AnonymousFunctionExpressionSyntax lambda => semanticModel.GetSymbol(lambda) as IMethodSymbol,
        BaseMethodDeclarationSyntax declaration =>
            semanticModel.GetDeclaredSymbol<IMethodSymbol>(declaration) ?? method,
        _ => semanticModel.GetDeclaredSymbol<IMethodSymbol>(function)
    };

    // Whether a using statement or an earlier using declaration of the same function encloses the owner
    private static bool IsInsideUsing(SyntaxNode owner, SyntaxNode? function)
    {
        var child = owner;
        for (var current = owner.Parent; current != null && current != function; current = current.Parent)
        {
            switch (current)
            {
                case UsingStatementSyntax usingStatement when usingStatement.Statement == child:
                    return true;
                case BlockSyntax block when DeclaresUsingBefore(block, child):
                    return true;
            }

            child = current;
        }

        return false;
    }

    private static bool DeclaresUsingBefore(BlockSyntax block, SyntaxNode child)
    {
        foreach (var statement in block.Statements)
        {
            if (statement == child)
            {
                return false;
            }

            if (UsingCpuEmitter.TryGetUsingDeclaration(statement, out _))
            {
                return true;
            }
        }

        return false;
    }
}

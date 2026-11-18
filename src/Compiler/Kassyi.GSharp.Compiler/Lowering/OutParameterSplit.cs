namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Whether a device function that may suspend writes an <c>out</c> parameter and then reaches a split point before it
/// returns, found on the lowered state machine of the callee (<see cref="AstLoweringPipeline"/>).
/// </summary>
/// <remarks>
/// [WHY] A callee that writes its <c>out</c> span in the last call that runs it hands the value to the caller in the
/// same call of the state function, so the caller's redeclared local holds it when the caller reads it. A callee that
/// writes it and then splits does not: the next resume runs the callee from its later state, and the caller's local is
/// <c>default</c> again. A callee whose syntax is not available (another assembly) is not looked into; its write does
/// not count, as before, so the caller's read is reported.
/// </remarks>
internal static class OutParameterSplit
{
    /// <summary>
    /// Whether the callee of the call that takes <paramref name="argument"/> writes the <c>out</c> parameter that the
    /// argument passes and then splits: the value does not survive the next resume. <see langword="true"/> when the
    /// callee cannot be looked into.
    /// </summary>
    public static bool IsWrittenBeforeASplit(ArgumentSyntax argument, SemanticModel semanticModel)
    {
        if (argument.Parent?.Parent is not InvocationExpressionSyntax call)
        {
            return true;
        }

        SimpleNameSyntax? name = call.Expression switch
        {
            IdentifierNameSyntax identifier => identifier,
            MemberAccessExpressionSyntax access => access.Name,
            _ => null
        };
        if (name is null || semanticModel.GetSymbol(name) is not IMethodSymbol callee)
        {
            return true;
        }

        var parameter = FindParameter(callee, call, argument);
        return parameter is null || SplitsAfterWrite(callee, parameter, semanticModel.Compilation);
    }

    // The parameter that the argument is passed to, by name or by position
    private static IParameterSymbol? FindParameter(IMethodSymbol callee, InvocationExpressionSyntax call,
        ArgumentSyntax argument)
    {
        if (argument.NameColon is { } colon)
        {
            return callee.Parameters
                .AsStackEnumerable()
                .FirstOrDefault(colon.Name.Identifier.ValueText, static (p, parameterName) => p.Name == parameterName);
        }

        var index = call.ArgumentList.Arguments.IndexOf(argument);
        return index >= 0 && index < callee.Parameters.Length ? callee.Parameters[index] : null;
    }

    // Whether a write of parameter in callee is followed by a split point on some path to the return
    private static bool SplitsAfterWrite(IMethodSymbol callee, IParameterSymbol parameter, Compilation compilation)
    {
        if (callee.DeclaringSyntaxReferences is not [var reference] ||
            reference.GetSyntax() is not MethodDeclarationSyntax { Body: not null } declaration)
        {
            return true;
        }

        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        var states = ReadStates(declaration, callee, parameter, model);
        var splits = FindStatesThatSplit(states);
        foreach (var events in states.Values)
        {
            for (var i = 0; i < events.Count; i++)
            {
                if (events[i].Kind == Kind.Write && SplitsAfter(events, i, splits))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // The writes, splits and jumps of each state of the lowered callee, in the order they run
    private static Dictionary<int, List<(Kind Kind, int Target)>> ReadStates(MethodDeclarationSyntax declaration,
        IMethodSymbol callee, IParameterSymbol parameter, SemanticModel model)
    {
        var states = new Dictionary<int, List<(Kind Kind, int Target)>>();
        var lowered = AstLoweringPipeline.Lower(declaration, model, callee, isDeviceFunction: true);
        if (lowered.LoweredBody.Statements.LastOrDefault() is not SwitchStatementSyntax body)
        {
            return states;
        }

        foreach (var section in body.Sections)
        {
            if (section.Labels is [CaseSwitchLabelSyntax { Value: LiteralExpressionSyntax { Token.Value: int state } }])
            {
                var events = new List<(Kind, int)>();
                new Walker(parameter, model, events).Visit(section);
                states[state] = events;
            }
        }

        return states;
    }

    // The states that split from their start: a state assignment later in the state, or a goto case to such a state
    private static HashSet<int> FindStatesThatSplit(Dictionary<int, List<(Kind Kind, int Target)>> states)
    {
        var splits = new HashSet<int>();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var pair in states)
            {
                if (!splits.Contains(pair.Key) && SplitsAfter(pair.Value, -1, splits))
                {
                    splits.Add(pair.Key);
                    changed = true;
                }
            }
        }

        return splits;
    }

    private static bool SplitsAfter(List<(Kind Kind, int Target)> events, int index, HashSet<int> splits)
    {
        for (var i = index + 1; i < events.Count; i++)
        {
            if (events[i].Kind == Kind.Split || (events[i].Kind == Kind.Jump && splits.Contains(events[i].Target)))
            {
                return true;
            }
        }

        return false;
    }

    private enum Kind
    {
        Write,
        Split,
        Jump
    }

    private sealed class Walker(IParameterSymbol parameter, SemanticModel model, List<(Kind, int)> events)
        : CSharpSyntaxWalker
    {
        // The statements after a jump or a return never run
        public override void VisitBlock(BlockSyntax node)
        {
            foreach (var statement in node.Statements)
            {
                Visit(statement);
                if (statement is GotoStatementSyntax or ReturnStatementSyntax)
                {
                    return;
                }
            }
        }

        public override void VisitGotoStatement(GotoStatementSyntax node)
        {
            if (node.IsKind(SyntaxKind.GotoCaseStatement) &&
                node.Expression is LiteralExpressionSyntax { Token.Value: int target })
            {
                events.Add((Kind.Jump, target));
            }

            base.VisitGotoStatement(node);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.SimpleAssignmentExpression) && node.Left is IdentifierNameSyntax left)
            {
                if (left.Identifier.ValueText == GSharpInternalNames.StateMachine.State &&
                    node.Right is LiteralExpressionSyntax { Token.Value: int })
                {
                    events.Add((Kind.Split, 0));
                    return;
                }

                if (IsParameter(left))
                {
                    Visit(node.Right);
                    events.Add((Kind.Write, 0));
                    return;
                }
            }

            base.VisitAssignmentExpression(node);
        }

        public override void VisitArgument(ArgumentSyntax node)
        {
            if (node.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) && node.Expression is IdentifierNameSyntax name &&
                IsParameter(name))
            {
                events.Add((Kind.Write, 0));
                return;
            }

            base.VisitArgument(node);
        }

        private bool IsParameter(IdentifierNameSyntax name) =>
            LoweringIdentity.ForReference(name, model).Symbol.IsEqualTo(parameter.OriginalDefinition);
    }
}

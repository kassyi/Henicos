using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.ResumeLocals;

namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Finds the span locals that the CPU state machine declares again on every resume (<see cref="ResumeLocals"/>) and
/// that a state reads before it writes them, on the lowered state machine itself.
/// </summary>
/// <remarks>
/// <para>
/// [WHY] A span local is redeclared with its default value before the state switch on every resume, so it keeps a value
/// from one state into another only inside one call of the state function, through <c>goto case</c>. A state that a
/// resume enters (<c>__gsharp_state = n; return;</c>) and that reads the span before it declares or assigns it, or that
/// reaches such a read through <c>goto case</c> without writing the span, reads <c>default</c>. The analyzer's flow
/// graph misses the splits that lowering adds (the reconvergence points of <c>Warp.ActiveMask()</c>), the loops that
/// bring a state back, and a span that a suspending call writes through <c>out</c>; the lowered body has all of them.
/// Until the CPU paths keep a thread's stack, each one is GSH0039.
/// </para>
/// <para>
/// A write is a declaration or assignment of the whole local, or an <c>out</c> argument of a call that does not
/// suspend. The <c>out</c> argument of a suspending call is a write only when its callee does not split after writing
/// the parameter (<see cref="OutParameterSplit"/>); otherwise the callee wrote it in a state that does not run again
/// after the resume. A span that the analyzer reports as GSH0039 is left out
/// (<see cref="HeldRefLocal.IsReportedLive"/> makes the same split for <c>ref</c> locals).
/// </para>
/// </remarks>
internal static class SpanAcrossStates
{
    /// <summary>
    /// The declarators among <paramref name="lowered"/>'s context fields that carry a span from one state into another.
    /// </summary>
    /// <param name="lowered">The lowered state machine.</param>
    /// <param name="semanticModel">The semantic model of the tree that declares <paramref name="method"/>.</param>
    /// <param name="method">The kernel or device function, or <see langword="null"/> when it is not known.</param>
    public static ImmutableArray<VariableDeclaratorSyntax> Find(
        LoweredStateMachine lowered,
        SemanticModel? semanticModel,
        MethodDeclarationSyntax? method)
    {
        if (semanticModel is null || lowered.LoweredBody.Statements.LastOrDefault() is not SwitchStatementSyntax body)
        {
            return ImmutableArray<VariableDeclaratorSyntax>.Empty;
        }

        var candidates = FindCandidates(lowered, semanticModel, method?.Identifier.ValueText ?? "");
        if (candidates.Count == 0)
        {
            return ImmutableArray<VariableDeclaratorSyntax>.Empty;
        }

        var states = ReadStates(body, candidates, semanticModel);
        var entries = FindResumeEntries(body);
        var carried = ImmutableArray.CreateBuilder<VariableDeclaratorSyntax>();
        foreach (var (key, field) in candidates)
        {
            if (IsCarriedIntoAnEntry(key, states, entries))
            {
                carried.Add(field);
            }
        }

        return RemoveReportedByAnalyzer(carried, semanticModel, method);
    }

    // The hoisted locals that are redeclared on every resume and are ref structs (not ref locals or pointers)
    private static List<(DeclarationKey Key, VariableDeclaratorSyntax Field)> FindCandidates(
        LoweredStateMachine lowered,
        SemanticModel semanticModel,
        string kernelName)
    {
        var candidates = new List<(DeclarationKey, VariableDeclaratorSyntax)>();
        foreach (var field in lowered.ContextFields)
        {
            if (field.Parent is VariableDeclarationSyntax declaration && RefLocalLiveness.IsRefType(declaration.Type))
            {
                continue;
            }

            var type = HoistedFieldTypeResolver.Resolve(field, semanticModel, kernelName);
            if (type.IsRefLikeType && ResumeLocals.IsRedeclared(field, type, sharedMemoryInFrame: true))
            {
                candidates.Add((LoweringIdentity.ForDeclaration(field, semanticModel), field));
            }
        }

        return candidates;
    }

    // The states that some resume enters: the numbers that the state function stores before it returns
    private static HashSet<int> FindResumeEntries(SwitchStatementSyntax body)
    {
        var entries = new HashSet<int>();
        foreach (var node in body.DescendantNodes())
        {
            if (node is AssignmentExpressionSyntax
                {
                    Left: IdentifierNameSyntax { Identifier.ValueText: GSharpInternalNames.StateMachine.State },
                    Right: LiteralExpressionSyntax { Token.Value: int state }
                })
            {
                entries.Add(state);
            }
        }

        return entries;
    }

    // What each state does with each candidate, in the order it runs
    private static Dictionary<int, StateEvents> ReadStates(SwitchStatementSyntax body,
        List<(DeclarationKey Key, VariableDeclaratorSyntax Field)> candidates, SemanticModel semanticModel)
    {
        var keys = new HashSet<DeclarationKey>();
        foreach (var (key, _) in candidates)
        {
            keys.Add(key);
        }

        var states = new Dictionary<int, StateEvents>();
        foreach (var section in body.Sections)
        {
            if (section.Labels is not
                [CaseSwitchLabelSyntax { Value: LiteralExpressionSyntax { Token.Value: int state } }])
            {
                continue;
            }

            var events = new StateEvents();
            new EventWalker(keys, semanticModel, events).Visit(section);
            states[state] = events;
        }

        return states;
    }

    // Whether a state that a resume enters reads the local before writing it, directly or through goto case
    private static bool IsCarriedIntoAnEntry(
        DeclarationKey key,
        Dictionary<int, StateEvents> states,
        HashSet<int> entries)
    {
        var live = new HashSet<int>();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var pair in states)
            {
                if (!live.Contains(pair.Key) && pair.Value.IsLiveIn(key, live))
                {
                    live.Add(pair.Key);
                    changed = true;
                }
            }
        }

        return entries.Overlaps(live);
    }

    // Leaves out the spans that the analyzer reports as GSH0039 itself: a local of a declaration statement that is live
    // after a split
    private static ImmutableArray<VariableDeclaratorSyntax> RemoveReportedByAnalyzer(
        ImmutableArray<VariableDeclaratorSyntax>.Builder carried, SemanticModel semanticModel,
        MethodDeclarationSyntax? method)
    {
        if (carried.Count == 0 || method is null || method.SyntaxTree != semanticModel.SyntaxTree ||
            !new MethodFlowGraph(semanticModel, method).TryGetLiveness(out var graph, out var liveness))
        {
            return carried.ToImmutable();
        }

        var (symbols, analyzed) = CollectTrackedLocals(carried, semanticModel);
        var reported = FindReportedByAnalyzer(graph, liveness, symbols, analyzed);
        var remaining = ImmutableArray.CreateBuilder<VariableDeclaratorSyntax>();
        foreach (var field in carried)
        {
            if (!reported.Contains(field))
            {
                remaining.Add(field);
            }
        }

        return remaining.ToImmutable();
    }

    // The carried spans that the analyzer tracks (a local of a declaration statement), with their symbols
    private static (List<ISymbol> Symbols, List<VariableDeclaratorSyntax> Fields) CollectTrackedLocals(
        ImmutableArray<VariableDeclaratorSyntax>.Builder carried, SemanticModel semanticModel)
    {
        var symbols = new List<ISymbol>();
        var fields = new List<VariableDeclaratorSyntax>();
        foreach (var field in carried)
        {
            if (LoweringIdentity.ForDeclaration(field, semanticModel).Symbol is ILocalSymbol
                {
                    DeclaringSyntaxReferences: [var reference]
                } local && reference.GetSyntax() is VariableDeclaratorSyntax)
            {
                symbols.Add(local);
                fields.Add(field);
            }
        }

        return (symbols, fields);
    }

    // The fields whose symbol is live after a split of the flow graph: the ones that GSH0039 of the analyzer reports
    private static HashSet<VariableDeclaratorSyntax> FindReportedByAnalyzer(SplicedFlowGraph graph,
        LivenessRecorder liveness, List<ISymbol> symbols, List<VariableDeclaratorSyntax> fields)
    {
        var reported = new HashSet<VariableDeclaratorSyntax>();
        if (symbols.Count == 0)
        {
            return reported;
        }

        var live = CrossBarrierLiveness.FindLiveAcrossSplit(graph, liveness, symbols);
        for (var i = 0; i < live.Length; i++)
        {
            if (live[i])
            {
                reported.Add(fields[i]);
            }
        }

        return reported;
    }

    // The reads, writes and jumps of one state, in the order they run: a jump records how many accesses precede it, so
    // that the writes before it can be told from the writes after it
    private sealed class StateEvents
    {
        private readonly List<(DeclarationKey Key, bool IsWrite)> _accesses = [];
        private readonly List<(int Target, int AccessCount)> _jumps = [];

        public void AddAccess(DeclarationKey key, bool isWrite) => _accesses.Add((key, isWrite));

        public void AddJump(int target) => _jumps.Add((target, _accesses.Count));

        // Whether entering the state with the local unset reaches a read of it before a write: its own read comes
        // first, or a goto case that no write precedes leads to a state that is live
        public bool IsLiveIn(DeclarationKey key, HashSet<int> live)
        {
            var written = false;
            var next = 0;
            for (var i = 0; i <= _accesses.Count; i++)
            {
                while (next < _jumps.Count && _jumps[next].AccessCount == i)
                {
                    if (!written && live.Contains(_jumps[next].Target))
                    {
                        return true;
                    }

                    next++;
                }

                if (i == _accesses.Count || !_accesses[i].Key.Equals(key))
                {
                    continue;
                }

                if (!_accesses[i].IsWrite)
                {
                    return !written;
                }

                written = true;
            }

            return false;
        }
    }

    private sealed class EventWalker(HashSet<DeclarationKey> keys, SemanticModel semanticModel, StateEvents events)
        : CSharpSyntaxWalker
    {
        private int _suspendingCallDepth;

        // The statements after a jump or a return never run: lowering leaves a goto case behind the jump that ends a
        // state
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
                events.AddJump(target);
            }

            base.VisitGotoStatement(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var suspends = node.Expression is IdentifierNameSyntax
            {
                Identifier.ValueText: GSharpInternalNames.SuspendingCall.ResumeCall
            };
            _suspendingCallDepth += suspends ? 1 : 0;
            base.VisitInvocationExpression(node);
            _suspendingCallDepth -= suspends ? 1 : 0;
        }

        // The value is evaluated before the local is written
        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.SimpleAssignmentExpression) && TryGetKey(node.Left) is { } key)
            {
                Visit(node.Right);
                events.AddAccess(key, isWrite: true);
                return;
            }

            base.VisitAssignmentExpression(node);
        }

        public override void VisitArgument(ArgumentSyntax node)
        {
            if (node.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) && TryGetKey(node.Expression) is { } key)
            {
                if (_suspendingCallDepth == 0 || !OutParameterSplit.IsWrittenBeforeASplit(node, semanticModel))
                {
                    events.AddAccess(key, isWrite: true);
                }

                return;
            }

            base.VisitArgument(node);
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (TryGetKey(node) is { } key)
            {
                events.AddAccess(key, isWrite: false);
                return;
            }

            base.VisitMemberAccessExpression(node);
        }

        // <ctx>-><local> when <local> is a candidate
        private DeclarationKey? TryGetKey(ExpressionSyntax expression)
        {
            if (expression is not MemberAccessExpressionSyntax access ||
                !ContextLocalRewriter.IsContextPointerAccess(access))
            {
                return null;
            }

            var key = LoweringIdentity.ForReference(access.Name, semanticModel);
            return key.Symbol != null && keys.Contains(key) ? key : null;
        }
    }
}

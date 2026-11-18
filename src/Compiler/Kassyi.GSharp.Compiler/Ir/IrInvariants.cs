using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>
/// Checks the invariants that every IR tree keeps (positions, value categories, arguments, variables, scopes), for
/// tests and for the passes that rewrite the tree.
/// </summary>
/// <remarks>
/// Each violation is one line that starts with the number of the invariant (<c>I7 ...</c>). Whether a node that is not
/// synthesized has the span of a node of its tree (I2) is checked by the tests only, since it searches the tree. The
/// types keep the rest: a constant is a value (or a null pointer) with a constant value (I12), an opaque node has no
/// children (I13).
/// </remarks>
internal sealed class IrInvariants
{
    private readonly IrMethod _method;
    private readonly SyntaxTree[] _trees;
    private readonly ImmutableArray<string>.Builder _violations = ImmutableArray.CreateBuilder<string>();
    private readonly Dictionary<ISymbol, ImmutableArray<IrVariable>> _variablesBySymbol =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<IrVariable> _declaredByReference = [];

    private IrInvariants(IrMethod method, Compilation compilation)
    {
        _method = method;
        _trees = SemanticModelExtensions.GetSyntaxTrees(compilation);
    }

    /// <summary>The violations of the invariants in <paramref name="method"/>; empty when it keeps them all.</summary>
    public static ImmutableArray<string> Verify(IrMethod method, Compilation compilation)
    {
        var checker = new IrInvariants(method, compilation);
        checker.CheckSource(method.Source, method.Source);
        checker.Node(method.Body, null, ImmutableStack<ImmutableArray<IrVariable>>.Empty);
        return checker._violations.ToImmutable();
    }

    /// <summary>
    /// The violations of a tree after the classify pass: I24, every call is classified; I25, every opaque node says
    /// whether it holds a split point. Empty when it keeps both.
    /// </summary>
    public static ImmutableArray<string> VerifyClassified(IrMethod method)
    {
        var violations = ImmutableArray.CreateBuilder<string>();
        foreach (var node in IrNodeChildren.DescendantsAndSelf(method.Body))
        {
            var (number, text) = node switch
            {
                IrCall { Classification.IsClassified: false } => ("I24", "a call is not classified"),
                IrOpaqueExpression { Split: IrOpaqueSplit.Unclassified } or
                    IrOpaqueStatement { Split: IrOpaqueSplit.Unclassified } =>
                    ("I25", "an opaque node does not say whether it holds a split point"),
                _ => ((string?)null, "")
            };
            if (number != null)
            {
                var start = node.Source.Start;
                violations.Add($"{number} at {start}..{start + node.Source.Length}: {text}");
            }
        }

        return violations.ToImmutable();
    }

    /// <summary>Whether <paramref name="node"/> is or holds an opaque statement or expression anywhere.</summary>
    public static bool ContainsOpaque(IrNode node) =>
        IrNodeChildren.Exists(node, static child => child is IrOpaqueExpression or IrOpaqueStatement);

    /// <summary>
    /// The violations of the invariants of the linear form in <paramref name="statements"/>; empty when it keeps them
    /// all.
    /// </summary>
    public static ImmutableArray<string> VerifyLinear(ImmutableArray<IrStatement> statements, Compilation compilation)
    {
        var trees = SemanticModelExtensions.GetSyntaxTrees(compilation);
        var violations = ImmutableArray.CreateBuilder<string>();
        var labelCounts = new Dictionary<IrLabelSymbol, int>();
        var labelKeys = new HashSet<(IrLabelKind Kind, int Ordinal)>();

        foreach (var statement in statements)
        {
            if (statement is IrBlock or IrIf or IrWhile && IrSplitLowering.HoldsSplit(statement, compilation))
            {
                AddLinearViolation(violations, "L1", statement.Source,
                    $"a {statement.GetType().Name} that holds a split was not flattened");
            }

            CheckLinearTopLevelCalls(statement, trees, violations);

            foreach (var node in IrNodeChildren.DescendantsAndSelf(statement))
            {
                if (node is IrOpaqueStatement)
                {
                    AddLinearViolation(violations, "L3", node.Source,
                        "an opaque statement remains in the linear form");
                }
            }

            if (statement is IrLabel label)
            {
                if (!labelKeys.Add((label.Label.Kind, label.Label.Ordinal)))
                {
                    AddLinearViolation(violations, "L4", label.Source,
                        $"duplicate label ({label.Label.Kind}, {label.Label.Ordinal})");
                }

                labelCounts[label.Label] = labelCounts.TryGetValue(label.Label, out var count) ? count + 1 : 1;
                CheckAllowedLinearLabelKind(label.Label, label.Source, violations);
            }
        }

        foreach (var statement in statements)
        {
            var target = statement switch
            {
                IrGoto @goto => @goto.Target,
                IrConditionalGoto conditionalGoto => conditionalGoto.Target,
                _ => null
            };
            if (target is null)
            {
                continue;
            }

            if (!labelCounts.TryGetValue(target, out var count) || count != 1)
            {
                AddLinearViolation(violations, "L4", statement.Source,
                    $"jump target ({target.Kind}, {target.Ordinal}) is not defined by a single label");
            }

            CheckAllowedLinearLabelKind(target, statement.Source, violations);
        }

        return violations.ToImmutable();
    }

    private static void CheckLinearTopLevelCalls(IrStatement statement, SyntaxTree[] trees,
        ImmutableArray<string>.Builder violations)
    {
        switch (statement)
        {
            case IrExpressionStatement { Expression: IrCall topCall }:
                if (!RemovedCalls.IsRemoved(topCall.Method, trees[topCall.Source.TreeOrdinal]))
                {
                    CheckChildrenHaveNoSplitCall(topCall, trees, violations);
                }

                return;
            case IrLocalDeclaration { Initializer: IrCall topCall }:
                if (!RemovedCalls.IsRemoved(topCall.Method, trees[topCall.Source.TreeOrdinal]))
                {
                    CheckChildrenHaveNoSplitCall(topCall, trees, violations);
                }

                return;
            default:
                CheckNoSplitCall(statement, trees, violations);
                return;
        }
    }

    private static void CheckChildrenHaveNoSplitCall(IrNode node, SyntaxTree[] trees,
        ImmutableArray<string>.Builder violations)
    {
        foreach (var child in IrNodeChildren.Of(node))
        {
            CheckNoSplitCall(child, trees, violations);
        }
    }

    private static void CheckNoSplitCall(IrNode node, SyntaxTree[] trees, ImmutableArray<string>.Builder violations)
    {
        if (node is IrCall call)
        {
            if (RemovedCalls.IsRemoved(call.Method, trees[call.Source.TreeOrdinal]))
            {
                return;
            }

            if (call.Classification.Split.IsSplit)
            {
                AddLinearViolation(violations, "L2", call.Source,
                    "a call that splits is not at the top of a linear statement");
            }
        }

        CheckChildrenHaveNoSplitCall(node, trees, violations);
    }

    private static void CheckAllowedLinearLabelKind(IrLabelSymbol symbol, IrSource source,
        ImmutableArray<string>.Builder violations)
    {
        if (symbol.Kind is IrLabelKind.ReconvergeContinue or IrLabelKind.User)
        {
            AddLinearViolation(violations, "L5", source,
                $"label kind {symbol.Kind.ToString()} is not allowed before reconvergence/user-label passes");
        }
    }

    private static void AddLinearViolation(ImmutableArray<string>.Builder violations, string code, IrSource source,
        string text) =>
        violations.Add($"{code} at {source.Start}..{source.Start + source.Length}: {text}");

    /// <summary>
    /// The violations of the invariants of the state-machine form in <paramref name="machine"/>; empty when it keeps
    /// them all.
    /// </summary>
    public static ImmutableArray<string> VerifyStateMachine(IrStateMachine machine)
    {
        var violations = ImmutableArray.CreateBuilder<string>();
        if (machine.States.IsEmpty)
        {
            AddLinearViolation(violations, "SM1", machine.Source, "state machine has no states");
            return violations.ToImmutable();
        }

        if (machine.WarpSiteCount < 0)
        {
            AddLinearViolation(violations, "SM7", machine.Source, "warp site count is negative");
        }

        var seenFrames = new HashSet<int>();
        var publishNodes = new HashSet<IrNode>();
        var readCalls = ImmutableArray.CreateBuilder<IrRuntimeCall>();

        for (var stateIndex = 0; stateIndex < machine.States.Length; stateIndex++)
        {
            var state = machine.States[stateIndex];
            if (state.Number != stateIndex)
            {
                AddLinearViolation(violations, "SM1", state.Source,
                    $"state number {state.Number} does not match expected index {stateIndex}");
            }

            if (state.UnreachableFrom is not null)
            {
                AddLinearViolation(violations, "SM8", state.Source,
                    "unreachableFrom must be null before the unreachable-statement pass");
            }

            if (state.Statements.IsEmpty)
            {
                AddLinearViolation(violations, "SM2", state.Source, $"state {state.Number} has no statements");
                continue;
            }

            var lastIndex = state.Statements.Length - 1;
            var last = state.Statements[lastIndex];
            if (last is not (IrSuspend or IrStateJump { Condition: null } or IrReturn or IrStateMachineExit))
            {
                AddLinearViolation(violations, "SM2", last.Source,
                    $"state {state.Number} must end with suspend, unconditional jump, return, or exit, " +
                    $"not {last.GetType().Name}");
            }

            if (last is IrStateJump { Condition: null } trailingJump && trailingJump.State != state.Number + 1)
            {
                AddLinearViolation(violations, "SM5", trailingJump.Source,
                    $"trailing unconditional jump targets state {trailingJump.State}, expected {state.Number + 1}");
            }

            for (var stmtIndex = 0; stmtIndex < state.Statements.Length; stmtIndex++)
            {
                var statement = state.Statements[stmtIndex];
                if (stmtIndex < lastIndex && statement is IrSuspend or IrStateMachineExit)
                {
                    AddLinearViolation(violations, "SM2", statement.Source,
                        $"{statement.GetType().Name} may only appear as the last statement of a state");
                }

                if (statement is IrSuspend suspend)
                {
                    if (suspend.Kind == IrSuspendKind.Reconverge)
                    {
                        AddLinearViolation(violations, "SM8", suspend.Source,
                            "reconverge suspend is not allowed before the reconvergence pass");
                    }

                    if (suspend.ResumeState != state.Number + 1 || suspend.ResumeState >= machine.States.Length)
                    {
                        AddLinearViolation(violations, "SM5", suspend.Source,
                            $"suspend resume state {suspend.ResumeState} does not match {state.Number + 1}");
                    }
                }

                if (statement is IrResumeCall resume)
                {
                    if (stmtIndex != 0)
                    {
                        AddLinearViolation(violations, "SM6", resume.Source,
                            "resume call must be the first statement of its state");
                    }

                    if (resume.State != state.Number)
                    {
                        AddLinearViolation(violations, "SM6", resume.Source,
                            $"resume call state {resume.State} does not match containing state {state.Number}");
                    }

                    if (resume.CallFrame < 0 || resume.CallFrame >= machine.CallFrames.Length ||
                        !seenFrames.Add(resume.CallFrame))
                    {
                        AddLinearViolation(violations, "SM6", resume.Source,
                            $"resume call frame {resume.CallFrame} is out of range or duplicated");
                    }

                    if (resume.ResultTarget is { } target &&
                        (!target.IsDeclaration || target.Variable.Kind != IrVariableKind.Temporary))
                    {
                        AddLinearViolation(violations, "SM6", resume.Source,
                            "resume call result target must declare a temporary variable");
                    }
                }

                VerifyStateStatementNodes(statement, machine, state, stmtIndex, publishNodes, readCalls, violations);
            }
        }

        if (seenFrames.Count != machine.CallFrames.Length)
        {
            AddLinearViolation(violations, "SM6", machine.Source,
                $"call frame count {seenFrames.Count} does not match callFrames length {machine.CallFrames.Length}");
        }

        foreach (var read in readCalls)
        {
            foreach (var argument in read.Arguments)
            {
                foreach (var node in IrNodeChildren.DescendantsAndSelf(argument.Value))
                {
                    if (publishNodes.Contains(node))
                    {
                        AddLinearViolation(violations, "SM7", node.Source,
                            "warp publish and warp read share an IR node instance");
                    }
                }
            }
        }

        return violations.ToImmutable();
    }

    private static void VerifyStateStatementNodes(IrStatement statement, IrStateMachine machine, IrState state,
        int stmtIndex, HashSet<IrNode> publishNodes, ImmutableArray<IrRuntimeCall>.Builder readCalls,
        ImmutableArray<string>.Builder violations)
    {
        foreach (var node in IrNodeChildren.DescendantsAndSelf(statement))
        {
            switch (node)
            {
                case IrLabel or IrGoto or IrConditionalGoto or IrOpaqueStatement:
                    AddLinearViolation(violations, "SM3", node.Source,
                        $"{node.GetType().Name} is not allowed in the state-machine form");
                    break;
                case IrStateJump jump:
                    if (jump.State <= 0 || jump.State >= machine.States.Length)
                    {
                        AddLinearViolation(violations, "SM4", jump.Source,
                            $"state jump target {jump.State} is invalid or targets state 0");
                    }

                    break;
                case IrResumeCall when node != statement || stmtIndex != 0:
                    AddLinearViolation(violations, "SM6", node.Source,
                        "resume call must be the first top-level statement of its state");
                    break;
                case IrSuspend or IrStateMachineExit when node != statement:
                    AddLinearViolation(violations, "SM2", node.Source,
                        $"{node.GetType().Name} may only appear as a top-level statement");
                    break;
                case IrRuntimeCall runtime:
                    if (runtime.Function is IrRuntimeFunction.WarpPublish or IrRuntimeFunction.WarpRead)
                    {
                        if (runtime.Shuffle == Analysis.WarpShuffleKind.None)
                        {
                            AddLinearViolation(violations, "SM7", runtime.Source,
                                "warp runtime call has WarpShuffleKind.None");
                        }

                        if (runtime.Place is null)
                        {
                            AddLinearViolation(violations, "SM7", runtime.Source,
                                "warp runtime call has a null place");
                        }

                        if (runtime.Site < 0 || runtime.Site >= machine.WarpSiteCount)
                        {
                            AddLinearViolation(violations, "SM7", runtime.Source,
                                $"warp runtime call site {runtime.Site} is outside 0..{machine.WarpSiteCount - 1}");
                        }

                        if (runtime.Function == IrRuntimeFunction.WarpPublish)
                        {
                            foreach (var argument in runtime.Arguments)
                            {
                                foreach (var argNode in IrNodeChildren.DescendantsAndSelf(argument.Value))
                                {
                                    publishNodes.Add(argNode);
                                }
                            }
                        }
                        else
                        {
                            readCalls.Add(runtime);
                        }
                    }
                    else
                    {
                        AddLinearViolation(violations, "SM8", runtime.Source,
                            $"runtime function {runtime.Function.ToString()} is not allowed before reconvergence");
                    }

                    break;
            }
        }
    }

    // <scopes>: the locals of the blocks and sequences that enclose <node>, the innermost on top
    private void Node(IrNode node, IrNode? parent, ImmutableStack<ImmutableArray<IrVariable>> scopes)
    {
        CheckSource(node.Source, parent?.Source ?? _method.Source);
        if (IsLinearNode(node))
        {
            Report(node, "I27: a linear statement outside the split pass");
            return;
        }

        if (node is IrExpression expression)
        {
            CheckExpression(expression, parent, scopes);
        }
        else
        {
            CheckStatement(node, scopes);
        }

        var inner = node switch
        {
            IrBlock block => scopes.Push(block.Locals),
            IrSequence sequence => scopes.Push(sequence.Locals),
            IrWhile @while => scopes.Push(@while.Locals),
            _ => scopes
        };
        foreach (var child in IrNodeChildren.Of(node))
        {
            Node(child, node, inner);
        }
    }

    private static bool IsLinearNode(IrNode node) => node is
        IrLabel or IrGoto or IrConditionalGoto or IrSuspend or IrResumeCall or
        IrStateJump or IrStateMachineExit or IrRuntimeCall or IrState or IrStateMachine;

    private void Report(IrNode node, string? violation)
    {
        if (violation is not null)
        {
            Report(node.Source, violation);
        }
    }

    // "I7: ..." is reported as "I7 at <start>..<end>: ..."
    private void Report(IrSource source, string violation)
    {
        var colon = violation.IndexOf(':');
        _violations.Add(
            $"{violation[..colon]} at {source.Start}..{source.Start + source.Length}{violation[colon..]}");
    }

    // I1, I23: a position in a tree of the compilation, and a synthesized node inside its parent
    private void CheckSource(IrSource source, IrSource parent)
    {
        if (source.TreeOrdinal < 0 || source.TreeOrdinal >= _trees.Length ||
            source.Start < 0 || source.Length < 0 ||
            source.Start + source.Length > _trees[source.TreeOrdinal].Length)
        {
            Report(source, "I1: the position is outside the trees");
        }

        if (source.Synthesized && (source.TreeOrdinal != parent.TreeOrdinal || !parent.Span.Contains(source.Span)))
        {
            Report(source, "I23: a synthesized node lies outside its parent");
        }
    }

    private void CheckStatement(IrNode statement, ImmutableStack<ImmutableArray<IrVariable>> scopes)
    {
        switch (statement)
        {
            case IrLocalDeclaration declaration:
                CheckDeclaration(declaration, scopes);
                break;
            case IrReturn { Value: { } value } @return:
                Report(@return, IrNodeChecks.Return(_method.Symbol, value));
                break;
            case IrWhile @while:
                Report(@while, IrNodeChecks.WhileCondition(@while.Condition));
                break;
        }
    }

    private void CheckDeclaration(IrLocalDeclaration declaration, ImmutableStack<ImmutableArray<IrVariable>> scopes)
    {
        if (declaration.Variable.Kind != IrVariableKind.Local)
        {
            Report(declaration, "I10: a declaration declares a variable that is not a local");
        }

        CheckVariable(declaration.Variable, declaration, scopes);
    }

    private void CheckExpression(IrExpression expression, IrNode? parent,
        ImmutableStack<ImmutableArray<IrVariable>> scopes)
    {
        Report(expression, IrNodeChecks.Category(expression, IrNodeChildren.IsWriteTarget(parent, expression)));
        Report(expression, IrNodeChecks.Node(expression));
        switch (expression)
        {
            case IrVariableReference reference:
                CheckReference(reference, IrNodeChildren.IsOutArgument(parent, reference), scopes);
                break;
            case IrCall call:
                CheckArguments(call, call.Arguments, call.Method.Parameters);
                break;
            case IrPropertyReference property:
                CheckArguments(property, property.Arguments, property.Property.Parameters);
                break;
            case IrStructCreation creation:
                CheckArguments(creation, creation.Arguments,
                    creation.Constructor?.Parameters ?? ImmutableArray<IParameterSymbol>.Empty);
                break;
        }
    }

    // I7, I8, I22: one argument per parameter, in parameter order, each of the category its parameter takes
    private void CheckArguments(IrExpression holder, ImmutableArray<IrArgument> arguments,
        ImmutableArray<IParameterSymbol> parameters)
    {
        if (arguments.Length != parameters.Length)
        {
            Report(holder, "I8: the number of arguments differs from the number of parameters");
        }

        var seen = new bool[arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (index < parameters.Length && !argument.Parameter.IsEqualTo(parameters[index]))
            {
                Report(holder, "I8: an argument is not in the position of its parameter");
            }

            var position = argument.EvaluationIndex;
            if (position < 0 || position >= seen.Length || seen[position])
            {
                Report(holder, "I8: the evaluation positions of the arguments are not a permutation");
            }
            else
            {
                seen[position] = true;
            }

            Report(holder, IrNodeChecks.Argument(argument));
        }
    }

    // I17: a local is declared by at most one reference, the value of an out argument
    private void CheckReference(IrVariableReference reference, bool isOutArgument,
        ImmutableStack<ImmutableArray<IrVariable>> scopes)
    {
        CheckVariable(reference.Variable, reference, scopes);
        if (!reference.IsDeclaration)
        {
            return;
        }

        if (!isOutArgument)
        {
            Report(reference, "I17: a declaring reference is not the value of an out argument");
        }

        if (!_declaredByReference.Add(reference.Variable))
        {
            Report(reference, "I17: a local is declared by more than one reference");
        }
    }

    // I9, I19, I20: one instance per symbol and region, and every variable in scope
    private void CheckVariable(IrVariable variable, IrNode node, ImmutableStack<ImmutableArray<IrVariable>> scopes)
    {
        if (variable.Symbol is { } symbol)
        {
            CheckIdentity(symbol, variable, node);
        }

        var inScope = variable.Kind switch
        {
            IrVariableKind.Parameter => _method.Parameters.Contains(variable),
            IrVariableKind.This => variable == _method.This,
            _ => IsDeclaredIn(scopes, variable)
        };
        if (!inScope)
        {
            var number = variable.Kind == IrVariableKind.Temporary ? "I19" : "I20";
            Report(node, $"{number}: {variable.Name} is not declared in an enclosing scope of the method");
        }
    }

    private static bool IsDeclaredIn(ImmutableStack<ImmutableArray<IrVariable>> scopes, IrVariable variable)
    {
        foreach (var scope in scopes)
        {
            if (scope.Contains(variable))
            {
                return true;
            }
        }

        return false;
    }

    private void CheckIdentity(ISymbol symbol, IrVariable variable, IrNode node)
    {
        var instances = _variablesBySymbol.TryGetValue(symbol, out var known)
            ? known
            : ImmutableArray<IrVariable>.Empty;
        if (instances
            .AsStackEnumerable()
            .Any(variable, static (other, current) => other.Region == current.Region && other != current))
        {
            Report(node, "I9: two instances stand for the same variable");
        }
        else if (!instances.Contains(variable))
        {
            _variablesBySymbol[symbol] = instances.Add(variable);
        }
    }
}

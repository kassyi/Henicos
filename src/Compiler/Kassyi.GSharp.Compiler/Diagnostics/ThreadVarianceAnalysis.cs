namespace Kassyi.GSharp.Compiler.Diagnostics;

/// <summary>
/// Decides whether an expression of a kernel or device function body may have different values in different threads of
/// one block, which is what makes a branch around a block barrier divergent (GSH0037).
/// </summary>
/// <remarks>
/// <para>
/// A value is thread-variant when it depends on:
/// <list type="bullet">
///   <item><description><c>ThreadIndex</c>, the lane, warp or SM id, or the clock;</description></item>
///   <item><description>the result of an atomic operation or a warp-level operation (shuffle, vote, match, reduce);</description></item>
///   <item><description>a call to a device function whose result or <c>ref</c> / <c>out</c> parameters are thread-variant by
///   this same analysis of its body, given which of its arguments are; a call to any other method with a thread-variant
///   argument;</description></item>
///   <item><description>a local that is assigned a thread-variant value, or assigned under a thread-variant condition;</description></item>
///   <item><description>a parameter of a lambda or local function, whose callers are not tracked.</description></item>
/// </list>
/// </para>
/// <para>
/// [WHY] Two assumptions keep the analysis local to one method, and both may miss a divergent branch:
/// a parameter of a kernel or device function is uniform (every thread of a kernel receives the same arguments, and the
/// callers of a device function are not followed), and an element read with a uniform index is uniform (the threads
/// read the same memory after a barrier). A device function that takes each thread's value and returns the value it
/// reduced through shared memory (for example <c>return shared[0];</c>) therefore returns a uniform value.
/// </para>
/// </remarks>
/// <param name="semanticModel">The semantic model of the tree that declares <paramref name="method"/>.</param>
/// <param name="method">The kernel or device function to analyze.</param>
/// <param name="variantParameters">A bit for each parameter, by ordinal, that receives a thread-variant argument.</param>
/// <param name="calleesInProgress">The device functions whose analysis is running further up the call chain.</param>
internal sealed class ThreadVarianceAnalysis(
    SemanticModel semanticModel,
    MethodDeclarationSyntax method,
    ulong variantParameters = 0,
    HashSet<IMethodSymbol>? calleesInProgress = null)
{
    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<IMethodSymbol, ConcurrentDictionary<ulong, CalleeVariance>>>
        s_callees = new();

    private readonly HashSet<IMethodSymbol> _calleesInProgress = calleesInProgress ?? new(SymbolEqualityComparer.Default);

    private readonly HashSet<ISymbol> _variantSymbols = new(SymbolEqualityComparer.Default);
    private bool _propagated;

    /// <summary>Whether <paramref name="expression"/> may evaluate to different values in different threads of a block.</summary>
    public bool IsThreadVariant(SyntaxNode expression)
    {
        if (!_propagated)
        {
            _propagated = true;
            Propagate();
        }

        return ReadsVariant(expression);
    }

    // Marks locals until no assignment adds one: a local can become variant only after one it reads from has
    private void Propagate()
    {
        foreach (var node in method.DescendantNodes())
        {
            if (node is not ParameterSyntax parameter)
            {
                continue;
            }

            if (parameter.Parent is SimpleLambdaExpressionSyntax ||
                parameter.Parent?.Parent is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax ||
                (parameter.Parent?.Parent == method && IsVariantParameter(parameter)))
            {
                Mark(semanticModel.GetDeclaredSymbol(parameter));
            }
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var node in method.DescendantNodes())
            {
                changed |= node switch
                {
                    VariableDeclaratorSyntax declarator =>
                        (declarator.Initializer is { } initializer && ReadsVariant(initializer.Value) || IsInVariantRegion(declarator)) &&
                        Mark(semanticModel.GetDeclaredSymbol(declarator)),
                    AssignmentExpressionSyntax assignment =>
                        (ReadsVariant(assignment.Right) || IsInVariantRegion(assignment)) &&
                        Mark(GetAssignedLocal(assignment.Left)),
                    PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression, SyntaxKind.PreDecrementExpression) =>
                        IsInVariantRegion(prefix) && Mark(GetAssignedLocal(prefix.Operand)),
                    PostfixUnaryExpressionSyntax postfix =>
                        IsInVariantRegion(postfix) && Mark(GetAssignedLocal(postfix.Operand)),
                    ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword } argument =>
                        (argument.Parent?.Parent is InvocationExpressionSyntax call && WritesVariantArgument(call) ||
                         IsInVariantRegion(argument)) &&
                        Mark(GetAssignedLocal(argument.Expression)),
                    ForEachStatementSyntax forEach =>
                        (ReadsVariant(forEach.Expression) || IsInVariantRegion(forEach)) &&
                        Mark(semanticModel.GetDeclaredSymbol(forEach)),
                    _ => false
                };
            }
        } while (changed);
    }

    private bool Mark(ISymbol? symbol) => symbol != null && _variantSymbols.Add(symbol);

    private bool IsVariantParameter(ParameterSyntax parameter)
    {
        var ordinal = semanticModel.GetDeclaredSymbol(parameter)?.Ordinal ?? -1;
        return ordinal >= 0 && (variantParameters & ParameterBit(ordinal)) != 0;
    }

    // Parameters past the 64th share the last bit
    private static ulong ParameterBit(int ordinal) => 1UL << Math.Min(ordinal, 63);

    // The local that an assignment target writes, or null for an array element or other memory
    private ISymbol? GetAssignedLocal(ExpressionSyntax target)
    {
        while (true)
        {
            switch (target)
            {
                case MemberAccessExpressionSyntax memberAccess:
                    target = memberAccess.Expression;
                    continue;
                case ParenthesizedExpressionSyntax parenthesized:
                    target = parenthesized.Expression;
                    continue;
                case DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax designation }:
                    return semanticModel.GetDeclaredSymbol(designation);
                case IdentifierNameSyntax identifier:
                    var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
                    return symbol is ILocalSymbol or IParameterSymbol ? symbol : null;
                default:
                    return null;
            }
        }
    }

    // Whether only the threads for which a thread-variant condition holds reach the node
    private bool IsInVariantRegion(SyntaxNode node)
    {
        var child = node;
        for (var ancestor = node.Parent; ancestor is not null && ancestor != method; child = ancestor, ancestor = ancestor.Parent)
        {
            var condition = ancestor switch
            {
                IfStatementSyntax ifStatement when child != ifStatement.Condition => ifStatement.Condition,
                WhileStatementSyntax whileStatement when child != whileStatement.Condition => whileStatement.Condition,
                DoStatementSyntax doStatement when child != doStatement.Condition => doStatement.Condition,
                ForStatementSyntax { Condition: { } forCondition } forStatement when
                    child == forStatement.Statement ||
                    (child is ExpressionSyntax incrementor && forStatement.Incrementors.Contains(incrementor)) => forCondition,
                SwitchStatementSyntax switchStatement when child != switchStatement.Expression => switchStatement.Expression,
                SwitchExpressionSyntax switchExpression when child != switchExpression.GoverningExpression =>
                    switchExpression.GoverningExpression,
                ConditionalExpressionSyntax conditional when child != conditional.Condition => conditional.Condition,
                BinaryExpressionSyntax binary when child == binary.Right &&
                                                   binary.IsKind(SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression,
                                                       SyntaxKind.CoalesceExpression) => binary.Left,
                ForEachStatementSyntax forEach when child == forEach.Statement => forEach.Expression,
                _ => null
            };

            if (condition != null && ReadsVariant(condition))
            {
                return true;
            }
        }

        return false;
    }

    // The arguments of a device function call are not read here: the analysis of the callee decides what they make variant
    private bool ReadsVariant(SyntaxNode expression)
    {
        foreach (var node in expression.DescendantNodesAndSelf(n =>
                     n is not AnonymousFunctionExpressionSyntax &&
                     !(n is ArgumentListSyntax { Parent: InvocationExpressionSyntax call } && TryGetDeviceFunction(call, out _, out _))))
        {
            switch (node)
            {
                case IdentifierNameSyntax identifier when semanticModel.GetSymbolInfo(identifier).Symbol is { } symbol &&
                                                          (_variantSymbols.Contains(symbol) || IsThreadIndexMember(symbol)):
                    return true;
                case InvocationExpressionSyntax invocation when TryGetDeviceFunction(invocation, out _, out _):
                    if (GetCallVariance(invocation).ReturnsVariant)
                    {
                        return true;
                    }

                    break;
                case InvocationExpressionSyntax invocation when semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol callee &&
                                                                IsVariantIntrinsic(callee):
                    return true;
            }
        }

        return false;
    }

    // A device function with a body that is called with its arguments in order (not as an extension method)
    private bool TryGetDeviceFunction(InvocationExpressionSyntax call, [NotNullWhen(true)] out IMethodSymbol? callee,
        [NotNullWhen(true)] out MethodDeclarationSyntax? declaration)
    {
        declaration = null;
        callee = (semanticModel.GetSymbolInfo(call).Symbol as IMethodSymbol)?.OriginalDefinition;
        return callee is { ReducedFrom: null } &&
               callee.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
               callee.TryGetDeclaringSyntax(out declaration);
    }

    private static bool IsThreadIndexMember(ISymbol symbol) =>
        symbol is IPropertySymbol { ContainingType: { Name: "ThreadIndex" } type } && type.ContainingNamespace.IsCoreNamespace();

    // Values that differ between threads, or between the warps of a block
    private static bool IsVariantIntrinsic(IMethodSymbol method)
    {
        if (method.ReturnsVoid ||
            method.ContainingType is not { Name: TypeConstants.GpuType or TypeConstants.WarpType } type ||
            !type.ContainingNamespace.IsCoreNamespace())
        {
            return false;
        }

        var name = method.Name;
        return name is "LaneId" or "WarpId" or "SmId" or "Clock" or "Clock64" or "ActiveMask" or "AllSync" or "AnySync"
                   or "BallotSync" ||
               name.StartsWith("Atomic", StringComparison.Ordinal) ||
               name.StartsWith("Shuffle", StringComparison.Ordinal) ||
               name.StartsWith("Match", StringComparison.Ordinal) ||
               (name.StartsWith("Reduce", StringComparison.Ordinal) && name.EndsWith("Sync", StringComparison.Ordinal));
    }

    // A device function call writes thread-variant values through its by-reference arguments when the callee assigns its
    // by-reference parameters such values; any other call, when one of its arguments is variant
    private bool WritesVariantArgument(InvocationExpressionSyntax call) =>
        TryGetDeviceFunction(call, out _, out _) ? GetCallVariance(call).WritesVariantByReference : ReadsVariant(call);

    private CalleeVariance GetCallVariance(InvocationExpressionSyntax call)
    {
        if (!TryGetDeviceFunction(call, out var callee, out var declaration))
        {
            return default;
        }

        var mask = 0UL;
        var arguments = call.ArgumentList.Arguments;
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            var ordinal = argument.NameColon is { } nameColon ? FindParameter(callee, nameColon.Name.Identifier.ValueText) : i;
            if (ordinal >= 0 && ReadsVariant(argument.Expression))
            {
                mask |= ParameterBit(Math.Min(ordinal, callee.Parameters.Length - 1));
            }
        }

        var cache = s_callees.GetValue(semanticModel.Compilation,
                static _ => new ConcurrentDictionary<IMethodSymbol, ConcurrentDictionary<ulong, CalleeVariance>>(
                    SymbolEqualityComparer.Default))
            .GetOrAdd(callee, static _ => new ConcurrentDictionary<ulong, CalleeVariance>());
        if (cache.TryGetValue(mask, out var variance))
        {
            return variance;
        }

        // [WHY] A recursive call adds nothing that the other calls of the cycle do not; it is left out, not cached
        if (!_calleesInProgress.Add(callee))
        {
            return default;
        }

        variance = new ThreadVarianceAnalysis(semanticModel.Compilation.GetSemanticModel(declaration.SyntaxTree), declaration, mask,
            _calleesInProgress).AnalyzeAsCallee();
        _calleesInProgress.Remove(callee);
        return cache.GetOrAdd(mask, variance);
    }

    private static int FindParameter(IMethodSymbol method, string name)
    {
        foreach (var parameter in method.Parameters)
        {
            if (parameter.Name == name)
            {
                return parameter.Ordinal;
            }
        }

        return -1;
    }

    private CalleeVariance AnalyzeAsCallee()
    {
        _propagated = true;
        Propagate();

        var returnsVariant = method.ExpressionBody is { } arrow && ReadsVariant(arrow.Expression);
        foreach (var node in method.DescendantNodes(static n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
        {
            if (returnsVariant)
            {
                break;
            }

            returnsVariant = node is ReturnStatementSyntax { Expression: { } value } returnStatement &&
                             (ReadsVariant(value) || IsInVariantRegion(returnStatement));
        }

        var writesVariantByReference = false;
        foreach (var symbol in _variantSymbols)
        {
            if (symbol is IParameterSymbol { RefKind: RefKind.Ref or RefKind.Out } parameter &&
                SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, semanticModel.GetDeclaredSymbol(method)))
            {
                writesVariantByReference = true;
                break;
            }
        }

        return new CalleeVariance(returnsVariant, writesVariantByReference);
    }

    private readonly record struct CalleeVariance(bool ReturnsVariant, bool WritesVariantByReference);
}

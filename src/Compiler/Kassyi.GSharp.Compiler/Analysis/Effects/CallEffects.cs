namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The effects of calls for <see cref="ExpressionEffects"/>: invocations, local functions and delegates, the
/// evaluation of arguments by value and by reference, and what the call itself reads and writes
/// (<see cref="DeviceFunctionEffects"/>).
/// </summary>
internal static class CallEffects
{
    public static void AddInvocation(ref EffectWalker walker, InvocationExpressionSyntax invocation)
    {
        if (walker.Model.GetSymbol(invocation) is not IMethodSymbol method)
        {
            walker.AddArgumentValues(invocation.ArgumentList.Arguments);
            walker.AddUnknown();
            return;
        }

        if (method.MethodKind is MethodKind.LocalFunction or MethodKind.DelegateInvoke or
            MethodKind.AnonymousFunction)
        {
            AddLocalCall(ref walker, invocation, method);
            return;
        }

        var receiver = AddReceiver(ref walker, invocation, method);
        AddCall(ref walker, method, invocation.ArgumentList.Arguments, receiver);
    }

    // The receiver of an instance call is evaluated first (C# spec §12.6.6); a struct member reads and may write the
    // struct it is called on, which is returned as the receiver the call may write
    private static EffectRoot? AddReceiver(
        ref EffectWalker walker,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method
    )
    {
        if (method.IsStatic || invocation.Expression is not MemberAccessExpressionSyntax access)
        {
            return null;
        }

        if (!method.ContainingType.IsValueType && method.ReducedFrom == null)
        {
            walker.AddValue(access.Expression);
            return null;
        }

        var receiver = walker.AddReceiver(access.Expression);
        walker.AddRead(receiver);
        return receiver;
    }

    // A local function, or a delegate held by a local that a lambda initializes and nothing reassigns: it may read
    // any local it captures, and writes what its body writes (a captured local counts as any local). Any other
    // delegate may do anything.
    private static void AddLocalCall(
        ref EffectWalker walker,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method
    )
    {
        var model = walker.Model;
        DeviceFunctionEffect effect;
        if (method.MethodKind == MethodKind.LocalFunction)
        {
            effect = DeviceFunctionEffects.Classify(method, model.Compilation);
        }
        else if (TryGetLambda(model, invocation.Expression) is { } lambda)
        {
            effect = DeviceFunctionEffects.ClassifyLambda(lambda, model);
        }
        else
        {
            walker.AddArgumentValues(invocation.ArgumentList.Arguments);
            walker.AddUnknown();
            return;
        }

        walker.Reads.AllLocals = true;
        AddCall(ref walker, method, invocation.ArgumentList.Arguments, receiver: null, effect);
        if (effect == DeviceFunctionEffect.Memory)
        {
            walker.Writes.AllLocals = true;
        }
    }

    // The lambda that initializes the local named by expression, when no assignment changes the local
    private static AnonymousFunctionExpressionSyntax? TryGetLambda(SemanticModel model, ExpressionSyntax expression)
    {
        if (model.GetSymbol(expression) is not ILocalSymbol { IsRef: false } local ||
            !local.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var declarator) ||
            declarator.Initializer?.Value is not AnonymousFunctionExpressionSyntax lambda ||
            declarator.FirstAncestorOrSelf<BlockSyntax>() is not { } scope)
        {
            return null;
        }

        var declaringModel = model.Compilation.GetCachedSemanticModel(declarator.SyntaxTree);
        var reassigned = scope.DescendantNodes()
            .AsStackEnumerable()
            .Any((Local: local, Model: declaringModel), static (node, s) =>
                node is AssignmentExpressionSyntax assignment && s.Model.GetSymbol(assignment.Left).IsEqualTo(s.Local)
            );
        return reassigned ? null : lambda;
    }

    public static void AddCall(
        ref EffectWalker walker,
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        EffectRoot? receiver
    ) =>
        AddCall(
            ref walker,
            method,
            arguments,
            receiver,
            DeviceFunctionEffects.Classify(method, walker.Model.Compilation)
        );

    private static void AddCall(
        ref EffectWalker walker,
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        EffectRoot? receiver,
        DeviceFunctionEffect effect
    )
    {
        var references = ImmutableArray.CreateBuilder<EffectRoot>();
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            var refKind = argument.RefKindKeyword.Kind() switch
            {
                SyntaxKind.RefKeyword => RefKind.Ref,
                SyntaxKind.OutKeyword => RefKind.Out,
                SyntaxKind.InKeyword => RefKind.In,
                _ => method.FindParameter(arguments, i)?.RefKind ?? RefKind.None
            };
            if (refKind == RefKind.None)
            {
                walker.AddValue(argument.Expression);
                continue;
            }

            var root = walker.AddLocation(argument.Expression);

            // The callee reads through a ref / in / ref readonly parameter, as part of this call
            if (refKind is not RefKind.Out)
            {
                walker.AddRead(root);
            }

            if (refKind is RefKind.Ref or RefKind.Out)
            {
                references.Add(root);
            }
        }

        AddCallItself(ref walker, method, references, receiver, effect);
    }

    // A call of <method> with <reference> (a variable, or null) passed by ref, after its arguments
    public static void AddCallOf(ref EffectWalker walker, IMethodSymbol method, ISymbol? reference)
    {
        var references = ImmutableArray.CreateBuilder<EffectRoot>();
        if (reference != null)
        {
            // The callee reads and writes through the ref parameter
            var root = LocationEffects.RootOfSymbol(ref walker, reference);
            walker.AddRead(root);
            references.Add(root);
        }

        var effect = DeviceFunctionEffects.Classify(method, walker.Model.Compilation);
        AddCallItself(ref walker, method, references, receiver: null, effect);
    }

    // What the call itself reads and writes: memory unless it is pure math over scalars, and what
    // DeviceFunctionEffects says it writes (its ref / out arguments and receiver, or memory)
    private static void AddCallItself(
        ref EffectWalker walker,
        IMethodSymbol method,
        ImmutableArray<EffectRoot>.Builder references,
        EffectRoot? receiver,
        DeviceFunctionEffect effect
    )
    {
        if (!IsScalarMath(method))
        {
            walker.Reads.Memory = true;
        }

        if (effect == DeviceFunctionEffect.None)
        {
            return;
        }

        foreach (var root in references)
        {
            walker.AddWrite(root);
        }

        if (receiver is { } written)
        {
            walker.AddWrite(written);
        }

        if (effect == DeviceFunctionEffect.Memory)
        {
            walker.Writes.Memory = true;
        }
    }

    // A pure intrinsic whose parameters are all values without memory behind them (Math.Max, MathF.Sqrt ...)
    public static bool IsScalarMath(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences.Length == 0 &&
        DeviceIntrinsicEffects.IsPureIntrinsic(method) &&
        method.Parameters
            .AsStackEnumerable()
            .All(static p => p.RefKind == RefKind.None && p.Type is { IsValueType: true } and not IPointerTypeSymbol &&
                             !p.Type.IsArrayLikeType());
}

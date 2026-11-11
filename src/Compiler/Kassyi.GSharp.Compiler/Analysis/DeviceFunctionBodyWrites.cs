using static Kassyi.GSharp.Compiler.Analysis.DeviceFunctionEffects;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// What one node of the body of a function writes, as seen by the caller of the function, for
/// <see cref="DeviceFunctionEffects"/>.
/// </summary>
internal static class DeviceFunctionBodyWrites
{
    internal static DeviceFunctionEffect ClassifyNode(SyntaxNode node, IMethodSymbol method, SyntaxNode body,
        SemanticModel model, Compilation compilation, HashSet<IMethodSymbol> visiting) =>
        Max(ClassifyImplicitCalls(node, method, body, model, compilation, visiting),
            ClassifyExplicitNode(node, method, body, model, compilation, visiting));

    // The calls that no invocation spells: the Dispose of a using, a user-defined operator or conversion, the getter
    // and setter of a computed property
    private static DeviceFunctionEffect ClassifyImplicitCalls(SyntaxNode node, IMethodSymbol method, SyntaxNode body,
        SemanticModel model, Compilation compilation, HashSet<IMethodSymbol> visiting)
    {
        switch (node)
        {
            case UsingStatementSyntax or LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not (int)SyntaxKind.None }:
                return ClassifyUsing(node, method, body, model, compilation, visiting);
            case ExpressionSyntax expression:
            {
                var effect = DeviceFunctionEffect.None;
                if (model.GetSymbol(expression) is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or
                        MethodKind.Conversion } operatorMethod &&
                    expression is not InvocationExpressionSyntax)
                {
                    effect = ClassifyCalled(operatorMethod, receiver: null, method, body, model, compilation, visiting);
                }

                if (model.GetConversion(expression) is { IsUserDefined: true, MethodSymbol: { } conversion })
                {
                    effect = Max(effect,
                        ClassifyCalled(conversion, receiver: null, method, body, model, compilation, visiting));
                }

                // s.Sum is classified once, at the member access rather than at its name
                if (expression is MemberAccessExpressionSyntax ||
                    (expression is IdentifierNameSyntax &&
                     !(expression.Parent is MemberAccessExpressionSyntax parentAccess && parentAccess.Name == expression)))
                {
                    effect = Max(effect, ClassifyPropertyAccess(expression, method, body, model, compilation, visiting));
                }

                return effect;
            }
            default:
                return DeviceFunctionEffect.None;
        }
    }

    // The Dispose that a using calls on its resources, which are locals of the function
    private static DeviceFunctionEffect ClassifyUsing(SyntaxNode node, IMethodSymbol method, SyntaxNode body,
        SemanticModel model, Compilation compilation, HashSet<IMethodSymbol> visiting)
    {
        var declaration = node switch
        {
            UsingStatementSyntax statement => statement.Declaration,
            LocalDeclarationStatementSyntax local => local.Declaration,
            _ => null
        };
        var type = declaration != null
            ? model.GetTypeSymbol(declaration.Type)
            : node is UsingStatementSyntax { Expression: { } resource } ? model.GetTypeSymbol(resource) : null;
        if (type is not INamedTypeSymbol named || UsingTranspiler.FindDisposeMethod(named, compilation) is not { } dispose)
        {
            return DeviceFunctionEffect.None;
        }

        var effect = Classify(GetDefinition(dispose), compilation, visiting);
        if (effect != DeviceFunctionEffect.RefParameters)
        {
            return effect;
        }

        // Dispose changes its receiver: a local that the using declares, which the caller does not see, or the variable
        // that 'using (<expression>)' names
        return node is UsingStatementSyntax { Declaration: null, Expression: { } resourceExpression }
            ? ClassifyTarget(resourceExpression, method, body, model)
            : DeviceFunctionEffect.None;
    }

    // s.Sum, Sum (a computed property of a struct): its getter, and its setter when the access is assigned
    private static DeviceFunctionEffect ClassifyPropertyAccess(ExpressionSyntax access, IMethodSymbol method,
        SyntaxNode body, SemanticModel model, Compilation compilation, HashSet<IMethodSymbol> visiting)
    {
        if (model.GetSymbol(access) is not IPropertySymbol property || !StructAbiMembers.HasAccessorFunctions(property))
        {
            return DeviceFunctionEffect.None;
        }

        var receiver = access is MemberAccessExpressionSyntax memberAccess ? memberAccess.Expression : null;

        // s.Value++ and ++s.Value read through the getter and write through the setter, as s.Value += 1 does
        var assigned = access.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == access,
            PrefixUnaryExpressionSyntax prefix => prefix.IsKind(SyntaxKind.PreIncrementExpression,
                SyntaxKind.PreDecrementExpression),
            PostfixUnaryExpressionSyntax postfix => postfix.IsKind(SyntaxKind.PostIncrementExpression,
                SyntaxKind.PostDecrementExpression),
            _ => false
        };
        var effect = DeviceFunctionEffect.None;
        if (property.GetMethod is { } getter && (!assigned || !access.Parent.IsKind(SyntaxKind.SimpleAssignmentExpression)))
        {
            effect = ClassifyCalled(getter, receiver, method, body, model, compilation, visiting);
        }

        if (assigned && property.SetMethod is { } setter)
        {
            effect = Max(effect, ClassifyCalled(setter, receiver, method, body, model, compilation, visiting));
        }

        return effect;
    }

    // A call without arguments passed by reference: its effect, with a write to its receiver seen as a write to the
    // variable the receiver names
    private static DeviceFunctionEffect ClassifyCalled(IMethodSymbol callee, ExpressionSyntax? receiver,
        IMethodSymbol method, SyntaxNode body, SemanticModel model, Compilation compilation,
        HashSet<IMethodSymbol> visiting)
    {
        var effect = Classify(GetDefinition(callee), compilation, visiting);
        if (effect != DeviceFunctionEffect.RefParameters)
        {
            return effect;
        }

        if (callee.IsStatic || !callee.ContainingType.IsValueType)
        {
            return DeviceFunctionEffect.None;
        }

        return receiver is null or ThisExpressionSyntax
            ? ClassifyReceiverOfThis(method)
            : ClassifyTarget(receiver, method, body, model);
    }

    private static DeviceFunctionEffect ClassifyExplicitNode(SyntaxNode node, IMethodSymbol method, SyntaxNode body,
        SemanticModel model, Compilation compilation, HashSet<IMethodSymbol> visiting)
    {
        switch (node)
        {
            // A member assigned in an object or with initializer belongs to the new object
            case AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax }:
                return DeviceFunctionEffect.None;
            case AssignmentExpressionSyntax assignment:
                return ClassifyTarget(assignment.Left, method, body, model);
            case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression,
                SyntaxKind.PreDecrementExpression):
                return ClassifyTarget(prefix.Operand, method, body, model);
            case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression,
                SyntaxKind.PostDecrementExpression):
                return ClassifyTarget(postfix.Operand, method, body, model);
            case InvocationExpressionSyntax invocation:
                return ClassifyCall(invocation, invocation.ArgumentList, model.GetSymbol(invocation), method, body,
                    model, compilation, visiting);
            case BaseObjectCreationExpressionSyntax creation:
                if (model.GetTypeSymbol(creation).IsSharedMemoryType())
                {
                    return DeviceFunctionEffect.Memory;
                }

                return ClassifyCall(creation, creation.ArgumentList, model.GetSymbol(creation), method, body, model,
                    compilation, visiting);
            case ConstructorInitializerSyntax initializer:
                return ClassifyCall(initializer, initializer.ArgumentList, model.GetSymbol(initializer), method, body,
                    model, compilation, visiting);
            default:
                return DeviceFunctionEffect.None;
        }
    }

    private static DeviceFunctionEffect ClassifyCall(SyntaxNode call, ArgumentListSyntax? arguments,
        ISymbol? symbol, IMethodSymbol method, SyntaxNode body, SemanticModel model, Compilation compilation,
        HashSet<IMethodSymbol> visiting)
    {
        if (symbol is not IMethodSymbol callee)
        {
            // nameof(...) binds to no method and is a constant
            return call is ExpressionSyntax expression && model.GetSafeConstantValue(expression).HasValue
                ? DeviceFunctionEffect.None
                : DeviceFunctionEffect.Memory;
        }

        // A local function is part of the body, whose walk sees its writes; a delegate may do anything
        if (callee.MethodKind == MethodKind.LocalFunction)
        {
            return DeviceFunctionEffect.None;
        }

        if (callee.MethodKind is MethodKind.DelegateInvoke or MethodKind.AnonymousFunction)
        {
            return DeviceFunctionEffect.Memory;
        }

        var effect = Classify(GetDefinition(callee), compilation, visiting);
        if (effect != DeviceFunctionEffect.RefParameters)
        {
            return effect;
        }

        // Writes through the ref / out arguments (and the receiver): the caller sees what those name
        var result = DeviceFunctionEffect.None;
        if (arguments != null)
        {
            foreach (var argument in arguments.Arguments)
            {
                if (argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword, SyntaxKind.OutKeyword))
                {
                    result = Max(result, ClassifyTarget(argument.Expression, method, body, model));
                }
            }
        }

        if (!callee.IsStatic && callee.MethodKind != MethodKind.Constructor && callee.ContainingType.IsValueType)
        {
            result = Max(result, call is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access }
                ? ClassifyTarget(access.Expression, method, body, model)
                : ClassifyReceiverOfThis(method));
        }

        // c.Inc() of 'static void Inc(this ref Cell c)' passes c by reference, with no ref keyword at the call
        if (callee.ReducedFrom is { Parameters: [{ RefKind: RefKind.Ref }, ..] } &&
            call is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax receiver })
        {
            result = Max(result, ClassifyTarget(receiver.Expression, method, body, model));
        }

        return result;
    }

    // The variable that writing to target changes, as seen by the caller of method
    private static DeviceFunctionEffect ClassifyTarget(ExpressionSyntax target, IMethodSymbol method, SyntaxNode body,
        SemanticModel model)
    {
        switch (target)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return ClassifyTarget(parenthesized.Expression, method, body, model);
            case TupleExpressionSyntax tuple:
            {
                var effect = DeviceFunctionEffect.None;
                foreach (var argument in tuple.Arguments)
                {
                    effect = Max(effect, ClassifyTarget(argument.Expression, method, body, model));
                }

                return effect;
            }
            case DeclarationExpressionSyntax:
                return DeviceFunctionEffect.None;
            case IdentifierNameSyntax:
                return ClassifyVariable(model.GetSymbol(target), method, body);
            case MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression):
            {
                var member = model.GetSymbol(access);
                if (member is IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true })
                {
                    return DeviceFunctionEffect.Memory;
                }

                // A field of a struct is part of the struct; a member of anything else is memory
                if (model.GetTypeSymbol(access.Expression) is { IsValueType: true })
                {
                    return access.Expression is ThisExpressionSyntax
                        ? ClassifyReceiverOfThis(method)
                        : ClassifyTarget(access.Expression, method, body, model);
                }

                return DeviceFunctionEffect.Memory;
            }
            default:
                return DeviceFunctionEffect.Memory;
        }
    }

    private static DeviceFunctionEffect ClassifyVariable(ISymbol? variable, IMethodSymbol method, SyntaxNode body) =>
        variable switch
        {
            ILocalSymbol { IsRef: false } local => IsDeclaredIn(local, body)
                ? DeviceFunctionEffect.None
                : DeviceFunctionEffect.Memory,
            IParameterSymbol { RefKind: RefKind.None } parameter => IsOwnParameter(parameter, method, body)
                ? DeviceFunctionEffect.None
                : DeviceFunctionEffect.Memory,
            IParameterSymbol { RefKind: RefKind.Ref or RefKind.Out } parameter =>
                parameter.ContainingSymbol.IsEqualTo(method)
                    ? DeviceFunctionEffect.RefParameters
                    : DeviceFunctionEffect.Memory,
            IFieldSymbol { IsStatic: false } => ClassifyReceiverOfThis(method),
            _ => DeviceFunctionEffect.Memory
        };

    // A field of the struct that method belongs to: its own object in a constructor, the receiver otherwise
    private static DeviceFunctionEffect ClassifyReceiverOfThis(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Constructor
            ? DeviceFunctionEffect.None
            : method is { IsStatic: false, ContainingType.IsValueType: true }
                ? DeviceFunctionEffect.RefParameters
                : DeviceFunctionEffect.Memory;

    // A by-value parameter of the function itself, or of a lambda or local function inside it (a captured parameter of
    // an enclosing function is the caller's)
    private static bool IsOwnParameter(IParameterSymbol parameter, IMethodSymbol method, SyntaxNode body) =>
        parameter.ContainingSymbol.OriginalDefinition.IsEqualTo(method) || IsDeclaredIn(parameter, body);

    // A local of the function itself, lambdas and local functions inside it included (a captured local of an
    // enclosing function is the caller's)
    private static bool IsDeclaredIn(ISymbol variable, SyntaxNode body) =>
        variable.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(body, static (reference, b) => reference.SyntaxTree == b.SyntaxTree && b.FullSpan.Contains(reference.Span));
}

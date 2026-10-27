namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>What a call of a function may write, besides its own locals and by-value parameters.</summary>
internal enum DeviceFunctionEffect
{
    /// <summary>Writes nothing that the caller can see.</summary>
    None,

    /// <summary>Writes only through its <c>ref</c> / <c>out</c> parameters (and, for a struct member, its receiver).</summary>
    RefParameters,

    /// <summary>May write any memory: array elements, pointers, shared memory, static fields, or calls that do.</summary>
    Memory
}

/// <summary>
/// Classifies what a call of a <c>[GpuDeviceFunction]</c>, a struct constructor or member, or an intrinsic may write
/// (<see cref="DeviceFunctionEffect"/>), so that <see cref="ExpressionEffects"/> knows which calls can change what
/// another operand of the same expression reads.
/// </summary>
/// <remarks>
/// <para>
/// A function with a body is classified by walking it: every assignment, increment, <c>ref</c> / <c>out</c> argument and
/// call is checked for the variable it writes. A write to a local or by-value parameter of the function (lambdas and
/// local functions inside it included) is invisible to the caller; a write through a <c>ref</c> / <c>out</c> parameter
/// (or to a field of the receiver of a struct member) is <see cref="DeviceFunctionEffect.RefParameters"/>; anything else
/// is <see cref="DeviceFunctionEffect.Memory"/>. A function in a call cycle is <see cref="DeviceFunctionEffect.Memory"/>.
/// </para>
/// <para>
/// A function without a body (YAML mapped, <c>[CudaIntrinsic]</c>, metadata) is classified by its declaration: the
/// atomics, asynchronous copies, inline PTX, barriers, fences and warp operations are
/// <see cref="DeviceFunctionEffect.Memory"/>; a function with <c>ref</c> / <c>out</c> parameters is
/// <see cref="DeviceFunctionEffect.RefParameters"/>; the math of <c>System</c> (<c>Math</c>, <c>MathF</c>, the methods
/// of the primitive types), <c>System.Numerics</c>, <c>FastMath</c> and the constructors of value types are
/// <see cref="DeviceFunctionEffect.None"/>; any other is <see cref="DeviceFunctionEffect.Memory"/>.
/// </para>
/// <para>
/// [WHY] Every result is cached, also those found while another function is being classified: a function only gets a
/// result that depends on the walk when it is in a cycle, and every function in a cycle is
/// <see cref="DeviceFunctionEffect.Memory"/> whichever is queried first, so the cache does not depend on the order of
/// the queries.
/// </para>
/// </remarks>
internal static class DeviceFunctionEffects
{
    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<IMethodSymbol, DeviceFunctionEffect>>
        s_effects = new();

    /// <summary>What a call of <paramref name="method"/> may write.</summary>
    public static DeviceFunctionEffect Classify(IMethodSymbol method, Compilation compilation)
    {
        var definition = GetDefinition(method);
        return GetCache(compilation).TryGetValue(definition, out var cached)
            ? cached
            : Classify(definition, compilation, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
    }

    private static ConcurrentDictionary<IMethodSymbol, DeviceFunctionEffect> GetCache(Compilation compilation) =>
        s_effects.GetValue(compilation,
            static _ => new ConcurrentDictionary<IMethodSymbol, DeviceFunctionEffect>(SymbolEqualityComparer.Default));

    // The declaration of a constructed generic method, or the static method of a reduced extension call
    private static IMethodSymbol GetDefinition(IMethodSymbol method) => (method.ReducedFrom ?? method).OriginalDefinition;

    private static DeviceFunctionEffect Classify(IMethodSymbol definition, Compilation compilation,
        HashSet<IMethodSymbol> visiting)
    {
        var cache = GetCache(compilation);
        if (cache.TryGetValue(definition, out var cached))
        {
            return cached;
        }

        if (!visiting.Add(definition))
        {
            return DeviceFunctionEffect.Memory;
        }

        var effect = TryGetBodies(definition, compilation, out var bodies)
            ? ClassifyBodies(definition, bodies, compilation, visiting)
            : ClassifyWithoutBody(definition, compilation);
        visiting.Remove(definition);
        return cache.GetOrAdd(definition, effect);
    }

    // The syntax that runs when the function is called: the body of a method or accessor, or the body and constructor
    // initializer of a constructor together with the instance member initializers of its struct
    // Only syntax of the compilation counts: a function of another assembly has none here, also when an IDE gives its
    // declaration through a referenced compilation
    private static bool TryGetBodies(IMethodSymbol method, Compilation compilation, out ImmutableArray<SyntaxNode> bodies)
    {
        var builder = ImmutableArray.CreateBuilder<SyntaxNode>();
        if (method.MethodKind == MethodKind.Constructor && method.ContainingType is { TypeKind: TypeKind.Struct } type &&
            !DeviceExportTable.IsOutside(type, compilation))
        {
            foreach (var reference in type.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is TypeDeclarationSyntax declaration)
                {
                    AddInstanceInitializers(declaration, builder);
                }
            }

            if (method.TryGetDeclaringSyntax<ConstructorDeclarationSyntax>(out var constructor))
            {
                AddNonNull(builder, constructor.Initializer);
                AddNonNull(builder, (SyntaxNode?)constructor.Body ?? constructor.ExpressionBody);
            }

            bodies = builder.ToImmutable();
            return true;
        }

        if (method.TryGetDeclaringSyntax<SyntaxNode>(out var syntax) && compilation.ContainsSyntaxTree(syntax.SyntaxTree) &&
            IsFunctionWithBody(method, syntax))
        {
            AddNonNull(builder, syntax switch
            {
                LocalFunctionStatementSyntax local => (SyntaxNode?)local.Body ?? local.ExpressionBody,
                BaseMethodDeclarationSyntax declaration => (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody,
                AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody,
                ArrowExpressionClauseSyntax arrow => arrow,
                _ => null
            });
            bodies = builder.ToImmutable();
            return bodies.Length > 0;
        }

        bodies = default;
        return false;
    }

    // A [GpuDeviceFunction], a member of a struct that G# defines on the device, or a local function
    private static bool IsFunctionWithBody(IMethodSymbol method, SyntaxNode syntax) =>
        (syntax is LocalFunctionStatementSyntax && method.MethodKind == MethodKind.LocalFunction) ||
        (syntax is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or ArrowExpressionClauseSyntax &&
         (method.HasAttribute(KnownAttributes.GpuDeviceFunction) ||
          (method.ContainingType is { } type && GlobalCppNames.IsEmittedUserType(type))));

    /// <summary>
    /// What a call of <paramref name="lambda"/> (a node of the compilation) may write. A write to a local it captures is
    /// <see cref="DeviceFunctionEffect.Memory"/>, as for a local function: the caller treats both as able to touch any local.
    /// </summary>
    public static DeviceFunctionEffect ClassifyLambda(AnonymousFunctionExpressionSyntax lambda, SemanticModel model)
    {
        var compilation = model.Compilation;
        if (!compilation.ContainsSyntaxTree(lambda.SyntaxTree) ||
            compilation.GetCachedSemanticModel(lambda.SyntaxTree).GetSymbolInfo(lambda).Symbol is not IMethodSymbol method)
        {
            return DeviceFunctionEffect.Memory;
        }

        return ClassifyBodies(method, ImmutableArray.Create<SyntaxNode>(lambda.Body), compilation,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
    }

    private static void AddInstanceInitializers(TypeDeclarationSyntax declaration,
        ImmutableArray<SyntaxNode>.Builder bodies)
    {
        foreach (var member in declaration.Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field when !field.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                                                       !field.Modifiers.Any(SyntaxKind.ConstKeyword):
                    foreach (var variable in field.Declaration.Variables)
                    {
                        AddNonNull(bodies, variable.Initializer);
                    }

                    break;
                case PropertyDeclarationSyntax property when !property.Modifiers.Any(SyntaxKind.StaticKeyword):
                    AddNonNull(bodies, property.Initializer);
                    break;
            }
        }
    }

    private static void AddNonNull(ImmutableArray<SyntaxNode>.Builder builder, SyntaxNode? node)
    {
        if (node != null)
        {
            builder.Add(node);
        }
    }

    private static DeviceFunctionEffect ClassifyBodies(IMethodSymbol method, ImmutableArray<SyntaxNode> bodies,
        Compilation compilation, HashSet<IMethodSymbol> visiting)
    {
        var effect = DeviceFunctionEffect.None;
        foreach (var body in bodies)
        {
            var model = compilation.GetCachedSemanticModel(body.SyntaxTree);
            foreach (var node in body.DescendantNodesAndSelf())
            {
                effect = Max(effect, ClassifyNode(node, method, body, model, compilation, visiting));
                if (effect == DeviceFunctionEffect.Memory)
                {
                    return effect;
                }
            }
        }

        return effect;
    }

    private static DeviceFunctionEffect ClassifyNode(SyntaxNode node, IMethodSymbol method, SyntaxNode body,
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
        if (model.GetSymbol(access) is not IPropertySymbol property || !StructAbiMembers.IsComputedProperty(property))
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
            default:
                return DeviceFunctionEffect.None;
        }
    }

    private static DeviceFunctionEffect ClassifyCall(ExpressionSyntax call, ArgumentListSyntax? arguments,
        ISymbol? symbol, IMethodSymbol method, SyntaxNode body, SemanticModel model, Compilation compilation,
        HashSet<IMethodSymbol> visiting)
    {
        if (symbol is not IMethodSymbol callee)
        {
            // nameof(...) binds to no method and is a constant
            return model.GetSafeConstantValue(call).HasValue ? DeviceFunctionEffect.None : DeviceFunctionEffect.Memory;
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
            IParameterSymbol { RefKind: RefKind.None } => DeviceFunctionEffect.None,
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

    // A local of the function itself, lambdas and local functions inside it included (a captured local of an
    // enclosing function is the caller's)
    private static bool IsDeclaredIn(ILocalSymbol local, SyntaxNode body) =>
        local.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(body, static (reference, b) => reference.SyntaxTree == b.SyntaxTree && b.FullSpan.Contains(reference.Span));

    // [WHY] A [GpuDeviceFunction] of another assembly is classified by what its assembly recorded (#618 would otherwise
    // take a ref parameter for the only write, and miss the memory it writes); without a record, as writing memory. A
    // constructor or a computed property of a struct of another assembly whose record brings its body runs that body,
    // which the record does not classify: it may write memory (#538 review m2)
    private static DeviceFunctionEffect ClassifyWithoutBody(IMethodSymbol method, Compilation compilation)
    {
        if (DeviceExportTable.TryFindFunction(method, compilation, out var imported))
        {
            return imported.Record.Effect switch
            {
                "None" => DeviceFunctionEffect.None,
                "RefParameters" => DeviceFunctionEffect.RefParameters,
                _ => DeviceFunctionEffect.Memory
            };
        }

        return method.HasAttribute(KnownAttributes.GpuDeviceFunction) || HasImportedBody(method, compilation)
            ? DeviceFunctionEffect.Memory
            : ClassifyDeclaration(method);
    }

    // A constructor with a body, or the getter of a computed property, of an emitted struct of another assembly
    private static bool HasImportedBody(IMethodSymbol method, Compilation compilation) =>
        method.ContainingType is { TypeKind: TypeKind.Struct } type &&
        GlobalCppNames.IsEmittedUserType(type) &&
        !type.ContainingNamespace.IsCoreNamespace() &&
        DeviceExportTable.IsOutside(type, compilation) &&
        method switch
        {
            { MethodKind: MethodKind.Constructor } => StructTranspiler.IsEmittedStructConstructor(method, compilation),
            { MethodKind: MethodKind.PropertyGet, AssociatedSymbol: IPropertySymbol property } =>
                StructAbiMembers.IsComputedProperty(property),
            _ => false
        };

    private static DeviceFunctionEffect ClassifyDeclaration(IMethodSymbol method)
    {
        if (IsMemoryIntrinsic(method))
        {
            return DeviceFunctionEffect.Memory;
        }

        if (method.Parameters
            .AsStackEnumerable()
            .Any(static p => p.RefKind is RefKind.Ref or RefKind.Out))
        {
            return DeviceFunctionEffect.RefParameters;
        }

        return IsPureIntrinsic(method) ? DeviceFunctionEffect.None : DeviceFunctionEffect.Memory;
    }

    // The atomics, asynchronous copies, inline PTX, barriers, fences and warp operations
#pragma warning disable HNT0003 // The warp type is checked with its namespace
    private static bool IsMemoryIntrinsic(IMethodSymbol method) =>
        method.Name.StartsWith("Atomic", StringComparison.Ordinal) ||
        method.Name.StartsWith(TypeConstants.GpuMacros.CopyAsync, StringComparison.Ordinal) ||
        method.Name.StartsWith(TypeConstants.GpuMacros.InlinePtx, StringComparison.Ordinal) ||
        method.Name.StartsWith("Sync", StringComparison.Ordinal) ||
        method.Name.StartsWith("Fence", StringComparison.Ordinal) ||
        method.ContainingType is { Name: TypeConstants.WarpType } warp && warp.ContainingNamespace.IsCoreNamespace();
#pragma warning restore HNT0003

    // The math of System (Math, MathF, the methods of the primitive types), System.Numerics, FastMath, and the
    // constructors of value types that have no body (the vector types, Nullable<T>, spans)
#pragma warning disable HNT0003 // The types are checked with their namespaces
    internal static bool IsPureIntrinsic(IMethodSymbol method)
    {
        var type = method.ContainingType;
        if (type == null)
        {
            return false;
        }

        if (method.MethodKind == MethodKind.Constructor && type.IsValueType)
        {
            return true;
        }

        var ns = type.ContainingNamespace;
        return type.SpecialType != SpecialType.None ||
               (ns.IsSystemNamespace() && type.Name is TypeConstants.MathType or TypeConstants.MathFType) ||
               (ns.IsSystemOrNumerics() && !ns.IsSystemNamespace()) ||
               (ns.IsCoreNamespace() && type.Name == FastMathType);
    }
#pragma warning restore HNT0003

    private const string FastMathType = "FastMath";

    private static DeviceFunctionEffect Max(DeviceFunctionEffect a, DeviceFunctionEffect b) => a > b ? a : b;
}

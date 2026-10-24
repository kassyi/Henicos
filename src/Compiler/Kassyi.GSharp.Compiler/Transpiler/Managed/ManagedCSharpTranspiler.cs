namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Transpiles a C# method declaration into a pure C# managed state machine and wrapper for host-side debugging.
/// </summary>
/// <remarks>
/// Types named from symbols go through <see cref="CSharpTypeNameWriter.AppendCsharpTypeName"/>; syntax nodes become text only in <see cref="ToSource"/>.
/// </remarks>
internal static class ManagedCSharpTranspiler
{
    /// <summary>
    /// Emits the context class and the state machine of a kernel, or returns an empty string for a kernel without a body.
    /// </summary>
    public static string Generate(in MethodEmissionPlan plan)
    {
        // A kernel has a state machine exactly when it has a body, and then always a symbol
        if (plan.Lowered is not { } lowered || plan.Symbol is not { } methodSymbol)
        {
            return string.Empty;
        }

        var methodDecl = plan.Declaration;
        var semanticModel = plan.SemanticModel;
        var loweredBody = lowered.LoweredBody;
        var contextFields = lowered.ContextFields;
        var contextName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Kernel.ContextPrefix, methodSymbol);
        var stateMachineName =
            GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Kernel.StateMachinePrefix, methodSymbol);
        // Internal-error reports name the kernel as the user wrote it.
        var kernelName = methodDecl.Identifier.ValueText;

        var (localInits, regularFields, unhoistedSymbols, renamedLocals) =
            ProcessContextFields(contextFields, semanticModel, kernelName);

        var unhoistedBody =
            new ContextLocalRewriter(unhoistedSymbols, renamedLocals, semanticModel, kernelName).Visit(loweredBody) as
                BlockSyntax
            ?? throw new InvalidOperationException(
                $"Unhoisting ref-struct fields removed the body of kernel '{kernelName}'.");
        var bodySource = RewriteAndNormalizeBody(unhoistedBody, semanticModel, isDeviceFunction: false);

        using var writer = new SourceWriter();
        EmitContextClass(writer, contextName, regularFields, ImmutableArray<IParameterSymbol>.Empty, lowered.CallSites,
            semanticModel);
        EmitStateMachineMethod(writer, contextName, stateMachineName, methodSymbol, plan.HasAutoResult, localInits,
            bodySource);
        return writer.ToString();
    }

    /// <summary>
    /// Emits the frame class and the state machine of a device function that may suspend. The state machine returns
    /// whether the call completed; the caller calls it again on every resume until it does.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="plan"/> is not a <see cref="MethodEmissionKind.SuspendingDeviceFunction"/>.
    /// </exception>
    public static string GenerateDeviceFunction(in MethodEmissionPlan plan)
    {
        // A suspending device function always has a state machine and a symbol
        if (plan.Kind != MethodEmissionKind.SuspendingDeviceFunction || plan.Lowered is not { } lowered ||
            plan.Symbol is not { } methodSymbol)
        {
            throw new ArgumentException("Only a device function that may suspend has a managed state machine.",
                nameof(plan));
        }

        var methodDecl = plan.Declaration;
        var semanticModel = plan.SemanticModel;
        var frameName =
            GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.SuspendingCall.FrameTypePrefix, methodSymbol);
        var stateMachineName =
            GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Kernel.StateMachinePrefix, methodSymbol);
        var functionName = methodDecl.Identifier.ValueText;

        var (localInits, regularFields, unhoistedSymbols, renamedLocals) =
            ProcessContextFields(lowered.ContextFields, semanticModel, functionName);
        var copiedParameters = plan.GetCopiedParameters(lowered);

        var unhoistedBody =
            new ContextLocalRewriter(unhoistedSymbols, renamedLocals, semanticModel, functionName)
                .Visit(lowered.LoweredBody) as BlockSyntax
            ?? throw new InvalidOperationException(
                $"Unhoisting ref-struct fields removed the body of device function '{functionName}'.");
        var bodySource = RewriteAndNormalizeBody(unhoistedBody, semanticModel, isDeviceFunction: true);

        using var writer = new SourceWriter();
        EmitContextClass(writer, frameName, regularFields, copiedParameters, lowered.CallSites, semanticModel);
        EmitDeviceStateMachineMethod(writer, frameName, stateMachineName, methodSymbol, copiedParameters, localInits,
            bodySource);
        return writer.ToString();
    }

#pragma warning disable RS0030 // Final emission of a node; the text is not parsed, compared, or edited afterwards
    // Generated code is left-aligned (transpiler-emission §3), so the normalized source has no indentation
    private static string ToSource(SyntaxNode node) => node.NormalizeWhitespace(indentation: "").ToFullString();
#pragma warning restore RS0030

    private static string RewriteAndNormalizeBody(BlockSyntax loweredBody, SemanticModel semanticModel,
        bool isDeviceFunction)
    {
        var rewriter = new ManagedExecutionRewriter(semanticModel, isDeviceFunction);
        var rewritten = rewriter.Visit(loweredBody) as BlockSyntax
                        ?? throw new InvalidOperationException("Managed execution rewriting removed the kernel body.");
        return ToSource(rewritten);
    }

    /// <summary>
    /// A hoisted context field with the type that <see cref="HoistedFieldTypeResolver"/> resolved for it and its name in
    /// the managed state machine.
    /// </summary>
    private readonly record struct ContextField(
        VariableDeclaratorSyntax Declarator,
        TypeSyntax DeclaredType,
        ITypeSymbol ResolvedType,
        string Name);

    private static (ImmutableArray<ContextField> LocalInits, ImmutableArray<ContextField> RegularFields,
        HashSet<ILocalSymbol> UnhoistedSymbols, Dictionary<ILocalSymbol, string> RenamedLocals)
        ProcessContextFields(
            IReadOnlyList<VariableDeclaratorSyntax>? contextFields,
            SemanticModel semanticModel,
            string kernelName)
    {
        var regularFields = ImmutableArray.CreateBuilder<ContextField>();
        var localInits = ImmutableArray.CreateBuilder<ContextField>();
        var unhoistedSymbols = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var renamedLocals = new Dictionary<ILocalSymbol, string>(SymbolEqualityComparer.Default);

        if (contextFields == null)
        {
            return (localInits.ToImmutable(), regularFields.ToImmutable(), unhoistedSymbols, renamedLocals);
        }

        var sharedNames = FindSharedNames(contextFields);
        foreach (var field in contextFields)
        {
            if (field.Parent is not VariableDeclarationSyntax declaration)
            {
                continue;
            }

            var localSymbol = semanticModel.GetDeclaredSymbol<ILocalSymbol>(field);
            var name = field.Identifier.ValueText.ToCSharpIdentifier();
            if (sharedNames.Contains(field.Identifier.ValueText))
            {
                if (localSymbol == null)
                {
                    throw new InvalidOperationException(
                        $"The hoisted local '{field.Identifier.ValueText}' of '{kernelName}' shares its name with another one and does not resolve to a symbol.");
                }

                name = GetDistinctName(localSymbol);
                renamedLocals[localSymbol] = name;
            }

            var contextField = new ContextField(field, declaration.Type,
                HoistedFieldTypeResolver.Resolve(field, semanticModel, kernelName), name);

            if (contextField.ResolvedType.IsRefLikeType)
            {
                if (localSymbol != null)
                {
                    unhoistedSymbols.Add(localSymbol);
                }

                // A ref struct local is declared again on every resume, so its initializer is emitted as is
                localInits.Add(contextField with { Declarator = AddSharedMemorySiteKeys(field, semanticModel) });
            }
            else
            {
                regularFields.Add(contextField);
            }
        }

        return (localInits.ToImmutable(), regularFields.ToImmutable(), unhoistedSymbols, renamedLocals);
    }

    // Names that more than one hoisted local declares; such locals live in sibling scopes of the user's method
    private static HashSet<string> FindSharedNames(IReadOnlyList<VariableDeclaratorSyntax> contextFields)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var shared = new HashSet<string>(StringComparer.Ordinal);
        contextFields
            .AsStackEnumerable()
            .Select(static field => field.Identifier.ValueText)
            .AddIf(shared, seen, static (name, names) => !names.Add(name));
        return shared;
    }

    // [WHY] Every field of a context class needs its own name. A local that shares its name takes the name of its field
    // in the C++ context struct, which carries the declaration ordinal; user identifiers cannot contain '__' (GSH0055),
    // so that name collides with nothing the user writes. A function-scope name does not depend on the dialect.
    private static string GetDistinctName(ILocalSymbol local) =>
        CppNameResolver.Resolve(local, local.Name, s_cppDialect).ToString();

    private static readonly StandardCppDialect s_cppDialect = new();

    private static TNode AddSharedMemorySiteKeys<TNode>(TNode node, SemanticModel semanticModel)
        where TNode : SyntaxNode
    {
        var creations = ImmutableArray.CreateBuilder<BaseObjectCreationExpressionSyntax>();
        node.DescendantNodes()
            .AsStackEnumerable()
            .OfType<BaseObjectCreationExpressionSyntax>()
            .AddIf(creations, semanticModel, static (creation, model) =>
                model.GetTypeSymbol(creation).IsSharedMemoryType()
            );

        return creations.Count == 0
            ? node
            : node.ReplaceNodes(creations,
                (original, rewritten) => AddSharedMemorySiteKey(original, rewritten, semanticModel));
    }

    // [WHY] new SharedMemory<T>(n) becomes new SharedMemory<T>(n, key): the buffer belongs to the declaration, one per
    // block, like a CUDA __shared__ variable (see SharedMemorySiteKey). The key is computed from the original node,
    // whose annotations map it to the source.
    internal static BaseObjectCreationExpressionSyntax AddSharedMemorySiteKey(
        BaseObjectCreationExpressionSyntax original,
        BaseObjectCreationExpressionSyntax rewritten, SemanticModel semanticModel)
    {
        var key = SharedMemorySiteKey.Compute(semanticModel, original);
        var keyArgument = SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression,
            SyntaxFactory.Literal(SharedMemorySiteKey.ToCSharpLiteral(key), key)));
        var arguments = rewritten.ArgumentList?.Arguments ?? default;
        if (arguments.Count == 0)
        {
            arguments = arguments.Add(SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(1))));
        }

        return rewritten.WithArgumentList(SyntaxFactory.ArgumentList(arguments.Add(keyArgument)));
    }

    /// <summary>Writes the field type: a written type as written, <c>var</c> as the resolved type.</summary>
    private static void AppendFieldType(SourceWriter writer, ContextField field)
    {
        if (field.DeclaredType.IsVar)
        {
            writer.AppendCsharpTypeName(field.ResolvedType);
        }
        else
        {
            // Without trivia: a comment above the original declaration would otherwise land inside the emitted one
            writer.Append(ToSource(field.DeclaredType.WithoutTrivia()));
        }
    }

    private static void EmitContextClass(
        SourceWriter writer,
        string contextName,
        ImmutableArray<ContextField> regularFields,
        ImmutableArray<IParameterSymbol> copiedParameters,
        IReadOnlyList<InvocationExpressionSyntax> callSites,
        SemanticModel semanticModel)
    {
        writer.AppendLine($"public class {contextName} {{");
        writer.AppendLine($"public int {GSharpInternalNames.StateMachine.State};");
        writer.AppendLine($"public uint {GSharpInternalNames.StateMachine.WarpExchange};");
        writer.AppendLine($"public bool {GSharpInternalNames.StateMachine.YieldedForGridSync};");

        foreach (var parameter in copiedParameters)
        {
            writer.Append("public ");
            writer.AppendCsharpTypeName(parameter.Type);
            writer.AppendLine($" {parameter.Name.ToCSharpIdentifier()};");
        }

        foreach (var field in regularFields)
        {
            writer.Append("public ");
            AppendFieldType(writer, field);
            writer.AppendLine($" {field.Name};");
        }

        // Each call that may suspend keeps the callee's frame, which is declared in the callee's class
        for (var i = 0; i < callSites.Count; i++)
        {
            var callee = semanticModel.GetSymbol(callSites[i]) as IMethodSymbol
                         ?? throw new InvalidOperationException(
                             "A call that may suspend does not resolve to a device function.");
            writer.Append("public ");
            AppendDeviceFunctionMember(writer, callee, GSharpInternalNames.SuspendingCall.FrameTypePrefix);
            writer.AppendLine($" {GSharpInternalNames.SuspendingCall.CallFramePrefix}{i} = new();");
        }

        writer.AppendLine("}");
        writer.AppendLine();
    }

    /// <summary>Writes the qualified name of a member that G# emits next to <paramref name="function"/>.</summary>
    private static void AppendDeviceFunctionMember(SourceWriter writer, IMethodSymbol function, string prefix)
    {
        writer.AppendFullyQualifiedCsharpName(function.ContainingType);
        writer.Append($".{GlobalCppNames.GetKernelDerivedName(prefix, function)}");
    }

    internal static string GetDeviceFunctionMember(IMethodSymbol function, string prefix)
    {
        using var writer = new SourceWriter();
        AppendDeviceFunctionMember(writer, function, prefix);
        return writer.ToString();
    }

    // Out parameters become ref: a suspended call returns before it assigns them, which C# rejects for out parameters
    private static void EmitDeviceStateMachineMethod(
        SourceWriter writer,
        string frameName,
        string stateMachineName,
        IMethodSymbol methodSymbol,
        ImmutableArray<IParameterSymbol> copiedParameters,
        ImmutableArray<ContextField> localInits,
        string bodySource)
    {
        writer.Append($"public static unsafe bool {stateMachineName}(");
        foreach (var p in methodSymbol.Parameters)
        {
            writer.Append(p.RefKind switch
            {
                RefKind.Ref or RefKind.Out => "ref ",
                RefKind.In => "in ",
                _ => ""
            });
            writer.AppendCsharpTypeName(p.Type);
            writer.Append(IsCopied(copiedParameters, p)
                ? $" {GSharpInternalNames.SuspendingCall.CopiedParameterPrefix}{p.Name}, "
                : $" {p.Name.ToCSharpIdentifier()}, ");
        }

        writer.Append($"{frameName} {GSharpInternalNames.StateMachine.Context}");
        if (!methodSymbol.ReturnsVoid)
        {
            writer.Append(", ref ");
            writer.AppendCsharpTypeName(methodSymbol.ReturnType);
            writer.Append($" {GSharpInternalNames.SuspendingCall.Result}");
        }

        writer.AppendLine(") {");
        if (localInits.Length > 0)
        {
            foreach (var localInit in localInits)
            {
                AppendFieldType(writer, localInit);
                writer.Append($" {localInit.Name}");
                if (localInit.Declarator.Initializer is { } initializer)
                {
                    writer.Append($" {ToSource(initializer.WithoutTrivia())}");
                }

                writer.AppendLine(";");
            }
        }

        if (copiedParameters.Length > 0)
        {
            writer.AppendLine(
                $"if ({GSharpInternalNames.StateMachine.Context}.{GSharpInternalNames.StateMachine.State} == 0) {{");
            foreach (var parameter in copiedParameters)
            {
                writer.AppendLine(
                    $"{GSharpInternalNames.StateMachine.Context}.{parameter.Name.ToCSharpIdentifier()} = {GSharpInternalNames.SuspendingCall.CopiedParameterPrefix}{parameter.Name};");
            }

            writer.AppendLine("}");
        }

        writer.AppendLine(bodySource);
        writer.AppendLine(
            "throw new global::System.InvalidOperationException(\"The device function state machine reached an unknown state.\");");
        writer.AppendLine("}");
        writer.AppendLine();
    }

    private static bool IsCopied(ImmutableArray<IParameterSymbol> copiedParameters, IParameterSymbol parameter) =>
        copiedParameters
            .AsStackEnumerable()
            .Any(parameter, static (copied, target) => copied.IsEqualTo(target));

    private static void EmitStateMachineMethod(
        SourceWriter writer,
        string contextName,
        string stateMachineName,
        IMethodSymbol methodSymbol,
        bool hasAutoResult,
        ImmutableArray<ContextField> localInits,
        string bodySource)
    {
        EmitStateMachineSignature(writer, contextName, stateMachineName, methodSymbol, hasAutoResult);
        writer.AppendLine(") {");

        foreach (var p in methodSymbol.Parameters)
        {
            if (p.RefKind == RefKind.Out)
            {
                writer.AppendLine($"{p.Name.ToCSharpIdentifier()} = default;");
            }
        }

        foreach (var localInit in localInits)
        {
            AppendFieldType(writer, localInit);
            writer.Append($" {localInit.Name}");
            if (localInit.Declarator.Initializer is { } initializer)
            {
                writer.Append($" {ToSource(initializer.WithoutTrivia())}");
            }

            writer.AppendLine(";");
        }

        writer.AppendLine(bodySource);
        writer.AppendLine("}");
    }

    private static void EmitStateMachineSignature(
        SourceWriter writer,
        string contextName,
        string stateMachineName,
        IMethodSymbol methodSymbol,
        bool hasAutoResult)
    {
        writer.Append($"public static unsafe void {stateMachineName}(");
        foreach (var p in methodSymbol.Parameters)
        {
            writer.Append(p.RefKind switch
            {
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                _ => ""
            });
            writer.AppendCsharpTypeName(p.Type);
            writer.Append($" {p.Name.ToCSharpIdentifier()}, ");
        }

        writer.Append($"{contextName} {GSharpInternalNames.StateMachine.Context}");

        if (hasAutoResult)
        {
            writer.Append(", ref ");
            writer.AppendCsharpTypeName(methodSymbol.ReturnType);
            writer.Append(" " + GSharpInternalNames.ReturnValue.Buffer);
        }
    }
}

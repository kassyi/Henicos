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
    private static BaseObjectCreationExpressionSyntax AddSharedMemorySiteKey(
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

    private static string GetDeviceFunctionMember(IMethodSymbol function, string prefix)
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

    /// <summary>Matches <c>__gsharp_ctx-&gt;X</c>, the context access that <see cref="ContextHoister"/> emits.</summary>
    private static bool IsContextPointerAccess(SyntaxNode? node) =>
        node is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.Text: GSharpInternalNames.StateMachine.Context }
        } memberAccess &&
        memberAccess.IsKind(SyntaxKind.PointerMemberAccessExpression);

    /// <summary>
    /// Turns context accesses to ref struct locals back into locals, and renames the accesses to locals that share their
    /// name with another hoisted local (see <see cref="GetDistinctName"/>).
    /// </summary>
    private sealed class ContextLocalRewriter(
        HashSet<ILocalSymbol> unhoistedSymbols,
        Dictionary<ILocalSymbol, string> renamedLocals,
        SemanticModel semanticModel,
        string kernelName) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node) =>
            VisitCreation(node, base.VisitObjectCreationExpression(node));

        public override SyntaxNode?
            VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node) =>
            VisitCreation(node, base.VisitImplicitObjectCreationExpression(node));

        private SyntaxNode? VisitCreation(BaseObjectCreationExpressionSyntax node, SyntaxNode? visited) =>
            visited is BaseObjectCreationExpressionSyntax rewritten &&
            semanticModel.GetTypeSymbol(node).IsSharedMemoryType()
                ? AddSharedMemorySiteKey(node, rewritten, semanticModel)
                : visited;

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var visited = base.VisitMemberAccessExpression(node);
            if (!IsContextPointerAccess(visited) || visited is not MemberAccessExpressionSyntax memberAccess)
            {
                return visited;
            }

            var symbol = semanticModel.GetSymbol(memberAccess.Name) as ILocalSymbol ??
                         semanticModel.GetDeclaredSymbol<ILocalSymbol>(memberAccess.Name);
            if (symbol == null)
            {
                return memberAccess;
            }

            SimpleNameSyntax name = renamedLocals.TryGetValue(symbol, out var renamed)
                ? SyntaxFactory.IdentifierName(renamed)
                : memberAccess.Name;
            return unhoistedSymbols.Contains(symbol)
                ? name.WithTriviaFrom(memberAccess)
                : memberAccess.WithName(name);
        }

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (node.Expression is AssignmentExpressionSyntax
                {
                    Left: MemberAccessExpressionSyntax memberAccess
                } assignment &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                IsContextPointerAccess(memberAccess))
            {
                // Only the declarator-derived initialization resolves through GetDeclaredSymbol; later user assignments stay
                var declaredSymbol = semanticModel.GetDeclaredSymbol<ILocalSymbol>(memberAccess.Name);
                if (declaredSymbol != null && unhoistedSymbols.Contains(declaredSymbol))
                {
                    if (node.Parent is BlockSyntax or SwitchSectionSyntax)
                    {
                        return null;
                    }

#pragma warning disable RS0030 // Statement text for a human-readable internal-error report only
                    throw new InvalidOperationException(
                        $"Cannot remove ref-struct initialization statement in kernel '{kernelName}' for local '{declaredSymbol.Name}': statement '{node.ToFullString().Trim()}' is not inside a block or switch section.");
#pragma warning restore RS0030
                }
            }

            return base.VisitExpressionStatement(node);
        }
    }

    private sealed class ManagedExecutionRewriter(SemanticModel semanticModel, bool isDeviceFunction)
        : CSharpSyntaxRewriter
    {
        private int _inLambdaOrLocalFuncCount;

        // __gsharp_resume(Foo(args), n[, target]) becomes Class.__gsharp_statemachine_Foo(args, __gsharp_ctx.__gsharp_call_frame_n[, ref target])
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (node.Expression is not IdentifierNameSyntax
                {
                    Identifier.ValueText: GSharpInternalNames.SuspendingCall.ResumeCall
                })
            {
                return base.VisitInvocationExpression(node);
            }

            var arguments = node.ArgumentList.Arguments;
            if (arguments[0].Expression is not InvocationExpressionSyntax call ||
                arguments[1].Expression is not LiteralExpressionSyntax frameIndex ||
                semanticModel.GetSymbol(call) is not IMethodSymbol callee)
            {
                throw new InvalidOperationException("A lowered resume call does not wrap a call to a device function.");
            }

            var callArguments = SyntaxFactory.SeparatedList<ArgumentSyntax>();
            var argumentsByParameter = MatchArgumentsToParameters(call.ArgumentList.Arguments, callee);
            for (var i = 0; i < argumentsByParameter.Length; i++)
            {
                if (argumentsByParameter[i] is not { } argument)
                {
                    callArguments = callArguments.Add(SyntaxFactory.Argument(CreateDefaultArgument(callee.Parameters[i])));
                    continue;
                }

                // The frame and the result follow the user's arguments, so every argument is passed by position
                var visited = (Visit(argument) as ArgumentSyntax ?? argument).WithNameColon(null);
                callArguments = callArguments.Add(visited.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)
                    ? visited.WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword))
                    : visited);
            }

            // __gsharp_ctx.__gsharp_call_frame_<frame index>
            var frameName = GSharpInternalNames.SuspendingCall.CallFramePrefix + frameIndex.Token.ValueText;
            var frame = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context),
                SyntaxFactory.IdentifierName(frameName)
            );
            callArguments = callArguments.Add(SyntaxFactory.Argument(frame));

            if (arguments.Count <= 2)
            {
                return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.ParseExpression(GetDeviceFunctionMember(callee,
                        GSharpInternalNames.Kernel.StateMachinePrefix)),
                    SyntaxFactory.ArgumentList(callArguments));
            }

            var target = Visit(arguments[2].Expression) as ExpressionSyntax ?? arguments[2].Expression;
            callArguments = callArguments.Add(SyntaxFactory.Argument(target)
                .WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword)));

            return SyntaxFactory.InvocationExpression(
                SyntaxFactory.ParseExpression(GetDeviceFunctionMember(callee,
                    GSharpInternalNames.Kernel.StateMachinePrefix)),
                SyntaxFactory.ArgumentList(callArguments));
        }

        // [WHY] The state machine of the callee takes every parameter of the user's function followed by the frame, so
        // a named argument or an omitted optional one would misplace the frame. Index i holds the argument that
        // parameter i receives, or null when the call leaves the parameter to its default value.
        private static ArgumentSyntax?[] MatchArgumentsToParameters(SeparatedSyntaxList<ArgumentSyntax> arguments,
            IMethodSymbol callee)
        {
            var parameters = callee.Parameters;
            var matched = new ArgumentSyntax?[parameters.Length];
            for (var i = 0; i < arguments.Count; i++)
            {
                var argument = arguments[i];
                var index = argument.NameColon is { } nameColon
                    ? parameters
                        .AsStackEnumerable()
                        .IndexOf(nameColon.Name.Identifier.ValueText, static (parameter, name) => parameter.Name == name)
                    : i;
                if (index < 0 || index >= matched.Length)
                {
                    throw new InvalidOperationException(
                        $"An argument of a call that may suspend does not match a parameter of '{callee.Name}'.");
                }

                matched[index] = argument;
            }

            return matched;
        }

        // The value that C# passes for an omitted optional parameter: <literal>, (<enum type>)<literal> or default
        private static ExpressionSyntax CreateDefaultArgument(IParameterSymbol parameter)
        {
            if (!parameter.HasExplicitDefaultValue)
            {
                throw new InvalidOperationException(
                    $"A call that may suspend omits the parameter '{parameter.Name}', which has no default value.");
            }

            var value = parameter.ExplicitDefaultValue;
            var literal = value switch
            {
                null => SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression),
                bool flag => SyntaxFactory.LiteralExpression(flag
                    ? SyntaxKind.TrueLiteralExpression
                    : SyntaxKind.FalseLiteralExpression),
                float single when float.IsNaN(single) || float.IsInfinity(single) =>
                    SyntaxFactory.ParseExpression(GetNonFiniteName("float", single)),
                double number when double.IsNaN(number) || double.IsInfinity(number) =>
                    SyntaxFactory.ParseExpression(GetNonFiniteName("double", number)),
                float single => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression,
                    SyntaxFactory.Literal(single)),
                double number => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression,
                    SyntaxFactory.Literal(number)),
                _ => CreateIntegralLiteral(value)
            };

            if (parameter.Type.TypeKind != TypeKind.Enum)
            {
                return literal;
            }

            using var writer = new SourceWriter();
            writer.AppendCsharpTypeName(parameter.Type);
            return SyntaxFactory.CastExpression(SyntaxFactory.ParseTypeName(writer.ToString()), literal);
        }

        private static string GetNonFiniteName(string type, double value) =>
            type + (double.IsNaN(value) ? ".NaN" : value > 0 ? ".PositiveInfinity" : ".NegativeInfinity");

        private static LiteralExpressionSyntax CreateIntegralLiteral(object value)
        {
            var token = value switch
            {
                int number => SyntaxFactory.Literal(number),
                uint number => SyntaxFactory.Literal(number),
                long number => SyntaxFactory.Literal(number),
                ulong number => SyntaxFactory.Literal(number),
                char character => SyntaxFactory.Literal(character),
                byte or sbyte or short or ushort => SyntaxFactory.Literal(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
                _ => throw new InvalidOperationException(
                    $"A call that may suspend omits a parameter whose default value '{value}' has no C# literal.")
            };

            return SyntaxFactory.LiteralExpression(value is char
                ? SyntaxKind.CharacterLiteralExpression
                : SyntaxKind.NumericLiteralExpression, token);
        }

        public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node) =>
            VisitCreation(node, base.VisitObjectCreationExpression(node));

        public override SyntaxNode?
            VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node) =>
            VisitCreation(node, base.VisitImplicitObjectCreationExpression(node));

        private SyntaxNode? VisitCreation(BaseObjectCreationExpressionSyntax node, SyntaxNode? visited) =>
            visited is BaseObjectCreationExpressionSyntax rewritten &&
            semanticModel.GetTypeSymbol(node).IsSharedMemoryType()
                ? AddSharedMemorySiteKey(node, rewritten, semanticModel)
                : visited;

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var visited = base.VisitMemberAccessExpression(node);
            if (!IsContextPointerAccess(visited) || visited is not MemberAccessExpressionSyntax memberAccess)
            {
                return visited;
            }

            return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                memberAccess.Expression,
                memberAccess.Name).WithTriviaFrom(memberAccess);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node.Parent is MemberAccessExpressionSyntax parent && parent.Name == node)
            {
                return base.VisitIdentifierName(node);
            }

            return node.Identifier.Text switch
            {
                // The managed shuffle helpers keep the warp exchange themselves; any context will do
                GSharpInternalNames.SuspendingCall.Root =>
                    SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context).WithTriviaFrom(node),
                // __gsharp_ctx.<name>
                GSharpInternalNames.StateMachine.State
                    or GSharpInternalNames.StateMachine.WarpExchange
                    or GSharpInternalNames.StateMachine.YieldedForGridSync =>
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context),
                        node
                    ).WithTriviaFrom(node),
                // System.Numerics.BitOperations
                TypeConstants.BitOperationsType =>
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("System.Numerics"),
                        SyntaxFactory.IdentifierName(TypeConstants.BitOperationsType)
                    ).WithTriviaFrom(node),
                _ => base.VisitIdentifierName(node)
            };
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) =>
            VisitNestedCallable(node, base.VisitLocalFunctionStatement);

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) =>
            VisitNestedCallable(node, base.VisitSimpleLambdaExpression);

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) =>
            VisitNestedCallable(node, base.VisitParenthesizedLambdaExpression);

        public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) =>
            VisitNestedCallable(node, base.VisitAnonymousMethodExpression);

        private SyntaxNode? VisitNestedCallable<TNode>(TNode node, Func<TNode, SyntaxNode?> baseVisitor)
        {
            _inLambdaOrLocalFuncCount++;
            try
            {
                return baseVisitor(node);
            }
            finally
            {
                _inLambdaOrLocalFuncCount--;
            }
        }

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (node.Expression is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: TypeConstants.GpuMacros.InlinePtx }
                })
            {
                return SyntaxFactory.ParseStatement(
                        "throw new global::System.NotSupportedException(\"Gpu.InlinePtx is not supported by the StandardCppDialect dialect\");")
                    .WithTriviaFrom(node);
            }

            if (node.Expression is not InvocationExpressionSyntax invocation ||
                invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name.Identifier.Text != TypeConstants.GpuMacros.GridStrideLoop ||
                invocation.ArgumentList.Arguments.Count != 2)
            {
                return base.VisitExpressionStatement(node);
            }

            var countArg = invocation.ArgumentList.Arguments[0].Expression;
            if (invocation.ArgumentList.Arguments[1].Expression is not SimpleLambdaExpressionSyntax
                {
                    Block: not null
                } lambda)
            {
                return base.VisitExpressionStatement(node);
            }

            _inLambdaOrLocalFuncCount++;
            var loopVar = lambda.Parameter.Identifier.Text;

            var startExpr = SyntaxFactory.ParseExpression(
                "global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimX * global::Kassyi.GSharp.Core.ManagedGpuContext.BlockIdxX +" +
                " global::Kassyi.GSharp.Core.ManagedGpuContext.ThreadIdxX");
            var stepExpr = SyntaxFactory.ParseExpression(
                "global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimX * global::Kassyi.GSharp.Core.ManagedGpuContext.GridDimX");
            // Visit the count so that context accesses in it are rewritten like the rest of the body
            var countExpr = Visit(countArg) as ExpressionSyntax ?? countArg;

            // for (int i = <start>; i < (<count>); i += <step>) <body>
            var condition = SyntaxFactory.BinaryExpression(
                SyntaxKind.LessThanExpression,
                SyntaxFactory.IdentifierName(loopVar),
                SyntaxFactory.ParenthesizedExpression(countExpr)
            );

            var declaration = SyntaxFactory.VariableDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(lambda.Parameter.Identifier)
                        .WithInitializer(SyntaxFactory.EqualsValueClause(startExpr))
                )
            );

            var incrementors = SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.AddAssignmentExpression,
                    SyntaxFactory.IdentifierName(loopVar),
                    stepExpr
                )
            );

            var body = base.Visit(lambda.Block) as BlockSyntax ?? lambda.Block;
            _inLambdaOrLocalFuncCount--;

            return SyntaxFactory.ForStatement(
                declaration,
                SyntaxFactory.SeparatedList<ExpressionSyntax>(),
                condition,
                incrementors,
                body
            ).WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (isDeviceFunction && _inLambdaOrLocalFuncCount == 0)
            {
                return RewriteDeviceFunctionReturn(node);
            }

            if (_inLambdaOrLocalFuncCount != 0 ||
                node.HasAnnotations(LoweringAnnotations.StateMachineReturnAnnotationKind))
            {
                return base.VisitReturnStatement(node);
            }

            var statements = ImmutableArray.CreateBuilder<StatementSyntax>(3);

            // { [__gsharp_auto_result = <value>;] __gsharp_ctx.__gsharp_state = -1; return; }
            if (node.Expression != null)
            {
                statements.Add(
                    CreateAssignment(
                        SyntaxFactory.IdentifierName(GSharpInternalNames.ReturnValue.Buffer),
                        Visit(node.Expression) as ExpressionSyntax ?? node.Expression
                    )
                );
            }

            statements.Add(CreateContextStateAssignment(-1));
            statements.Add(SyntaxFactory.ReturnStatement());

            return SyntaxFactory.Block(statements).WithTriviaFrom(node);
        }

        // A suspension reports "not completed"; a return of the user's code writes the result, resets the frame for the
        // next call and reports completion
        private SyntaxNode RewriteDeviceFunctionReturn(ReturnStatementSyntax node)
        {
            if (node.HasAnnotations(LoweringAnnotations.StateMachineReturnAnnotationKind))
            {
                // return false;
                return SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression))
                    .WithTriviaFrom(node);
            }

            // { [__gsharp_result = <value>;] __gsharp_ctx.__gsharp_state = 0; return true; }
            var statements = ImmutableArray.CreateBuilder<StatementSyntax>(3);
            if (node.Expression != null)
            {
                statements.Add(
                    CreateAssignment(
                        SyntaxFactory.IdentifierName(GSharpInternalNames.SuspendingCall.Result),
                        Visit(node.Expression) as ExpressionSyntax ?? node.Expression
                    )
                );
            }

            statements.Add(CreateContextStateAssignment(0));
            statements.Add(
                SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)));

            return SyntaxFactory.Block(statements).WithTriviaFrom(node);
        }

        // <target> = <value>;
        private static ExpressionStatementSyntax CreateAssignment(ExpressionSyntax target, ExpressionSyntax value) =>
            SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, target, value)
            );

        // __gsharp_ctx.__gsharp_state = <state>;
        private static ExpressionStatementSyntax CreateContextStateAssignment(int state) =>
            CreateAssignment(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context),
                    SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.State)
                ),
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(state))
            );
    }
}

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Emits C# context classes and state machine methods for managed host execution.
/// </summary>
internal static class ManagedStateMachineEmitter
{
    /// <summary>
    /// The attribute that keeps a generated member out of IntelliSense: the members only the generated managed code
    /// calls (state machines, their contexts and frames, managed versions) are public when the function is, so a
    /// library's consumers would see them.
    /// </summary>
    public const string EditorBrowsableNever =
        "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]";

    // [WHY] An unsafe context or frame is a struct that holds managed references (arrays, the frames of its calls),
    // and the state machine takes it by pointer, which C# warns about (CS8500). The pointer is sound: the launcher
    // pins the contexts with fixed for the whole launch, and a frame is a field of its caller's pinned context, so the
    // GC neither moves them nor loses the references they hold (the launchers suppress the same warning around fixed)
    private const string PinnedContextWarningDisable =
        "#pragma warning disable CS8500 // The context is pinned by the launcher for the whole launch; " +
        "a frame is a field of a pinned context";

    private const string PinnedContextWarningRestore = "#pragma warning restore CS8500";

#pragma warning disable RS0030 // Final emission of a node; the text is not parsed, compared, or edited afterwards
    // Generated code is left-aligned (transpiler-emission §3), so the normalized source has no indentation
    internal static string ToSource(SyntaxNode node) => node.NormalizeWhitespace(indentation: "").ToFullString();
#pragma warning restore RS0030

    // The storage of a local declared again on every resume (ResumeLocals), before the state switch:
    // <type> <name> <initializer>;                                   (a SharedMemory<T>)
    // scoped <type> <name> = default;                                (assigned where it is declared)
    // Span<E> __gsharp_resume_storage_<name> = stackalloc E[<length>];
    // scoped <type> <name> = default;                                (a stackalloc span, bound where it is declared)
    // E* __gsharp_resume_storage_<name> = stackalloc E[<length>];
    // E* <name> = default;                                           (a stackalloc pointer)
    private static void AppendResumeLocal(SourceWriter writer, ManagedContextField local)
    {
        if (local.Resume is not { Kind: not ResumeLocalKind.Initialized } resume)
        {
            AppendFieldType(writer, local);
            writer.Append($" {local.Name}");
            if (local.Declarator.Initializer is { } initializer)
            {
                writer.Append($" {ToSource(initializer.WithoutTrivia())}");
            }

            writer.AppendLine(";");
            return;
        }

        if (resume is { Kind: ResumeLocalKind.StackAlloc, ElementType: { } elementType })
        {
            if (resume.Type is IPointerTypeSymbol)
            {
                writer.AppendCsharpTypeName(elementType);
                writer.Append("*");
            }
            else
            {
                writer.Append("global::System.Span<");
                writer.AppendCsharpTypeName(elementType);
                writer.Append(">");
            }

            writer.Append($" {local.StorageName} = stackalloc ");
            writer.AppendCsharpTypeName(elementType);
            writer.AppendLine($"[{resume.Length}];");
        }

        writer.AppendIf(resume.Type.IsRefLikeType, "scoped ");
        AppendFieldType(writer, local);
        writer.AppendLine($" {local.Name} = default;");
    }

    /// <summary>Writes the field type: a written type as written, <c>var</c> as the resolved type.</summary>
    public static void AppendFieldType(SourceWriter writer, ManagedContextField field)
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

    /// <param name="writer">The writer.</param>
    /// <param name="contextName">The class.</param>
    /// <param name="regularFields">The hoisted locals.</param>
    /// <param name="copiedParameters">The parameters that the class keeps.</param>
    /// <param name="callSites">The calls that may suspend, whose frames the class holds.</param>
    /// <param name="semanticModel">The model of the calls.</param>
    /// <param name="accessibility">The class's accessibility.</param>
    /// <param name="memberAccessibility">The accessibility of the fields other than the call frames.</param>
    /// <param name="isUnsafe">Whether the context is an unsafe struct rather than a class.</param>
    /// <param name="warpSiteCount">The warp collectives of the body (<see cref="LoweredStateMachine.WarpSiteCount"/>).</param>
    /// <remarks>
    /// A call frame field is internal: the frame of a device function that is not public is an internal class. Every
    /// context numbers its frames for the warp collectives (<c>__gsharp_number_warp_paths</c>), which is public because a
    /// frame of a public device function may belong to a caller in another assembly.
    /// </remarks>
    public static void EmitContextClass(
        SourceWriter writer,
        string contextName,
        ImmutableArray<ManagedContextField> regularFields,
        ImmutableArray<IParameterSymbol> copiedParameters,
        IReadOnlyList<InvocationExpressionSyntax> callSites,
        SemanticModel semanticModel,
        string accessibility = "public",
        string memberAccessibility = "public",
        bool isUnsafe = false,
        int warpSiteCount = 0)
    {
        writer.AppendLine(EditorBrowsableNever);
        var typeKeyword = isUnsafe ? "unsafe struct" : "class";
        var warpFrame = warpSiteCount > 0 ? $" : {ManagedWarpFrameInterface}" : "";
        using (writer.Scope($"{accessibility} {typeKeyword} {contextName}{warpFrame} {{"))
        {
            writer.AppendLine($"{memberAccessibility} int {GSharpInternalNames.StateMachine.State};");
            writer.AppendLine($"{memberAccessibility} uint {GSharpInternalNames.StateMachine.WarpPath};");

            foreach (var parameter in copiedParameters)
            {
                writer.Append($"{memberAccessibility} ");
                writer.AppendCsharpTypeName(parameter.Type);
                writer.AppendLine($" {parameter.Name.ToCSharpIdentifier()};");
            }

            foreach (var field in regularFields)
            {
                writer.Append($"{memberAccessibility} ");
                AppendFieldType(writer, field);
                writer.AppendLine($" {field.Name};");
            }

            // Each call that may suspend keeps the callee's frame, which is declared in the callee's class
            for (var i = 0; i < callSites.Count; i++)
            {
                var callee = semanticModel.GetSymbol(callSites[i]) as IMethodSymbol
                             ?? throw new InvalidOperationException(
                                 "A call that may suspend does not resolve to a device function.");
                writer.Append("internal ");
                AppendDeviceFunctionMember(writer, callee, GSharpInternalNames.SuspendingCall.FrameTypePrefix);
                writer.AppendLine(isUnsafe
                    ? $" {GSharpInternalNames.SuspendingCall.CallFramePrefix}{i};"
                    : $" {GSharpInternalNames.SuspendingCall.CallFramePrefix}{i} = new();");
            }

            EmitWarpFrameMembers(writer, memberAccessibility, isUnsafe, warpSiteCount);
            EmitWarpPathNumbering(writer, callSites.Count);
        }

        writer.AppendLine();
    }

    private const string ManagedWarpFrameInterface = "global::Kassyi.GSharp.Core.IManagedWarpFrame";

    // The visit counts of the warp collectives of the body and the frame's side of IManagedWarpFrame, which the managed
    // warp collectives read (#1468)
    private static void EmitWarpFrameMembers(SourceWriter writer, string memberAccessibility, bool isUnsafe,
        int warpSiteCount)
    {
        if (warpSiteCount == 0)
        {
            return;
        }

        var visits = GSharpInternalNames.StateMachine.WarpVisits;
        writer.AppendLine(isUnsafe
            ? $"{memberAccessibility} fixed uint {visits}[{warpSiteCount}];"
            : $"{memberAccessibility} uint[] {visits} = new uint[{warpSiteCount}];");
        writer.AppendLine($"uint {ManagedWarpFrameInterface}.WarpPath => {GSharpInternalNames.StateMachine.WarpPath};");
        writer.AppendLine($"uint {ManagedWarpFrameInterface}.AdvanceWarpVisit(int site) => ++{visits}[site];");
        writer.AppendLine($"uint {ManagedWarpFrameInterface}.GetWarpVisit(int site) => {visits}[site];");
    }

    // [WHY] The lanes of a warp meet at the same call only when they reach it through the same device function calls.
    // The launcher numbers the frames of every thread in the same order, so a frame has the same number in every lane and
    // a different one from any other frame of the thread (the native path uses the frame's offset in the context).
    // public uint __gsharp_number_warp_paths(uint next) { __gsharp_warp_path = next++; next = __gsharp_call_frame_<i>.__gsharp_number_warp_paths(next); ... return next; }
    private static void EmitWarpPathNumbering(SourceWriter writer, int callSiteCount)
    {
        var number = GSharpInternalNames.StateMachine.NumberWarpPaths;
        using (writer.Scope($"public uint {number}(uint next) {{"))
        {
            writer.AppendLine($"{GSharpInternalNames.StateMachine.WarpPath} = next++;");
            for (var i = 0; i < callSiteCount; i++)
            {
                writer.AppendLine($"next = {GSharpInternalNames.SuspendingCall.CallFramePrefix}{i}.{number}(next);");
            }

            writer.AppendLine("return next;");
        }
    }

    /// <summary>Writes the qualified name of a member that G# emits next to <paramref name="function"/>.</summary>
    public static void AppendDeviceFunctionMember(SourceWriter writer, IMethodSymbol function, string prefix)
    {
        writer.AppendFullyQualifiedCsharpName(function.ContainingType);
        writer.Append($".{GlobalCppNames.GetKernelDerivedName(prefix, function)}");
    }

    public static string GetDeviceFunctionMember(IMethodSymbol function, string prefix)
    {
        using var writer = new SourceWriter();
        AppendDeviceFunctionMember(writer, function, prefix);
        return writer.ToString();
    }

    /// <summary>Writes the state machine method of a device function that may suspend.</summary>
    public static void EmitDeviceStateMachineMethod(
        SourceWriter writer,
        string frameName,
        string stateMachineName,
        IMethodSymbol methodSymbol,
        ImmutableArray<IParameterSymbol> copiedParameters,
        ImmutableArray<ManagedContextField> localInits,
        string bodySource,
        string accessibility,
        bool isUnsafe)
    {
        writer.AppendLineIf(isUnsafe, PinnedContextWarningDisable);
        writer.AppendLine(EditorBrowsableNever);
        AppendDeviceStateMachineSignature(writer, frameName, stateMachineName, methodSymbol, copiedParameters,
            accessibility, isUnsafe);
        using (writer.Scope(" {"))
        {
            foreach (var localInit in localInits)
            {
                AppendResumeLocal(writer, localInit);
            }

            AppendCopiedParameterInitialization(writer, copiedParameters, isUnsafe);
            writer.AppendLine(bodySource);
            writer.AppendLine(
                "throw new global::System.InvalidOperationException(\"The device function state machine reached an unknown state.\");");
        }

        writer.AppendLineIf(isUnsafe, PinnedContextWarningRestore);
        writer.AppendLine();
    }

    // <accessibility> static [unsafe] bool <state machine>(<parameters>, <frame> __gsharp_ctx[, ref <result> __gsharp_result])
    // Out parameters become ref: a suspended call returns before it assigns them, which C# rejects for out parameters
    private static void AppendDeviceStateMachineSignature(SourceWriter writer, string frameName, string stateMachineName,
        IMethodSymbol methodSymbol, ImmutableArray<IParameterSymbol> copiedParameters, string accessibility, bool isUnsafe)
    {
        writer.Append($"{accessibility} static ");
        writer.AppendIf(isUnsafe, "unsafe ");
        writer.Append($"bool {stateMachineName}");
        using (writer.Parentheses())
        {
            AppendParameterList(writer, methodSymbol, copiedParameters);
            writer.Append(isUnsafe
                ? $"{frameName}* {GSharpInternalNames.StateMachine.Context}"
                : $"{frameName} {GSharpInternalNames.StateMachine.Context}");
            if (!methodSymbol.ReturnsVoid)
            {
                writer.Append(", ref ");
                writer.AppendCsharpTypeName(methodSymbol.ReturnType);
                writer.Append($" {GSharpInternalNames.SuspendingCall.Result}");
            }
        }
    }

    // <ref kind> <type> <name or __gsharp_in_name>, ... (each followed by ", "; the context parameter comes last). A
    // parameter that the body writes is renamed, because the frame keeps its copy under the plain name
    private static void AppendParameterList(SourceWriter writer, IMethodSymbol methodSymbol,
        ImmutableArray<IParameterSymbol> copiedParameters)
    {
        foreach (var p in methodSymbol.Parameters)
        {
            writer.Append(p.RefKind switch
            {
                RefKind.Ref or RefKind.Out => "ref ",
                RefKind.In => "in ",
                RefKind.RefReadOnlyParameter => "ref readonly ",
                _ => ""
            });
            writer.AppendCsharpTypeName(p.Type);
            writer.Append(IsCopied(copiedParameters, p)
                ? $" {GSharpInternalNames.StateMachine.CopiedParameterPrefix}{p.Name}, "
                : $" {p.Name.ToCSharpIdentifier()}, ");
        }
    }

    // On the first entry, the parameters the body assigns are copied into the frame; later resumes keep the frame's copy
    private static void AppendCopiedParameterInitialization(SourceWriter writer,
        ImmutableArray<IParameterSymbol> copiedParameters, bool isUnsafe)
    {
        if (copiedParameters.Length == 0)
        {
            return;
        }

        var op = isUnsafe ? "->" : ".";
        using (writer.Scope($"if ({GSharpInternalNames.StateMachine.Context}{op}{GSharpInternalNames.StateMachine.State} == 0) {{"))
        {
            foreach (var parameter in copiedParameters)
            {
                writer.AppendLine(
                    $"{GSharpInternalNames.StateMachine.Context}{op}{parameter.Name.ToCSharpIdentifier()} = {GSharpInternalNames.StateMachine.CopiedParameterPrefix}{parameter.Name};");
            }
        }
    }

    private static bool IsCopied(ImmutableArray<IParameterSymbol> copiedParameters, IParameterSymbol parameter) =>
        copiedParameters
            .AsStackEnumerable()
            .Any(parameter, static (copied, target) => copied.IsEqualTo(target));

    internal static bool HasPointerField(
        ImmutableArray<ManagedContextField> regularFields,
        ImmutableArray<IParameterSymbol> copiedParameters) =>
        copiedParameters
            .AsStackEnumerable()
            .Any(static p => p.Type is IPointerTypeSymbol or IFunctionPointerTypeSymbol) ||
        regularFields
            .AsStackEnumerable()
            .Any(static f => f.ResolvedType is IPointerTypeSymbol or IFunctionPointerTypeSymbol ||
                             f.DeclaredType.DescendantNodesAndSelf()
                                 .AsStackEnumerable()
                                 .Any(static n => n is PointerTypeSyntax or FunctionPointerTypeSyntax));

    public static void EmitStateMachineMethod(
        SourceWriter writer,
        string contextName,
        string stateMachineName,
        IMethodSymbol methodSymbol,
        bool hasAutoResult,
        ImmutableArray<IParameterSymbol> copiedParameters,
        ImmutableArray<ManagedContextField> localInits,
        string bodySource,
        bool isUnsafe = false)
    {
        writer.AppendLineIf(isUnsafe, PinnedContextWarningDisable);
        writer.AppendLine(EditorBrowsableNever);
        EmitStateMachineSignature(writer, contextName, stateMachineName, methodSymbol, hasAutoResult, copiedParameters, isUnsafe);
        using (writer.Scope(" {"))
        {
            foreach (var localInit in localInits)
            {
                AppendResumeLocal(writer, localInit);
            }

            AppendCopiedParameterInitialization(writer, copiedParameters, isUnsafe);
            writer.AppendLine(bodySource);
        }

        writer.AppendLineIf(isUnsafe, PinnedContextWarningRestore);
    }

    // Out parameters become ref: a suspended call returns before it assigns them, and resumed calls must keep previous writes
    private static void EmitStateMachineSignature(
        SourceWriter writer,
        string contextName,
        string stateMachineName,
        IMethodSymbol methodSymbol,
        bool hasAutoResult,
        ImmutableArray<IParameterSymbol> copiedParameters,
        bool isUnsafe)
    {
        writer.Append("public static ");
        writer.AppendIf(isUnsafe, "unsafe ");
        writer.Append($"void {stateMachineName}");
        using (writer.Parentheses())
        {
            AppendParameterList(writer, methodSymbol, copiedParameters);
            writer.Append(isUnsafe
                ? $"{contextName}* {GSharpInternalNames.StateMachine.Context}"
                : $"{contextName} {GSharpInternalNames.StateMachine.Context}");
            if (hasAutoResult)
            {
                writer.Append(", ref ");
                writer.AppendCsharpTypeName(methodSymbol.ReturnType);
                writer.Append(" " + GSharpInternalNames.ReturnValue.Buffer);
            }
        }
    }
}

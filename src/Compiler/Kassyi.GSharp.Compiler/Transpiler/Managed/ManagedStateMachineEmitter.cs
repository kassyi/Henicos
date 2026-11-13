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
    /// <param name="callFrames">The frames of the calls that may suspend, in call order, which the class holds.</param>
    /// <param name="accessibility">The class's accessibility.</param>
    /// <param name="memberAccessibility">The accessibility of the fields other than the call frames.</param>
    /// <param name="isUnsafe">Whether the context is an unsafe struct rather than a class.</param>
    /// <param name="warpSiteCount">The warp collectives of the body (<see cref="LoweredStateMachine.WarpSiteCount"/>).</param>
    /// <remarks>
    /// A call frame field is internal: the frame of a device function that is not public is an internal or private
    /// class (<see cref="ManagedStateMachineAvailability.GetFrameAccessibility"/>). A frame private to a class that does
    /// not contain this one is an <see cref="object"/>, which the callee's entries create on first use. Every context
    /// numbers its frames for the warp collectives (<c>__gsharp_number_warp_paths</c>), which is public because a frame
    /// of a public device function may belong to a caller in another assembly.
    /// </remarks>
    public static void EmitContextClass(
        SourceWriter writer,
        string contextName,
        ImmutableArray<ManagedContextField> regularFields,
        ImmutableArray<IParameterSymbol> copiedParameters,
        ImmutableArray<ManagedCallFrame> callFrames,
        Accessibility accessibility,
        Accessibility memberAccessibility,
        bool isUnsafe = false,
        int warpSiteCount = 0)
    {
        writer.AppendLine(EditorBrowsableNever);
        var typeKeyword = isUnsafe ? "unsafe struct" : "class";
        var warpFrame = warpSiteCount > 0 ? $" : {ManagedWarpFrameInterface}" : "";
        var members = GeneratedMemberAccessibility.ToKeyword(memberAccessibility);
        var type = GeneratedMemberAccessibility.ToKeyword(accessibility);
        using (writer.Scope($"{type} {typeKeyword} {contextName}{warpFrame} {{"))
        {
            writer.AppendLine($"{members} int {GSharpInternalNames.StateMachine.State};");
            writer.AppendLine($"{members} uint {GSharpInternalNames.StateMachine.WarpPath};");

            foreach (var parameter in copiedParameters)
            {
                writer.Append($"{members} ");
                writer.AppendCsharpTypeName(parameter.Type);
                writer.AppendLine($" {parameter.Name.ToCSharpIdentifier()};");
            }

            foreach (var field in regularFields)
            {
                writer.Append($"{members} ");
                AppendFieldType(writer, field);
                writer.AppendLine($" {field.Name};");
            }

            EmitCallFrameFields(writer, callFrames, isUnsafe);
            EmitWarpFrameMembers(writer, members, isUnsafe, warpSiteCount);
            EmitWarpPathNumbering(writer, callFrames);
        }

        writer.AppendLine();
    }

    // Each call that may suspend keeps the callee's frame, which is declared in the callee's class:
    // internal <frame type> __gsharp_call_frame_<i>[ = new()];  or  internal object? __gsharp_call_frame_<i>;
    private static void EmitCallFrameFields(SourceWriter writer, ImmutableArray<ManagedCallFrame> callFrames,
        bool isUnsafe)
    {
        for (var i = 0; i < callFrames.Length; i++)
        {
            var frameField = GSharpInternalNames.SuspendingCall.CallFramePrefix + i;
            if (callFrames[i].ThroughEntry)
            {
                writer.AppendLine($"internal object? {frameField};");
                continue;
            }

            writer.Append("internal ");
            AppendDeviceFunctionMember(writer, callFrames[i].Callee, GSharpInternalNames.SuspendingCall.FrameTypePrefix);
            writer.AppendLine(isUnsafe ? $" {frameField};" : $" {frameField} = new();");
        }
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
    // A frame held through the entries: next = <callee class>.__gsharp_number_frame_<callee>(ref __gsharp_call_frame_<i>, next);
    private static void EmitWarpPathNumbering(SourceWriter writer, ImmutableArray<ManagedCallFrame> callFrames)
    {
        var number = GSharpInternalNames.StateMachine.NumberWarpPaths;
        using (writer.Scope($"public uint {number}(uint next) {{"))
        {
            writer.AppendLine($"{GSharpInternalNames.StateMachine.WarpPath} = next++;");
            for (var i = 0; i < callFrames.Length; i++)
            {
                var frameField = GSharpInternalNames.SuspendingCall.CallFramePrefix + i;
                if (!callFrames[i].ThroughEntry)
                {
                    writer.AppendLine($"next = {frameField}.{number}(next);");
                    continue;
                }

                writer.Append("next = ");
                AppendDeviceFunctionMember(writer, callFrames[i].Callee,
                    GSharpInternalNames.SuspendingCall.NumberFramePrefix);
                writer.AppendLine($"(ref {frameField}, next);");
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
        Accessibility accessibility,
        bool isUnsafe)
    {
        // An unsafe frame is taken by pointer (ManagedPointerWarning.PinnedContext)
        using (writer.WarningSuppressed(ManagedPointerWarning.Id, ManagedPointerWarning.PinnedContext, isUnsafe))
        {
            writer.AppendLine(EditorBrowsableNever);
            var context = GSharpInternalNames.StateMachine.Context;
            AppendDeviceStateMachineSignature(writer, isUnsafe ? $"{frameName}* {context}" : $"{frameName} {context}",
                stateMachineName, methodSymbol, copiedParameters, GeneratedMemberAccessibility.ToKeyword(accessibility),
                isUnsafe);
            using (writer.Scope(" {"))
            {
                foreach (var localInit in localInits)
                {
                    AppendResumeLocal(writer, localInit);
                }

                AppendCopiedParameterInitialization(writer, copiedParameters, isUnsafe);
                writer.AppendLine(bodySource);
                writer.AppendLine("throw new global::System.InvalidOperationException(" +
                                  "\"The device function state machine reached an unknown state.\");");
            }
        }

        writer.AppendLine();
    }

    /// <summary>
    /// Writes the entries of a device function whose frame is private to its class
    /// (<see cref="ManagedStateMachineAvailability.HasEntries"/>), through which a caller of another class holds the frame
    /// as an <see cref="object"/>: one creates the frame when it is missing and numbers it for the warp collectives, the
    /// other creates it when it is missing and runs the state machine on it.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="frameName">The frame type.</param>
    /// <param name="stateMachineName">The state machine.</param>
    /// <param name="methodSymbol">The device function.</param>
    /// <param name="copiedParameters">The parameters that the frame keeps.</param>
    /// <param name="accessibility">
    /// The entries' accessibility: the function's declared one (public, internal, protected, protected internal or private
    /// protected).
    /// </param>
    /// <param name="isUnsafe">
    /// Whether the frame is an unsafe struct, which the entries keep in a one-element array on the pinned object heap
    /// and take by pointer.
    /// </param>
    /// <remarks>
    /// [WHY] A caller outside the class cannot name a private frame, but it can hold one as an object, and only the
    /// class's own members cast it back. The frame is created on first use rather than by the caller's initializer, so
    /// that an unsafe context (a struct without a constructor) holds it as well. An unsafe frame may keep pointers into
    /// itself across a resume (the address of a hoisted local), which the direct form keeps valid because the frame is a
    /// field of the context the launcher pins for the whole launch; a boxed frame could move between two calls, so the
    /// entries allocate it pinned (<c>GC.AllocateArray(1, pinned: true)</c>) and reach it in place, never through a copy.
    /// </remarks>
    public static void EmitFrameEntries(
        SourceWriter writer,
        string frameName,
        string stateMachineName,
        IMethodSymbol methodSymbol,
        ImmutableArray<IParameterSymbol> copiedParameters,
        Accessibility accessibility,
        bool isUnsafe)
    {
        var context = GSharpInternalNames.StateMachine.Context;
        var keyword = ToEntryKeyword(accessibility);
        // ((<frame>)(__gsharp_ctx ??= new <frame>()))  or  ((<frame>[])(__gsharp_ctx ??= GC.AllocateArray<<frame>>(1, pinned: true)))[0]
        var frame = isUnsafe
            ? $"(({frameName}[])({context} ??= {PinnedArray}<{frameName}>(1, pinned: true)))[0]"
            : $"(({frameName})({context} ??= new {frameName}()))";
        using (writer.WarningSuppressed(ManagedPointerWarning.Id, ManagedPointerWarning.PinnedContext, isUnsafe))
        {
            // <accessibility> static uint __gsharp_number_frame_<path>(ref object? __gsharp_ctx, uint next) => <frame>.__gsharp_number_warp_paths(next);
            var number = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.SuspendingCall.NumberFramePrefix,
                methodSymbol);
            writer.AppendLine(EditorBrowsableNever);
            writer.AppendLine(
                $"{keyword} static uint {number}(ref object? {context}, uint next) => " +
                $"{frame}.{GSharpInternalNames.StateMachine.NumberWarpPaths}(next);");

            writer.AppendLine(EditorBrowsableNever);
            AppendDeviceStateMachineSignature(writer, $"ref object? {context}",
                GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.SuspendingCall.EnterPrefix, methodSymbol),
                methodSymbol, copiedParameters, keyword, isUnsafe);
            AppendEnterBody(writer, frameName, stateMachineName, methodSymbol, copiedParameters, frame, isUnsafe);
        }

        writer.AppendLine();
    }

    // The body of the entry that runs the state machine on <frame>, after its signature:
    //  => <state machine>(<arguments>, <frame>[, ref __gsharp_result]);
    // or, for an unsafe frame (the array never moves; fixed only lets C# take the address of its element):
    //  { fixed (<frame type>* __gsharp_frame = &<frame>) { return <state machine>(<arguments>, __gsharp_frame[, ...]); } }
    private static void AppendEnterBody(SourceWriter writer, string frameName, string stateMachineName,
        IMethodSymbol methodSymbol, ImmutableArray<IParameterSymbol> copiedParameters, string frame, bool isUnsafe)
    {
        if (!isUnsafe)
        {
            writer.Append(" => ");
            AppendStateMachineCall(writer, stateMachineName, methodSymbol, copiedParameters, frame);
            writer.AppendLine(";");
            return;
        }

        using (writer.Scope(" {"))
        {
            using (writer.Scope($"fixed ({frameName}* {EntryFramePointer} = &{frame}) {{"))
            {
                writer.Append("return ");
                AppendStateMachineCall(writer, stateMachineName, methodSymbol, copiedParameters, EntryFramePointer);
                writer.AppendLine(";");
            }
        }
    }

    // The keyword of a declared accessibility that the entries copy from their function
    private static string ToEntryKeyword(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => throw new ArgumentOutOfRangeException(nameof(accessibility), accessibility,
            "The entries of a frame belong to a function that is not private.")
    };

    private const string PinnedArray = "global::System.GC.AllocateArray";

    // The pointer to the pinned frame in the entry of an unsafe frame; a local of the entry, which has no other
    private const string EntryFramePointer = "__gsharp_frame";

    // <state machine>(<ref kind> <parameter>, ..., <frame>[, ref __gsharp_result]): the entry's parameters passed on as
    // the state machine declares them
    private static void AppendStateMachineCall(SourceWriter writer, string stateMachineName, IMethodSymbol methodSymbol,
        ImmutableArray<IParameterSymbol> copiedParameters, string frame)
    {
        writer.Append(stateMachineName);
        using (writer.Parentheses())
        {
            foreach (var p in methodSymbol.Parameters)
            {
                writer.Append(p.RefKind switch
                {
                    RefKind.Ref or RefKind.Out => "ref ",
                    RefKind.In or RefKind.RefReadOnlyParameter => "in ",
                    _ => ""
                });
                writer.Append(IsCopied(copiedParameters, p)
                    ? $"{GSharpInternalNames.StateMachine.CopiedParameterPrefix}{p.Name}, "
                    : $"{p.Name.ToCSharpIdentifier()}, ");
            }

            writer.Append(frame);
            writer.AppendIf(!methodSymbol.ReturnsVoid, $", ref {GSharpInternalNames.SuspendingCall.Result}");
        }
    }

    // <accessibility> static [unsafe] bool <name>(<parameters>, <frame parameter type> __gsharp_ctx[, ref <result> __gsharp_result])
    // with <frame parameter type> the frame, a pointer to an unsafe frame, or what the caller passes it as. Out parameters
    // become ref: a suspended call returns before it assigns them, which C# rejects for out parameters
    private static void AppendDeviceStateMachineSignature(SourceWriter writer, string frameParameter, string name,
        IMethodSymbol methodSymbol, ImmutableArray<IParameterSymbol> copiedParameters, string accessibility, bool isUnsafe)
    {
        writer.Append($"{accessibility} static ");
        writer.AppendIf(isUnsafe, "unsafe ");
        writer.Append($"bool {name}");
        using (writer.Parentheses())
        {
            AppendParameterList(writer, methodSymbol, copiedParameters);
            writer.Append(frameParameter);
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
        Accessibility accessibility,
        bool isUnsafe = false)
    {
        // An unsafe context is taken by pointer (ManagedPointerWarning.PinnedContext)
        using var suppression =
            writer.WarningSuppressed(ManagedPointerWarning.Id, ManagedPointerWarning.PinnedContext, isUnsafe);
        writer.AppendLine(EditorBrowsableNever);
        EmitStateMachineSignature(writer, contextName, stateMachineName, methodSymbol, hasAutoResult, copiedParameters,
            GeneratedMemberAccessibility.ToKeyword(accessibility), isUnsafe);
        using (writer.Scope(" {"))
        {
            foreach (var localInit in localInits)
            {
                AppendResumeLocal(writer, localInit);
            }

            AppendCopiedParameterInitialization(writer, copiedParameters, isUnsafe);
            writer.AppendLine(bodySource);
        }
    }

    // Out parameters become ref: a suspended call returns before it assigns them, and resumed calls must keep previous writes
    private static void EmitStateMachineSignature(
        SourceWriter writer,
        string contextName,
        string stateMachineName,
        IMethodSymbol methodSymbol,
        bool hasAutoResult,
        ImmutableArray<IParameterSymbol> copiedParameters,
        string accessibility,
        bool isUnsafe)
    {
        writer.Append($"{accessibility} static ");
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

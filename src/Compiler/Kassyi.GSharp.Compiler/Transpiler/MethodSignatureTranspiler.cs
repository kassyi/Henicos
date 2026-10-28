namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Provides code emission services for method signatures, parameter qualifiers, and target-specific function prefixes.
/// </summary>
internal static class MethodSignatureTranspiler
{
    /// <summary>
    /// Writes the signature of a kernel entry point or of a device function that does not suspend. A kernel with a state
    /// machine (<paramref name="hasContext"/>) takes its per-thread context as the last parameter before the auto result.
    /// A CUDA device function that reaches a dynamic <c>SharedMemory&lt;T&gt;</c> declaration is a template over the
    /// table type of its kernel and takes the table as the last parameter (<see cref="DynamicSharedMemoryEmitter"/>).
    /// </summary>
    public static void WriteMethodSignature(in MethodEmissionPlan plan, bool hasContext, ref TranspileContext context)
    {
        var method = plan.Declaration;
        var methodSymbol = plan.Symbol;
        var isDevice = plan.Kind != MethodEmissionKind.Kernel;
        if (isDevice && methodSymbol != null && GpuCodeConstraints.IsInstanceDeviceFunction(methodSymbol))
        {
            context.Reporter.Fail(
                GSharpDiagnostics.InstanceDeviceFunctionNotSupported,
                method.Identifier.GetLocation(),
                methodSymbol.ContainingType.Name,
                methodSymbol.Name);
        }

        FailUnsupportedParameterTypes(methodSymbol, ref context);

        string methodName;
        string? contextName = null;
        var takesTable = isDevice && methodSymbol != null &&
                         DynamicSharedMemoryEmitter.TakesTable(methodSymbol, context.Emitter.Dialect,
                             context.Query.SemanticModel.Compilation);
        if (isDevice)
        {
            if (takesTable)
            {
                DynamicSharedMemoryEmitter.AppendTemplateHeader(ref context);
            }

            WriteDeviceMethodPrefix(methodSymbol, ref context);
            methodName = methodSymbol != null && CppNameResolver.IsEmittedDeviceFunction(methodSymbol, context.Emitter.Dialect)
                ? GlobalCppNames.GetFunctionName(methodSymbol)
                : method.Identifier.Text;
        }
        else
        {
            // A kernel is named only from its symbol (GlobalCppNames.GetKernelName); without one there is no entry point
            if (methodSymbol is null)
            {
                return;
            }

            WriteKernelMethodPrefix(ref context);
            methodName = GlobalCppNames.GetKernelName(methodSymbol);
            if (hasContext)
            {
                contextName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Kernel.ContextPrefix, methodSymbol);
            }
        }

        context.Emitter.Writer.Append($" {methodName}");
        using (context.Emitter.Writer.Parentheses())
        {
            WriteParameters(method.ParameterList.Parameters, isDevice, ref context);

            if (takesTable)
            {
                DynamicSharedMemoryEmitter.AppendTableParameter(method.ParameterList.Parameters.Count > 0, ref context);
            }

            if (contextName != null)
            {
                if (method.ParameterList.Parameters.Count > 0)
                {
                    context.Emitter.Writer.Append(", ");
                }

                context.Emitter.Writer
                    .Append(contextName)
                    .Append("* ").Append(GSharpInternalNames.StateMachine.Context);
            }

            // A plan with an auto result always has a symbol
            if (isDevice || methodSymbol is null)
            {
                return;
            }

            var written = method.ParameterList.Parameters.Count > 0 || contextName != null;
            if (plan.HasAutoResult)
            {
                context.Emitter.Writer.AppendIf(written, ", ");
                context.Emitter.Dialect.AppendType(ref context, methodSymbol.ReturnType);
                context.Emitter.Writer.Append($"* __restrict__ {GSharpInternalNames.ReturnValue.Buffer}");
                written = true;
            }

            // The scratch of the grid strategy of AsParallel() aggregates, which the CUDA launcher allocates
            // (KernelHostModel.GridScratchSlotBytes, ParallelAggregatePlanner)
            if (context.Emitter.Dialect.IsCuda &&
                ParallelAggregatePlanner.GetGridScratchSlotBytes(method, context.Query.SemanticModel) > 0)
            {
                context.Emitter.Writer.AppendIf(written, ", ");
                context.Emitter.Writer.Append($"unsigned char* __restrict__ {GSharpInternalNames.StackLinq.GridScratch}");
            }
        }
    }

    /// <summary>
    /// Writes the signature of a device function that may suspend (CPU dialects):
    /// <c>template &lt;typename TRoot&gt; bool f(params, Frame* __gsharp_ctx, TRoot* __gsharp_root[, R* __gsharp_result])</c>.
    /// It returns whether the call completed; the root is the kernel context that owns the warp exchange.
    /// </summary>
    public static void WriteSuspendingDeviceSignature(MethodDeclarationSyntax method, IMethodSymbol methodSymbol,
        IReadOnlyCollection<string> copiedParameters, ref TranspileContext context)
    {
        FailUnsupportedParameterTypes(methodSymbol, ref context);

        var methodName = CppNameResolver.IsEmittedDeviceFunction(methodSymbol, context.Emitter.Dialect)
            ? GlobalCppNames.GetFunctionName(methodSymbol)
            : method.Identifier.Text;
        context.Emitter.Writer.Append($"template <typename TRoot> {GetInliningKeyword(methodSymbol)} bool {methodName}");
        using (context.Emitter.Writer.Parentheses())
        {
            WriteParameters(method.ParameterList.Parameters, true, ref context, copiedParameters);
            if (method.ParameterList.Parameters.Count > 0)
            {
                context.Emitter.Writer.Append(", ");
            }

            context.Emitter.Writer
                .Append(GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.SuspendingCall.FrameTypePrefix, methodSymbol))
                .Append($"* {GSharpInternalNames.StateMachine.Context}, TRoot* {GSharpInternalNames.SuspendingCall.Root}");

            if (!methodSymbol.ReturnsVoid)
            {
                context.Emitter.Writer.Append(", ");
                context.Emitter.Dialect.AppendType(ref context, methodSymbol.ReturnType);
                context.Emitter.Writer.Append($"* {GSharpInternalNames.SuspendingCall.Result}");
            }
        }
    }

    // [WHY] Checked here rather than in WriteParameters, which also runs for the CPU host launcher
    // and would record the same parameter twice.
    private static void FailUnsupportedParameterTypes(IMethodSymbol? methodSymbol, ref TranspileContext context)
    {
        if (methodSymbol is null)
        {
            return;
        }

        foreach (var parameter in methodSymbol.Parameters)
        {
            if (!GpuCodeConstraints.IsFunctionPointer(parameter.Type) ||
                !parameter.TryGetDeclaringSyntax<ParameterSyntax>(out var paramSyntax) ||
                paramSyntax.Type is not { } typeSyntax)
            {
                continue;
            }

            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, typeSyntax, typeSyntax.Kind().ToString());
        }
    }

    internal static (bool IsRefPointer, bool IsPointerType, bool HasRestrict) WriteParameterDeclaration(
        ParameterSyntax p, ITypeSymbol? typeSymbol, IParameterSymbol? parameterSymbol, ref TranspileContext context)
    {
        typeSymbol = typeSymbol.UnwrapNullableArrayLike();
        var isReadOnly = parameterSymbol != null && parameterSymbol.HasAttribute(KnownAttributes.GpuReadOnly);
        var isKernel = parameterSymbol?.ContainingSymbol is IMethodSymbol method &&
                       method.HasAttribute(KnownAttributes.GpuKernel);
        var isReadOnlyRef = !isKernel &&
                            (parameterSymbol is { RefKind: RefKind.In or RefKind.RefReadOnlyParameter } ||
                             p.Modifiers.Any(SyntaxKind.InKeyword) ||
                             p.Modifiers.Any(SyntaxKind.ReadOnlyKeyword));
        var isArray = typeSymbol.IsArrayLikeType();

        if ((isReadOnly && !isArray) || isReadOnlyRef)
        {
            context.Emitter.Writer.Append("const ");
        }

        return context.Emitter.Dialect.AppendParameter(ref context, p, typeSymbol, parameterSymbol);
    }

    /// <param name="parameters">The parameters to write.</param>
    /// <param name="isDevice">Whether the parameters belong to a device function.</param>
    /// <param name="context">The transpile context.</param>
    /// <param name="copiedParameters">
    /// Parameters that the body reads from its frame; their incoming value gets the <c>__gsharp_in_</c> prefix.
    /// </param>
    public static void WriteParameters(SeparatedSyntaxList<ParameterSyntax> parameters, bool isDevice,
        ref TranspileContext context, IReadOnlyCollection<string>? copiedParameters = null)
    {
        if (parameters.Count == 0)
        {
            return;
        }

        for (var i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Type == null)
            {
                continue;
            }

            WriteParameter(parameters[i], isDevice, ref context, copiedParameters);
            if (i < parameters.Count - 1)
            {
                context.Emitter.Writer.Append(", ");
            }
        }
    }

    /// <summary>Writes one parameter of <see cref="WriteParameters"/>: <c>&lt;type&gt; &lt;name&gt;</c>.</summary>
    internal static void WriteParameter(ParameterSyntax p, bool isDevice, ref TranspileContext context,
        IReadOnlyCollection<string>? copiedParameters = null)
    {
        if (p.Type == null)
        {
            return;
        }

        var paramSemanticModel = context.Query.SemanticModel.Compilation.GetSemanticModel(p.SyntaxTree);
        var typeSymbol = paramSemanticModel.GetTypeSymbol(p.Type).UnwrapNullableArrayLike();

        StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);

        var paramSymbol = paramSemanticModel.GetDeclaredSymbol<IParameterSymbol>(p);

        var (_, isPointerType, hasRestrict) =
            WriteParameterDeclaration(p, typeSymbol, paramSymbol, ref context);

        if (!isDevice && isPointerType && !hasRestrict)
        {
            context.Emitter.Writer.Append(" __restrict__");
        }

        context.Emitter.Writer.Append(' ');
        if (copiedParameters != null && Contains(copiedParameters, p.Identifier.ValueText))
        {
            context.Emitter.Writer.Append(GSharpInternalNames.SuspendingCall.CopiedParameterPrefix);
        }

        context.Emitter.Writer.AppendCppName(
            CppNameResolver.Resolve(paramSymbol, p.Identifier.ValueText, context.Emitter.Dialect));
    }

    private static bool Contains(IReadOnlyCollection<string> names, string name)
    {
        foreach (var candidate in names)
        {
            if (candidate == name)
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteDeviceMethodPrefix(IMethodSymbol? methodSymbol, ref TranspileContext context)
    {
        var inlineKeyword = GetInliningKeyword(methodSymbol);
        context.Emitter.Writer.Append(context.Emitter.Dialect.IsCuda ? $"{inlineKeyword} __device__ " : $"{inlineKeyword} ");

        var returnType = methodSymbol?.ReturnType;
        if (returnType is { SpecialType: not SpecialType.System_Void })
        {
            // C# ref returns map onto C++ lvalue references; 'ref readonly' adds const.
            if (methodSymbol is { RefKind: RefKind.RefReadOnly })
            {
                context.Emitter.Writer.Append("const ");
            }

            context.Emitter.Dialect.AppendType(ref context, returnType);

            if (methodSymbol is { RefKind: RefKind.Ref or RefKind.RefReadOnly })
            {
                context.Emitter.Writer.Append("&");
            }
        }
        else
        {
            context.Emitter.Writer.Append("void");
        }
    }

    private static string GetInliningKeyword(IMethodSymbol? methodSymbol)
    {
        if (methodSymbol == null)
        {
            return "inline";
        }

        foreach (var attr in methodSymbol.GetAttributes())
        {
            if (attr.IsAttribute(KnownAttributes.GpuInline))
            {
                return "__forceinline__";
            }

            if (attr.IsAttribute(KnownAttributes.GpuNoInline))
            {
                return "__noinline__";
            }

            if (attr.IsAttribute(KnownAttributes.MethodImpl))
            {
                var opt = ExtractMethodImplOptions(attr);
                if ((opt & MethodImplOptions.AggressiveInlining) != 0)
                {
                    return "__forceinline__";
                }

                if ((opt & MethodImplOptions.NoInlining) != 0)
                {
                    return "__noinline__";
                }
            }
        }

        return "inline";
    }

    private static MethodImplOptions ExtractMethodImplOptions(AttributeData attr)
    {
        if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int or short)
        {
            return (MethodImplOptions)Convert.ToInt32(attr.ConstructorArguments[0].Value);
        }

        var hasValue = attr.NamedArguments
            .AsStackEnumerable()
            .TryFirst(static namedArg => namedArg is { Key: "Value", Value.Value: int or short }, out var valueArg);

        return hasValue ? (MethodImplOptions)Convert.ToInt32(valueArg.Value.Value) : default;
    }

    private static void WriteKernelMethodPrefix(ref TranspileContext context)
    {
        // Auto-allocate feature: GpuKernel methods are now allowed to return values.
        context.Emitter.Writer.Append(context.Emitter.Dialect.IsCuda
            ? "extern \"C\" __global__ void"
            : "extern \"C\" __declspec(dllexport) void");
    }
}

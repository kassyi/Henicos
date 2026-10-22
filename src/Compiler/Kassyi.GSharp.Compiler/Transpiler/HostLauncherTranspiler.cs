
namespace Kassyi.GSharp.Compiler.Transpiler;

using static GSharpInternalNames.Kernel;
using static GSharpInternalNames.Launcher;
using static GSharpInternalNames.ReturnValue;
using static GSharpInternalNames.StateMachine;
using static GSharpInternalNames.Prelude;

/// <summary>
/// Emits CPU / OpenMP multi-threaded execution launchers and shared memory emulation wrappers for host execution.
/// </summary>
internal static class HostLauncherTranspiler
{
    private const string SharedMemoryBufferPrefix = "__gsharp_smem_buffer_";

    public static void WriteCpuLauncher(MethodDeclarationSyntax method,
        IReadOnlyList<VariableDeclaratorSyntax>? contextFields, ref TranspileContext context)
    {
        var hasContext = contextFields != null;
        var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(method);
        if (methodSymbol == null)
        {
            return;
        }

        context.Emitter.Writer.AppendLine();
        context.Emitter.Writer.AppendKeyword("extern \"C\" __declspec(dllexport) void");
        var methodName = MethodSignatureTranspiler.GetKernelMethodName(method, methodSymbol);
        var contextName = MethodSignatureTranspiler.GetKernelDerivedName(ContextPrefix, method, methodSymbol);
        context.Emitter.Writer.AppendSpace()
            .Append(MethodSignatureTranspiler.GetKernelDerivedName(LaunchExportPrefix, method, methodSymbol))
            .Append('(');

        // Launch dimensions
        context.Emitter.Writer.Append(
            $"uint32_t {GridDimX}, uint32_t {GridDimY}, uint32_t {GridDimZ}, ");
        context.Emitter.Writer.Append(
            $"uint32_t {BlockDimX}, uint32_t {BlockDimY}, uint32_t {BlockDimZ}");

        if (method.ParameterList.Parameters.Count > 0)
        {
            context.Emitter.Writer.Append(", ");
        }

        MethodSignatureTranspiler.WriteParameters(method.ParameterList.Parameters, false, ref context);

        var isKernelWithReturn = methodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
                                 methodSymbol.ReturnType.SpecialType != SpecialType.System_Void;
        if (isKernelWithReturn)
        {
            context.Emitter.Writer.Append(", ");
            context.Emitter.Dialect.AppendType(ref context, methodSymbol.ReturnType);
            context.Emitter.Writer.Append($"* __restrict__ {Buffer}");
        }

        context.Emitter.Writer.AppendLine(")");

        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine($"{HostLastError}.Reset();");
            context.Emitter.Writer.AppendLine($"GSharpErrorSlot {Error};");
            context.Emitter.Writer.AppendLine(
                $"int32_t {TotalBlocks} = (int32_t)({GridDimX} * {GridDimY} * {GridDimZ});");
            context.Emitter.Writer.AppendLine("#pragma omp parallel for schedule(dynamic)");
            context.Emitter.Writer.AppendLine(
                $"for (int32_t {BlockLoopIndex} = 0; {BlockLoopIndex} < {TotalBlocks}; {BlockLoopIndex}++)");
            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Writer.AppendLine($"if ({Error}.HasError()) continue;");
                context.Emitter.Writer.AppendLine($"int32_t {BlockX} = {BlockLoopIndex} % {GridDimX};");
                context.Emitter.Writer.AppendLine(
                    $"int32_t {BlockY} = ({BlockLoopIndex} / {GridDimX}) % {GridDimY};");
                context.Emitter.Writer.AppendLine(
                    $"int32_t {BlockZ} = {BlockLoopIndex} / ({GridDimX} * {GridDimY});");
                context.Emitter.Writer.AppendLine("__gsharp_reset_smem();");
                if (hasContext)
                {
                    context.Emitter.Writer.AppendLine(
                        $"std::vector<{contextName}> {Contexts}({BlockDimX} * {BlockDimY} * {BlockDimZ});");

                    if (contextFields != null)
                    {
                        WriteSharedMemoryEmulationBuffers(contextFields, ref context);
                    }

                    context.Emitter.Writer.AppendLine($"for (auto& {ContextItem} : {Contexts}) {{");
                    context.Emitter.Writer.AppendLine($"{ContextItem}.{State} = 0;");
                    context.Emitter.Writer.AppendLine($"{ContextItem}.{AllContexts} = {Contexts}.data();");

                    if (contextFields != null)
                    {
                        WriteSharedMemoryEmulationAssignments(contextFields, ref context);
                    }

                    context.Emitter.Writer.AppendLine("}");
                    context.Emitter.Writer.AppendLine($"std::atomic<bool> {AnyActive}{{true}};");
                    context.Emitter.Writer.AppendLine($"while ({AnyActive} && !{Error}.HasError())");
                    using (context.Emitter.Writer.Scope())
                    {
                        context.Emitter.Writer.AppendLine($"{AnyActive} = false;");
                        WriteBlockLoop(method, methodSymbol, methodName, hasContext, ref context);
                    }
                }
                else
                {
                    WriteBlockLoop(method, methodSymbol, methodName, hasContext, ref context);
                }
            }

            context.Emitter.Writer.AppendLine($"if ({Error}.HasError()) {{");
            context.Emitter.Writer.AppendLine(
                $"{HostLastError}.Report(static_cast<GSharpErrorCode>({Error}.code.load(std::memory_order_acquire)), {Error}.message);");
            context.Emitter.Writer.AppendLine("}");
        }
    }

    private static void WriteSharedMemoryEmulationBuffers(IReadOnlyList<VariableDeclaratorSyntax> contextFields,
        ref TranspileContext context)
    {
        foreach (var field in contextFields)
        {
            var typeSyntax = (field.Parent as VariableDeclarationSyntax)?.Type;
            if (typeSyntax is not GenericNameSyntax { Identifier.ValueText: TypeConstants.SharedMemoryType })
            {
                continue;
            }

            var typeSymbolField = context.Query.SemanticModel.GetTypeSymbol(typeSyntax) as INamedTypeSymbol;
            var typeArg = typeSymbolField?.TypeArguments[0];

            var initializer = field.Initializer?.Value;
            var sizeExpression =
                initializer != null &&
                InitializationSyntaxExtractor.TryGetObjectCreationFirstArgument(initializer, out var sizeArg)
                    ? sizeArg
                    : null;

            if (typeArg == null || sizeExpression == null)
            {
                continue;
            }

            context.Emitter.Writer.Append("std::vector<");
            context.Emitter.Dialect.AppendType(ref context, typeArg);
            context.Emitter.Writer.Append("> ");
            AppendSharedMemoryBufferName(field, ref context);
            using (context.Emitter.Writer.Parentheses())
            {
                context.Visitor.Visit(sizeExpression);
            }

            context.Emitter.Writer.AppendSemiColon().AppendLine();
        }
    }

    private static void WriteSharedMemoryEmulationAssignments(IReadOnlyList<VariableDeclaratorSyntax> contextFields,
        ref TranspileContext context)
    {
        foreach (var field in contextFields)
        {
            var typeSyntax = (field.Parent as VariableDeclarationSyntax)?.Type;
            if (typeSyntax is not GenericNameSyntax { Identifier.ValueText: TypeConstants.SharedMemoryType })
            {
                continue;
            }

            context.Emitter.Writer.Append($"{ContextItem}.");
            CppNameResolver.AppendDeclaredName(field, field.Identifier, ref context);
            context.Emitter.Writer.Append(".data = ");
            AppendSharedMemoryBufferName(field, ref context);
            context.Emitter.Writer.AppendLine(".data();");
        }
    }

    private static void AppendSharedMemoryBufferName(VariableDeclaratorSyntax field, ref TranspileContext context)
    {
        context.Emitter.Writer.Append(SharedMemoryBufferPrefix);
        CppNameResolver.AppendDeclaredName(field, field.Identifier, ref context);
    }

    private static void WriteBlockLoop(MethodDeclarationSyntax method, IMethodSymbol methodSymbol, string methodName,
        bool hasContext, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine(
            $"int32_t {ThreadsPerBlock} = (int32_t)({BlockDimX} * {BlockDimY} * {BlockDimZ});");
        context.Emitter.Writer.AppendLine("#pragma omp parallel for schedule(static)");
        context.Emitter.Writer.AppendLine(
            $"for (int32_t {ThreadLoopIndex} = 0; {ThreadLoopIndex} < {ThreadsPerBlock}; {ThreadLoopIndex}++)");
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine($"if ({Error}.HasError()) continue;");
            context.Emitter.Writer.AppendLine($"{ActiveErrorSlot} = &{Error};");
            context.Emitter.Writer.AppendLine($"int32_t {ThreadX} = {ThreadLoopIndex} % {BlockDimX};");
            context.Emitter.Writer.AppendLine(
                $"int32_t {ThreadY} = ({ThreadLoopIndex} / {BlockDimX}) % {BlockDimY};");
            context.Emitter.Writer.AppendLine(
                $"int32_t {ThreadZ} = {ThreadLoopIndex} / ({BlockDimX} * {BlockDimY});");
            if (hasContext)
            {
                context.Emitter.Writer.AppendLine(
                    $"uint32_t {ContextThreadIndex} = {ThreadZ} * {BlockDimY} * {BlockDimX} + {ThreadY} * {BlockDimX} + {ThreadX};");
                context.Emitter.Writer.AppendLine(
                    $"if ({Contexts}[{ContextThreadIndex}].{State} != -1)");
            }

            using (hasContext ? context.Emitter.Writer.Scope() : default)
            {
                if (hasContext)
                {
                    context.Emitter.Writer.AppendLine($"{AnyActive} = true;");
                }

                context.Emitter.Writer.AppendLine(
                    $"{SetThreadIndex}({ThreadX}, {ThreadY}, {ThreadZ}, {BlockX}, {BlockY}, {BlockZ}, {BlockDimX}, {BlockDimY}, {BlockDimZ}, {GridDimX}, {GridDimY}, {GridDimZ});");
                context.Emitter.Writer.Append(methodName);
                using (context.Emitter.Writer.Parentheses())
                {
                    var isFirst = true;

                    foreach (var parameter in method.ParameterList.Parameters)
                    {
                        AppendArgumentSeparator(ref isFirst, ref context);
                        CppNameResolver.AppendDeclaredName(parameter, parameter.Identifier, ref context);
                    }

                    if (hasContext)
                    {
                        AppendArgumentSeparator(ref isFirst, ref context);
                        context.Emitter.Writer.Append($"&{Contexts}[{ContextThreadIndex}]");
                    }

                    var isKernelWithReturn = methodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
                                             methodSymbol.ReturnType.SpecialType != SpecialType.System_Void;

                    if (isKernelWithReturn)
                    {
                        AppendArgumentSeparator(ref isFirst, ref context);
                        context.Emitter.Writer.Append(Buffer);
                    }
                }

                context.Emitter.Writer.AppendSemiColonLine();
            }
        }
    }

    private static void AppendArgumentSeparator(ref bool isFirst, ref TranspileContext context)
    {
        if (!isFirst)
        {
            context.Emitter.Writer.AppendComma().AppendSpace();
        }

        isFirst = false;
    }
}

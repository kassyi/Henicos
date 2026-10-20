
namespace Kassyi.GSharp.Compiler.Transpiler.Rules.DeclarationRules;

/// <summary>
/// Transpiles C# local declaration statements into target dialect variable definitions,
/// with special handling for static and dynamic GPU shared memory declarations (<c>SharedMemory&lt;T&gt;</c>).
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and safe for concurrent execution across threads.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class LocalDeclarationStatementTranspilerRule : SyntaxNodeTranspilerRuleBase<LocalDeclarationStatementSyntax>
{
    private const int MaxSharedMemoryWarningBytes = 32768;

    private readonly struct SharedMemoryAllocation(
        VariableDeclaratorSyntax declarator,
        ExpressionSyntax sizeArgument,
        int? constantSize)
    {
        public VariableDeclaratorSyntax Declarator { get; } = declarator;
        public ExpressionSyntax SizeArgument { get; } = sizeArgument;
        private int? ConstantSize { get; } = constantSize;
        public bool IsDynamic => !ConstantSize.HasValue;
        public int StaticSize => ConstantSize ?? 0;
    }

    /// <inheritdoc />
    public override void Transpile(LocalDeclarationStatementSyntax node, ref TranspileContext context)
    {
        var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(node.Declaration.Type);
        if (typeSymbol.IsSharedMemoryType())
        {
            ProcessSharedMemoryDeclaration(node, (INamedTypeSymbol)typeSymbol, ref context);
            return;
        }

        DeclarationHoistingHelper.HoistDeclarationExpressions(node, ref context);

        // Dispatch inner variable declaration transpilation to the visitor (which will use VariableDeclarationTranspilerRule)
        context.Visitor.Visit(node.Declaration);
        context.Emitter.Writer.AppendSemiColon();
    }

    private static void ProcessSharedMemoryDeclaration(LocalDeclarationStatementSyntax node,
        INamedTypeSymbol typeSymbol, ref TranspileContext context)
    {
        var typeArg = typeSymbol.TypeArguments[0];

        for (var i = 0; i < node.Declaration.Variables.Count; i++)
        {
            var alloc = AnalyzeSharedMemoryAllocation(node.Declaration.Variables[i], context.Query.SemanticModel);
            if (alloc == null)
            {
                continue;
            }

            if (alloc.Value.IsDynamic)
            {
                WriteDynamicSharedMemory(alloc.Value, typeArg, node, i, ref context);
            }
            else
            {
                WriteStaticSharedMemory(alloc.Value, typeArg, node, i, ref context);
            }
        }
    }

    private static SharedMemoryAllocation? AnalyzeSharedMemoryAllocation(VariableDeclaratorSyntax declarator,
        SemanticModel semanticModel)
    {
        if (declarator.Initializer?.Value == null ||
            !InitializationSyntaxExtractor.TryGetObjectCreationFirstArgument(declarator.Initializer.Value,
                out var sizeArg) ||
            sizeArg == null)
        {
            return null;
        }

        var constantValue = semanticModel.GetSafeConstantValue(sizeArg);
        return new SharedMemoryAllocation(
            declarator,
            sizeArg,
            constantValue.HasValue ? Convert.ToInt32(constantValue.Value ?? 0) : null
        );
    }

    private static void WriteStaticSharedMemory(SharedMemoryAllocation alloc,
        ITypeSymbol typeArg, LocalDeclarationStatementSyntax node, int i, ref TranspileContext context)
    {
        CheckExcessiveSharedMemoryUsage(alloc.SizeArgument, alloc.StaticSize, typeArg, ref context);

        context.Emitter.Writer.Append("__shared__ ");
        context.Emitter.Dialect.AppendType(ref context, typeArg);
        context.Emitter.Writer.AppendSpace();
        CppNameResolver.AppendDeclaredName(alloc.Declarator, alloc.Declarator.Identifier, ref context);
        using (context.Emitter.Writer.Brackets())
        {
            context.Visitor.Visit(alloc.SizeArgument);
        }

        context.Emitter.Writer.Append(";");
        if (i < node.Declaration.Variables.Count - 1)
        {
            context.Emitter.Writer.AppendLine();
        }
    }

    private static void CheckExcessiveSharedMemoryUsage(ExpressionSyntax sizeArg, int elementCount,
        ITypeSymbol typeArg, ref TranspileContext context)
    {
        var elemSize = context.Emitter.Dialect.GetApproximateSize(typeArg);
        var byteSize = elementCount * elemSize;
        if (byteSize > MaxSharedMemoryWarningBytes)
        {
            context.Reporter.Report(GSharpDiagnostics.ExcessiveSharedMemoryWarning, sizeArg, byteSize,
                MaxSharedMemoryWarningBytes);
        }
    }

    private static void WriteDynamicSharedMemory(SharedMemoryAllocation alloc, ITypeSymbol typeArg,
        LocalDeclarationStatementSyntax node, int index, ref TranspileContext context)
    {
        EnsureDynamicSharedMemoryBase(ref context);
        AlignDynamicSharedMemoryOffset(typeArg, ref context);
        EmitDynamicSharedMemoryPointerAndAdvance(alloc, typeArg, ref context);

        if (index < node.Declaration.Variables.Count - 1)
        {
            context.Emitter.Writer.AppendLine();
        }
    }

    private static void EnsureDynamicSharedMemoryBase(ref TranspileContext context)
    {
        if (context.Emitter.HasEmittedDynamicSmemBase)
        {
            return;
        }

        context.Emitter.Writer.AppendLine("extern __shared__ char __gsharp_dynamic_smem[];");
        context.Emitter.Writer.AppendLine("size_t __gsharp_smem_offset = 0;");
        context.Emitter.HasEmittedDynamicSmemBase = true;
    }

    private static void AlignDynamicSharedMemoryOffset(ITypeSymbol typeArg, ref TranspileContext context)
    {
        // [WHY] Computes aligned offset: (offset + align - 1) & ~(align - 1) to avoid CUDA unaligned memory access faults.
        context.Emitter.Writer.Append("__gsharp_smem_offset = ");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Writer.Append("__gsharp_smem_offset + ");
            AppendAlignmentMask(typeArg, ref context);
        }

        context.Emitter.Writer.Append(" & ~");
        using (context.Emitter.Writer.Parentheses())
        {
            AppendAlignmentMask(typeArg, ref context);
        }

        context.Emitter.Writer.AppendSemiColonLine();
    }

    private static void AppendAlignmentMask(ITypeSymbol typeArg, ref TranspileContext context)
    {
        context.Emitter.Writer.Append("alignof");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Dialect.AppendType(ref context, typeArg);
        }

        context.Emitter.Writer.Append(" - 1");
    }

    private static void EmitDynamicSharedMemoryPointerAndAdvance(SharedMemoryAllocation alloc,
        ITypeSymbol typeArg, ref TranspileContext context)
    {
        context.Emitter.Dialect.AppendType(ref context, typeArg);
        context.Emitter.Writer.Append("* ");
        CppNameResolver.AppendDeclaredName(alloc.Declarator, alloc.Declarator.Identifier, ref context);
        context.Emitter.Writer.Append(" = ");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Dialect.AppendType(ref context, typeArg);
            context.Emitter.Writer.Append("*");
        }

        context.Emitter.Writer.Append("(__gsharp_dynamic_smem + __gsharp_smem_offset)");

        context.Emitter.Writer.AppendLine(";");
        context.Emitter.Writer.Append("__gsharp_smem_offset += ");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(alloc.SizeArgument);
        }

        context.Emitter.Writer.Append(" * sizeof");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Dialect.AppendType(ref context, typeArg);
        }

        context.Emitter.Writer.AppendSemiColon();
    }
}

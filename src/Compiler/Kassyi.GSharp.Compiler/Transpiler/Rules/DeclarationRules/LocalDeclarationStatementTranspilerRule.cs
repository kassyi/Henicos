using Kassyi.GSharp.Compiler.Transpiler.Helpers;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.DeclarationRules;

/// <summary>
/// Transpiles C# local declaration statements into target dialect variable definitions,
/// with special handling for static and dynamic GPU shared memory declarations (<c>SharedMemory&lt;T&gt;</c>).
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and safe for concurrent execution across threads.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public class LocalDeclarationStatementTranspilerRule : SyntaxNodeTranspilerRuleBase<LocalDeclarationStatementSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="LocalDeclarationStatementTranspilerRule"/>.
    /// </summary>
    public LocalDeclarationStatementTranspilerRule()
    {
    }

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
        var typeSymbol = context.SemanticModel.GetTypeSymbol(node.Declaration.Type);
        if (typeSymbol?.Name == TypeConstants.SharedMemoryType &&
            typeSymbol.ContainingNamespace.IsCoreNamespace())
        {
            ProcessSharedMemoryDeclaration(node, (INamedTypeSymbol)typeSymbol, ref context);
            return;
        }

        DeclarationHoistingHelper.HoistDeclarationExpressions(node, ref context);

        // Dispatch inner variable declaration transpilation to the visitor (which will use VariableDeclarationTranspilerRule)
        context.Visitor.VisitVariableDeclaration(node.Declaration);
        context.Writer.AppendSemiColon();
    }

    private static void ProcessSharedMemoryDeclaration(LocalDeclarationStatementSyntax node,
        INamedTypeSymbol typeSymbol, ref TranspileContext context)
    {
        var typeArg = typeSymbol.TypeArguments[0];

        for (var i = 0; i < node.Declaration.Variables.Count; i++)
        {
            var alloc = AnalyzeSharedMemoryAllocation(node.Declaration.Variables[i], context.SemanticModel);
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
            !InitializationSyntaxExtractor.TryGetObjectCreationFirstArgument(declarator.Initializer.Value, out var sizeArg) ||
            sizeArg == null)
        {
            return null;
        }

        var constantValue = semanticModel.GetConstantValue(sizeArg);
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

        context.Writer.Append("__shared__ ");
        context.AppendType(typeArg);
        context.Writer.Append($" {alloc.Declarator.Identifier.Text}");
        using (context.Writer.Brackets())
        {
            context.Visitor.Visit(alloc.SizeArgument);
        }

        context.Writer.Append(";");
        if (i < node.Declaration.Variables.Count - 1)
        {
            context.Writer.AppendLine();
        }
    }

    private static void CheckExcessiveSharedMemoryUsage(ExpressionSyntax sizeArg, int elementCount,
        ITypeSymbol typeArg, ref TranspileContext context)
    {
        var elemSize = context.Dialect.GetApproximateSize(typeArg);
        var byteSize = elementCount * elemSize;
        if (byteSize > 32768)
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.ExcessiveSharedMemoryWarning,
                sizeArg.GetLocation(), byteSize, 32768));
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
            context.Writer.AppendLine();
        }
    }

    private static void EnsureDynamicSharedMemoryBase(ref TranspileContext context)
    {
        if (context.HasEmittedDynamicSmemBase)
        {
            return;
        }

        context.Writer.AppendLine("extern __shared__ char __gsharp_dynamic_smem[];");
        context.Writer.AppendLine("size_t __smem_offset = 0;");
        context.HasEmittedDynamicSmemBase = true;
    }

    private static void AlignDynamicSharedMemoryOffset(ITypeSymbol typeArg, ref TranspileContext context)
    {
        // [WHY] Computes aligned offset: (offset + align - 1) & ~(align - 1) to avoid CUDA unaligned memory access faults.
        context.Writer.Append("__smem_offset = ");
        using (context.Writer.Parentheses())
        {
            context.Writer.Append("__smem_offset + alignof");
            using (context.Writer.Parentheses())
            {
                context.AppendType(typeArg);
            }
            context.Writer.Append(" - 1");
        }

        context.Writer.Append(" & ~");
        using (context.Writer.Parentheses())
        {
            context.Writer.Append("alignof");
            using (context.Writer.Parentheses())
            {
                context.AppendType(typeArg);
            }
            context.Writer.Append(" - 1");
        }

        context.Writer.AppendSemiColonLine();
    }

    private static void EmitDynamicSharedMemoryPointerAndAdvance(SharedMemoryAllocation alloc,
        ITypeSymbol typeArg, ref TranspileContext context)
    {
        context.AppendType(typeArg);
        context.Writer.Append($"* {alloc.Declarator.Identifier.Text} = ");
        using (context.Writer.Parentheses())
        {
            context.AppendType(typeArg);
            context.Writer.Append("*");
        }

        context.Writer.Append("(__gsharp_dynamic_smem + __smem_offset)");

        context.Writer.AppendLine(";");
        context.Writer.Append("__smem_offset += ");
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(alloc.SizeArgument);
        }

        context.Writer.Append(" * sizeof");
        using (context.Writer.Parentheses())
        {
            context.AppendType(typeArg);
        }
        context.Writer.AppendSemiColon();
    }
}

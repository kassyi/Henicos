namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// A <c>new SharedMemory&lt;T&gt;(n)</c> that is not the initializer of a local (<c>return new ...</c>, an expression
/// body, an argument, an operand of <c>?:</c>) cannot be written in place: CUDA declares the region with a statement
/// (<c>__shared__ T name[n];</c>), and a braced allocation is no operand of <c>?:</c> or of a call. The statement
/// prelude declares it before the statement, as <c>__gsharp_smem_&lt;n&gt;</c>, and the creation is written as that
/// name.
/// </summary>
/// <remarks>
/// Each creation is one declaration, so it is one region, whichever branch of a condition is taken. Its count is
/// evaluated at the declaration, before the statement.
/// </remarks>
internal static class SharedMemoryCreationLift
{
    // Only this helper spells the names
    private const string NamePrefix = "__gsharp_smem_";

    /// <summary>The GSH0003 text for a creation that no statement can declare.</summary>
    public const string NoStatement = "shared memory creation outside a statement that can declare its memory";

    /// <summary>
    /// Declares the memory of <paramref name="node"/> when it is a creation that is written as a name.
    /// </summary>
    public static void DeclareStorage(SyntaxNode node, ref TranspileContext context)
    {
        if (node is not BaseObjectCreationExpressionSyntax creation ||
            context.Query.SemanticModel.GetTypeSymbol(creation) is not INamedTypeSymbol { TypeArguments.Length: 1 } 
                type ||
            !type.IsSharedMemoryType() ||
            !IsLifted(creation, context.Emitter.Dialect.IsCuda))
        {
            return;
        }

        var number = context.Emitter.SharedMemoryLifts.Count;
        context.Emitter.SharedMemoryLifts[creation] = number;
        var statement = creation.FirstAncestorOrSelf<StatementSyntax>();
        SharedMemoryDeclarationWriter.Write(SharedMemoryName.Lifted(NamePrefix + number), creation,
            type.TypeArguments[0], statement != null && GotoJumpAnalysis.MayBeJumpedOver(statement), ref context);
        context.Emitter.Writer.AppendLine();
    }

    /// <summary>
    /// Writes the name of the memory declared for <paramref name="creation"/>; false when the prelude declared none
    /// (the CPU dialects write the creation of an assignment in place).
    /// </summary>
    public static bool TryAppend(BaseObjectCreationExpressionSyntax creation, ref TranspileContext context)
    {
        if (!context.Emitter.SharedMemoryLifts.TryGetValue(creation, out var number))
        {
            return false;
        }

        context.Emitter.Writer.Append(NamePrefix + number);
        return true;
    }

    // A local declaration writes its own creation. The CPU dialects write the value of an assignment in place, as a
    // braced allocation (the assignment of a state machine's context gets one)
    private static bool IsLifted(BaseObjectCreationExpressionSyntax creation, bool isCuda)
    {
        SyntaxNode child = creation;
        var parent = creation.Parent;
        while (parent is ParenthesizedExpressionSyntax)
        {
            child = parent;
            parent = parent.Parent;
        }

        return parent switch
        {
            EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax
                {
                    Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax }
                }
            } => false,
            AssignmentExpressionSyntax assignment when !isCuda &&
                                                       assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                                                       assignment.Right == child => false,
            _ => true
        };
    }
}

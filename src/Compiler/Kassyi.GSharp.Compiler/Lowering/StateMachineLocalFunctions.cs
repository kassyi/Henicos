namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Decides which statements of a split body the state machine writes in front of its <c>switch</c>: the declarations of
/// local functions, with a label in front of them if it has one.
/// </summary>
/// <remarks>
/// [WHY] A local function that stays in the <c>case</c> block of its state is invisible to the calls of the other
/// states (CS0103 in the managed output, C2065 in the C++ output). Outside the <c>case</c> blocks it is visible to every
/// state, as in the source, where a local function can be called from anywhere in its block.
/// </remarks>
internal static class StateMachineLocalFunctions
{
    /// <summary>
    /// The local function that <paramref name="statement"/> declares, also behind labels, or <see langword="null"/> for
    /// any other statement.
    /// </summary>
    public static LocalFunctionStatementSyntax? GetMoved(StatementSyntax statement) =>
        statement switch
        {
            LocalFunctionStatementSyntax function => function,
            LabeledStatementSyntax labeled => GetMoved(labeled.Statement),
            _ => null
        };

    /// <summary>
    /// What remains of <paramref name="statement"/> once its local function is taken out: <see langword="null"/> for the
    /// declaration itself, the label with an empty statement for a labeled one, and the statement as it is otherwise.
    /// </summary>
    public static StatementSyntax? WithoutFunction(StatementSyntax statement) =>
        statement switch
        {
            LocalFunctionStatementSyntax => null,
            LabeledStatementSyntax labeled when GetMoved(labeled) != null =>
                labeled.WithStatement(WithoutFunction(labeled.Statement) ?? SyntaxFactory.EmptyStatement()),
            _ => statement
        };

    /// <summary>
    /// The keys of the locals that the local functions of <paramref name="block"/> that <see cref="GetMoved"/> finds
    /// read or write; they are not declared in any state, so the context holds them.
    /// </summary>
    public static HashSet<DeclarationKey> FindCaptured(BlockSyntax block, SemanticModel? semanticModel)
    {
        var captured = new HashSet<DeclarationKey>();
        foreach (var statement in block.Statements)
        {
            if (GetMoved(statement) is not { } function)
            {
                continue;
            }

            foreach (var node in function.DescendantNodes())
            {
                if (node is IdentifierNameSyntax identifier &&
                    !HoistedVariableAnalysis.IsNonValueIdentifierPosition(identifier))
                {
                    captured.Add(LoweringIdentity.ForReference(identifier, semanticModel));
                }
            }
        }

        return captured;
    }
}

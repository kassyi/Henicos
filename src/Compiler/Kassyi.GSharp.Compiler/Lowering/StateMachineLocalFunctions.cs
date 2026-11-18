namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Decides which statements of a split body the state machine writes in front of its <c>switch</c>: the declarations of
/// local functions, with a label in front of them if it has one, that capture only locals the context can hold.
/// </summary>
/// <remarks>
/// [WHY] A local function that stays in the <c>case</c> block of its state is invisible to the calls of the other
/// states (CS0103 in the managed output, C2065 in the C++ output). Outside the <c>case</c> blocks it is visible to every
/// state, as in the source, where a local function can be called from anywhere in its block. A function that captures a
/// local that no state declares must read it from the context, so it moves only when every local it captures can be
/// hoisted (<see cref="Plan"/>).
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
    /// The functions of <paramref name="block"/> that move in front of the <c>switch</c> and the locals they capture.
    /// </summary>
    /// <remarks>
    /// A function stays in its state when it captures a local that the context cannot hold (a <c>using</c> declaration,
    /// whose disposal the hoisting would drop, or a pattern, <c>out var</c>, deconstruction, <c>foreach</c> or catch
    /// variable, which is not a declarator) or calls a function that stays; the second rule is a fixpoint.
    /// </remarks>
    public static MovedLocalFunctions Plan(BlockSyntax block, SemanticModel? semanticModel)
    {
        var candidates = new List<(DeclarationKey Key, HashSet<DeclarationKey> References)>();
        foreach (var statement in block.Statements)
        {
            if (GetMoved(statement) is { } function)
            {
                candidates.Add((LoweringIdentity.ForDeclaration(function, semanticModel),
                    FindReferences(function, semanticModel)));
            }
        }

        if (candidates.Count == 0)
        {
            return MovedLocalFunctions.None;
        }

        var notHoistable = FindNotHoistable(block, semanticModel);
        var stays = new HashSet<DeclarationKey>();
        bool changed;
        do
        {
            changed = false;
            foreach (var (key, references) in candidates)
            {
                if (!stays.Contains(key) && (references.Overlaps(notHoistable) || references.Overlaps(stays)))
                {
                    stays.Add(key);
                    changed = true;
                }
            }
        }
        while (changed);

        var functions = new HashSet<DeclarationKey>();
        var captured = new HashSet<DeclarationKey>();
        foreach (var (key, references) in candidates)
        {
            if (!stays.Contains(key))
            {
                functions.Add(key);
                captured.UnionWith(references);
            }
        }

        return new MovedLocalFunctions(functions, captured);
    }

    // The keys of the names the function reads or writes in a value position
    private static HashSet<DeclarationKey> FindReferences(
        LocalFunctionStatementSyntax function,
        SemanticModel? semanticModel)
    {
        var references = new HashSet<DeclarationKey>();
        foreach (var node in function.DescendantNodes())
        {
            if (node is IdentifierNameSyntax identifier &&
                !HoistedVariableAnalysis.IsNonValueIdentifierPosition(identifier))
            {
                references.Add(LoweringIdentity.ForReference(identifier, semanticModel));
            }
        }

        return references;
    }

    // The locals of the body (not of a local function or lambda in it) that the context cannot hold
    private static HashSet<DeclarationKey> FindNotHoistable(BlockSyntax block, SemanticModel? semanticModel)
    {
        var keys = new HashSet<DeclarationKey>();
        foreach (var node in block.DescendantNodes())
        {
            if (!IsBodyNode(node))
            {
                continue;
            }

            switch (node)
            {
                case LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0 } declaration:
                    foreach (var variable in declaration.Declaration.Variables)
                    {
                        keys.Add(LoweringIdentity.ForDeclaration(variable, semanticModel));
                    }

                    break;
                case SingleVariableDesignationSyntax or ForEachStatementSyntax or CatchDeclarationSyntax:
                    keys.Add(LoweringIdentity.ForDeclaration(node, semanticModel));
                    break;
            }
        }

        return keys;
    }

    // Whether the node belongs to the body itself, not to a local function or a lambda in it
    private static bool IsBodyNode(SyntaxNode node) =>
        !node.Ancestors()
            .AsStackEnumerable()
            .Any(static ancestor => ancestor is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax);
}

/// <summary>The local functions that move in front of the state <c>switch</c> and the locals they capture.</summary>
/// <param name="Functions">The keys (<see cref="LoweringIdentity.ForDeclaration"/>) of the functions that move.</param>
/// <param name="Captured">
/// The keys of the names that those functions reference; the context holds the locals among them.
/// </param>
internal sealed record MovedLocalFunctions(HashSet<DeclarationKey> Functions, HashSet<DeclarationKey> Captured)
{
    /// <summary>No function moves.</summary>
    public static MovedLocalFunctions None { get; } = new([], []);
}

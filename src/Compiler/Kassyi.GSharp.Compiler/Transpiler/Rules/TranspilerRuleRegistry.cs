namespace Kassyi.GSharp.Compiler.Transpiler.Rules;
using DeclarationRules;
using ExpressionRules;
using InvocationRules;
using MacroRules;

/// <summary>
/// The single composition root of the transpiler rules: the <see cref="SyntaxKind"/> table (O(1) array lookup),
/// the invocation rule chain and the statement-level macro rule chain.
/// </summary>
internal sealed class TranspilerRuleRegistry
{
    private readonly ISyntaxNodeTranspilerRule?[] _rules = new ISyntaxNodeTranspilerRule?[10000];

    /// <summary>
    /// Gets the registry populated with all default transpiler rules.
    /// </summary>
    public static TranspilerRuleRegistry Default { get; } = CreateDefault();

    /// <summary>
    /// Specialized invocation transpiler rules, evaluated in order before falling back to standard call emission.
    /// </summary>
    /// <remarks>BCL intrinsics come first so that an intrinsic mapping always wins over the other rules.</remarks>
    public ImmutableArray<IInvocationTranspilerRule> InvocationRules { get; } =
        ImmutableArray.Create<IInvocationTranspilerRule>(
            new BclIntrinsicTranspilerRule(),
            new GpuMacroTranspilerRule(),
            new MemoryExtensionsTranspilerRule(),
            new StackLinqFusionRule(),
            new StackLinqTranspilerRule()
        );

    /// <summary>
    /// Statement-level GPU macro rules, evaluated in order for invocation expression statements.
    /// </summary>
    public ImmutableArray<IMacroTranspilerRule> MacroRules { get; } =
        ImmutableArray.Create<IMacroTranspilerRule>(
            new HintUnrollMacroRule(),
            new ForUnrollMacroRule(),
            new GridStrideLoopMacroRule(),
            new SingleThreadMacroRule(),
            new SyncGridMacroRule()
        );

    /// <summary>
    /// Registers a transpilation rule for a specific syntax kind.
    /// </summary>
    public void Register(SyntaxKind kind, ISyntaxNodeTranspilerRule rule) => _rules[(int)kind] = rule;

    /// <summary>
    /// Registers a transpilation rule for multiple syntax kinds.
    /// </summary>
    public void Register(IEnumerable<SyntaxKind> kinds, ISyntaxNodeTranspilerRule rule)
    {
        foreach (var kind in kinds)
        {
            _rules[(int)kind] = rule;
        }
    }

    /// <summary>
    /// Gets the registered transpiler rule for the specified syntax kind, if any.
    /// </summary>
    public ISyntaxNodeTranspilerRule? GetRule(SyntaxKind kind)
    {
        var index = (int)kind;
        return index < _rules.Length ? _rules[index] : null;
    }

    /// <summary>
    /// Attempts to expand the expression statement as a GPU macro (see <see cref="MacroTranspiler"/>).
    /// </summary>
    public bool TryTranspileMacro(ExpressionStatementSyntax node, ref TranspileContext context) =>
        MacroTranspiler.TryTranspileMacro(node, ref context);

    /// <summary>
    /// Creates and returns a new registry populated with all default C# syntax transpiler rules.
    /// </summary>
    private static TranspilerRuleRegistry CreateDefault()
    {
        var registry = new TranspilerRuleRegistry();

        // Expressions
        registry.Register(SyntaxKind.Argument, new ArgumentTranspilerRule());
        registry.Register([
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxKind.AddAssignmentExpression,
            SyntaxKind.SubtractAssignmentExpression,
            SyntaxKind.MultiplyAssignmentExpression,
            SyntaxKind.DivideAssignmentExpression,
            SyntaxKind.ModuloAssignmentExpression,
            SyntaxKind.AndAssignmentExpression,
            SyntaxKind.ExclusiveOrAssignmentExpression,
            SyntaxKind.OrAssignmentExpression,
            SyntaxKind.LeftShiftAssignmentExpression,
            SyntaxKind.RightShiftAssignmentExpression,
            SyntaxKind.CoalesceAssignmentExpression
        ], new AssignmentExpressionTranspilerRule());
        registry.Register([
            SyntaxKind.AddExpression,
            SyntaxKind.SubtractExpression,
            SyntaxKind.MultiplyExpression,
            SyntaxKind.DivideExpression,
            SyntaxKind.ModuloExpression,
            SyntaxKind.LeftShiftExpression,
            SyntaxKind.RightShiftExpression,
            SyntaxKind.LogicalOrExpression,
            SyntaxKind.LogicalAndExpression,
            SyntaxKind.BitwiseOrExpression,
            SyntaxKind.BitwiseAndExpression,
            SyntaxKind.ExclusiveOrExpression,
            SyntaxKind.EqualsExpression,
            SyntaxKind.NotEqualsExpression,
            SyntaxKind.LessThanExpression,
            SyntaxKind.LessThanOrEqualExpression,
            SyntaxKind.GreaterThanExpression,
            SyntaxKind.GreaterThanOrEqualExpression,
            SyntaxKind.AsExpression,
            SyntaxKind.IsExpression,
            SyntaxKind.CoalesceExpression
        ], new BinaryExpressionTranspilerRule());
        registry.Register(SyntaxKind.CastExpression, new CastExpressionTranspilerRule());
        registry.Register(SyntaxKind.ConditionalExpression, new ConditionalExpressionTranspilerRule());
        registry.Register(SyntaxKind.ElementAccessExpression, new ElementAccessExpressionTranspilerRule());
        registry.Register(SyntaxKind.InvocationExpression, new InvocationExpressionTranspilerRule());
        registry.Register([
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.AnonymousMethodExpression
        ], new LambdaExpressionTranspilerRule());
        registry.Register([
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxKind.PointerMemberAccessExpression
        ], new MemberAccessExpressionTranspilerRule());
        registry.Register(SyntaxKind.ObjectCreationExpression, new ObjectCreationExpressionTranspilerRule());
        registry.Register(SyntaxKind.ImplicitObjectCreationExpression,
            new ImplicitObjectCreationExpressionTranspilerRule());
        registry.Register(SyntaxKind.ParenthesizedExpression, new ParenthesizedExpressionTranspilerRule());
        registry.Register(SyntaxKind.IsPatternExpression, new IsPatternExpressionTranspilerRule());
        registry.Register([
            SyntaxKind.PostIncrementExpression,
            SyntaxKind.PostDecrementExpression,
            SyntaxKind.SuppressNullableWarningExpression
        ], new PostfixUnaryExpressionTranspilerRule());
        registry.Register([
            SyntaxKind.UnaryPlusExpression,
            SyntaxKind.UnaryMinusExpression,
            SyntaxKind.BitwiseNotExpression,
            SyntaxKind.LogicalNotExpression,
            SyntaxKind.PreIncrementExpression,
            SyntaxKind.PreDecrementExpression,
            SyntaxKind.AddressOfExpression,
            SyntaxKind.PointerIndirectionExpression
        ], new PrefixUnaryExpressionTranspilerRule());
        registry.Register(SyntaxKind.SwitchExpression, new SwitchExpressionTranspilerRule());

        // Declarations
        registry.Register(SyntaxKind.LocalDeclarationStatement, new LocalDeclarationStatementTranspilerRule());
        registry.Register(SyntaxKind.VariableDeclaration, new VariableDeclarationTranspilerRule());

        return registry;
    }
}

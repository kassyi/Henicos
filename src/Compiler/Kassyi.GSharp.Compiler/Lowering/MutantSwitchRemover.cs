namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Removes the mutant switches that Stryker.NET writes into the code it mutates and keeps the original branch of each, so
/// that the device code G# copies out of the compilation is the code the user wrote.
/// </summary>
/// <remarks>
/// <para>
/// [WHY] Stryker instruments every file of the project it mutates, the files its <c>mutate</c> filter excludes included
/// (it only never activates their mutants). G# copies device code into code of its own: the state machines that lowering
/// builds, the managed versions of device functions and the bodies of inlined iterators. A mutant that does not compile
/// fails twice then, in the file and in the copy; Stryker removes it from the file but cannot match the error in the copy
/// to a mutant, and stops (#1191). Kernel code is never a mutation target, so the copies are made from the original code.
/// </para>
/// <para>
/// The shapes, as Stryker.NET 5.0.0 writes them (<c>ConditionalInstrumentationEngine</c>, <c>IfInstrumentationEngine</c>,
/// <c>MutationStore.Inject</c>), where a later mutant of the same node wraps the earlier one:
/// <list type="bullet">
///   <item><description><c>(&lt;switch&gt; ? &lt;mutated&gt; : &lt;original&gt;)</c> for an expression</description></item>
///   <item><description><c>if (&lt;switch&gt;) { &lt;mutated&gt; } else { &lt;original&gt; }</c> for a statement</description></item>
///   <item><description><c>{ if (&lt;switch&gt;) { &lt;mutated&gt; } else &lt;original block&gt; }</c> for a block</description></item>
/// </list>
/// The switch is a call to <c>IsActive(&lt;id&gt;)</c> of the <c>MutantControl</c> class that Stryker adds to the
/// compilation, in a namespace named <c>Stryker</c> followed by random characters. It is recognized by the symbol of that
/// call, so a compilation without Stryker's class has no switch, and the copies of a normal build are the input itself.
/// </para>
/// </remarks>
internal sealed class MutantSwitchRemover : CSharpSyntaxRewriter
{
    private const string HelperNamespacePrefix = "Stryker";
    private const string HelperClassName = "MutantControl";
    private const string SwitchMethodName = "IsActive";

    private static readonly ConditionalWeakTable<Compilation, IMethodSymbol[]> s_switchMethods = new();

    private readonly SemanticModel _semanticModel;
    private readonly IMethodSymbol[] _switchMethods;

    private MutantSwitchRemover(SemanticModel semanticModel, IMethodSymbol[] switchMethods)
    {
        _semanticModel = semanticModel;
        _switchMethods = switchMethods;
    }

    /// <summary>
    /// Returns <paramref name="node"/> itself when it holds no mutant switch; otherwise a copy outside the compilation in
    /// which every switch is replaced by its original branch.
    /// </summary>
    /// <param name="node">
    /// A node of the compilation of <paramref name="semanticModel"/>, or a copy of one whose nodes carry
    /// <see cref="LoweringAnnotations.OriginalSpanAnnotationKind"/>.
    /// </param>
    /// <param name="semanticModel">A model of the compilation; without one nothing is removed.</param>
    /// <remarks>
    /// Every node of the copy carries the original-span annotation of the node it was made from, so the passes after it
    /// find the symbols and report locations in the user's source (a node of the compilation is annotated first).
    /// </remarks>
    public static TNode Remove<TNode>(TNode node, SemanticModel? semanticModel) where TNode : SyntaxNode
    {
        if (semanticModel == null)
        {
            return node;
        }

        var compilation = semanticModel.Compilation;
        var switchMethods = s_switchMethods.GetValue(compilation, FindSwitchMethods);
        if (switchMethods.Length == 0)
        {
            return node;
        }

        var remover = new MutantSwitchRemover(semanticModel, switchMethods);
        if (!node.DescendantNodesAndSelf()
                .AsStackEnumerable()
                .Any(remover, static (descendant, r) => descendant is InvocationExpressionSyntax invocation &&
                                                       r.IsSwitch(invocation)))
        {
            return node;
        }

        var copy = compilation.ContainsSyntaxTree(node.SyntaxTree)
            ? LoweringAnnotations.AnnotateOriginalSpans(node, compilation)
            : node;
        return remover.Visit(copy) as TNode
               ?? throw new InvalidOperationException(
                   $"Removing the mutant switches turned a {node.Kind()} into a node of another kind.");
    }

    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        // A later mutant of a node wraps the switch of the earlier one: unwrap until the original is left
        while (node != null && TryGetOriginal(node, out var original))
        {
            node = original;
        }

        return base.Visit(node);
    }

    /// <summary>
    /// The original branch of <paramref name="node"/> when it is a switch Stryker wrote, with the trivia of the switch.
    /// </summary>
    private bool TryGetOriginal(SyntaxNode node, [NotNullWhen(true)] out SyntaxNode? original)
    {
        original = node switch
        {
            // (<switch> ? <mutated> : <original>)
            ParenthesizedExpressionSyntax { Expression: ConditionalExpressionSyntax conditional }
                when IsSwitchCondition(conditional.Condition) => conditional.WhenFalse.WithTriviaFrom(node),
            ConditionalExpressionSyntax conditional
                when IsSwitchCondition(conditional.Condition) => conditional.WhenFalse.WithTriviaFrom(node),
            // { if (<switch>) { <mutated> } else <original block> }: the wrapper of a block, or a statement switch that is
            // the only statement of its block, whose else block is then the original block as well
            BlockSyntax { Statements: [IfStatementSyntax ifStatement] }
                when TryGetElseBlock(ifStatement, out var elseBlock) => elseBlock.WithTriviaFrom(node),
            // if (<switch>) { <mutated> } else { <original statement> }
            IfStatementSyntax ifStatement when TryGetElseBlock(ifStatement, out var elseBlock) =>
                (elseBlock.Statements is [var statement] ? statement : elseBlock).WithTriviaFrom(node),
            _ => null
        };
        return original != null;
    }

    private bool TryGetElseBlock(IfStatementSyntax ifStatement, [NotNullWhen(true)] out BlockSyntax? elseBlock)
    {
        elseBlock = ifStatement is { Statement: BlockSyntax, Else.Statement: BlockSyntax block } &&
                    IsSwitchCondition(ifStatement.Condition)
            ? block
            : null;
        return elseBlock != null;
    }

    private bool IsSwitchCondition(ExpressionSyntax condition) =>
        condition is InvocationExpressionSyntax invocation && IsSwitch(invocation);

    // The name is checked first, so only the calls named IsActive are bound
    private bool IsSwitch(InvocationExpressionSyntax invocation) =>
        invocation is
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: SwitchMethodName },
            ArgumentList.Arguments.Count: 1
        } &&
        _semanticModel.GetSymbol(invocation) is IMethodSymbol method &&
        _switchMethods
            .AsStackEnumerable()
            .Any(method, static (switchMethod, called) => switchMethod.IsEqualTo(called));

    // public static bool IsActive(int id) of Stryker<random>.MutantControl, declared in the compilation's own source
    private static IMethodSymbol[] FindSwitchMethods(Compilation compilation)
    {
        var methods = ImmutableArray.CreateBuilder<IMethodSymbol>();
        foreach (var helperNamespace in compilation.Assembly.GlobalNamespace.GetNamespaceMembers())
        {
            if (!helperNamespace.Name.StartsWith(HelperNamespacePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var helperClass in helperNamespace.GetTypeMembers(HelperClassName))
            {
                helperClass.GetMembers(SwitchMethodName)
                    .AsStackEnumerable()
                    .OfType<IMethodSymbol>()
                    .Where(static method => method is
                    {
                        IsStatic: true,
                        ReturnType.SpecialType: SpecialType.System_Boolean,
                        Parameters: [{ Type.SpecialType: SpecialType.System_Int32 }]
                    })
                    .AddTo(methods);
            }
        }

        return methods.ToArray();
    }
}

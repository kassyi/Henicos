namespace Kassyi.GSharp.Compiler.Ir.Building;

/// <summary>The state of one build of a method body: the compilation, the variables, the tree ordinals.</summary>
/// <remarks>Lives within one generator transform or analyzer callback, like the IR it builds.</remarks>
internal sealed class IrBuildContext
{
    private SyntaxTree? _lastTree;
    private int _lastTreeOrdinal = -1;

    /// <summary>Creates the state for building the body of <paramref name="method"/>.</summary>
    /// <param name="compilation">The compilation that holds the method.</param>
    /// <param name="method">The method whose body is built.</param>
    /// <param name="declaration">The declaration of the method, the position of <see langword="this"/>.</param>
    /// <param name="cancellationToken">Cancels the build.</param>
    public IrBuildContext(Compilation compilation, IMethodSymbol method, SyntaxNode declaration,
        CancellationToken cancellationToken)
    {
        Compilation = compilation;
        Method = method;
        CancellationToken = cancellationToken;
        VoidType = compilation.GetSpecialType(SpecialType.System_Void);
        Variables = new IrVariableTable(this);
        DeclarationSource = SourceOf(declaration, synthesized: false);
        This = method.IsStatic ? null : IrVariable.CreateThis(method, Region, DeclarationSource);
    }

    /// <summary>The compilation that holds the method.</summary>
    public Compilation Compilation { get; }

    /// <summary>The method whose body is built.</summary>
    public IMethodSymbol Method { get; }

    /// <summary>Cancels the build.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>The <see langword="void"/> type, of an opaque expression without a type.</summary>
    public ITypeSymbol VoidType { get; }

    /// <summary>The locals and parameters of the build.</summary>
    public IrVariableTable Variables { get; }

    /// <summary>The variable of <see langword="this"/> in an instance member.</summary>
    public IrVariable? This { get; }

    /// <summary>The position of the method declaration.</summary>
    public IrSource DeclarationSource { get; }

    /// <summary>The copy of an inlined body being built; 0 outside any copy.</summary>
    public int Region => 0;

    /// <summary>The position of an operation; an implicit operation is <see cref="IrSource.Synthesized"/>.</summary>
    public IrSource SourceOf(IOperation operation) => SourceOf(operation.Syntax, operation.IsImplicit);

    /// <summary>The position of a node of a tree of the compilation.</summary>
    public IrSource SourceOf(SyntaxNode syntax, bool synthesized)
    {
        var tree = syntax.SyntaxTree;
        if (!ReferenceEquals(tree, _lastTree))
        {
            _lastTreeOrdinal = SemanticModelExtensions.GetSyntaxTreeOrdinal(Compilation, tree);
            _lastTree = tree;
        }

        if (_lastTreeOrdinal < 0)
        {
            throw new InvalidOperationException("The IR builder read syntax outside the compilation.");
        }

        var span = syntax.Span;
        return new IrSource(_lastTreeOrdinal, span.Start, span.Length, Region, synthesized);
    }
}

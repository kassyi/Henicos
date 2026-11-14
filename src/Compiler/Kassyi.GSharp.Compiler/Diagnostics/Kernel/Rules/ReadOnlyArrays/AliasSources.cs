namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;

/// <summary>
/// Everything a variable that can hold an array is given in a method body (<see cref="AliasKey"/>): its initializer,
/// the right side of each simple assignment, and the members of an object initializer. A variable that is also
/// assigned compound-wise, passed by <c>ref</c> or <c>out</c>, or deconstructed into has a source that cannot be read, so it is
/// left out. A struct that is assigned as a whole or passed by <c>ref</c> or <c>out</c> loses its fields the same way.
/// </summary>
internal sealed class AliasSources
{
    private readonly SemanticModel _model;
    private readonly Dictionary<AliasKey, ImmutableArray<ExpressionSyntax>.Builder> _sources = new();
    private readonly HashSet<AliasKey> _unreadable = new();
    private readonly HashSet<ISymbol> _structsChanged = new(SymbolEqualityComparer.Default);

    private AliasSources(SemanticModel model)
    {
        _model = model;
    }

    /// <summary>Reads the sources of every variable of <paramref name="body"/>.</summary>
    public static Dictionary<AliasKey, ImmutableArray<ExpressionSyntax>> Collect(SyntaxNode body, SemanticModel model)
    {
        var collector = new AliasSources(model);
        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case VariableDeclaratorSyntax { Initializer.Value: { } value } declarator:
                    collector.AddInitializer(declarator, value);
                    break;
                case AssignmentExpressionSyntax assignment:
                    collector.AddAssignment(assignment);
                    break;
                case ArgumentSyntax
                {
                    RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword
                } argument:
                    collector.MarkChanged(argument.Expression);
                    break;
            }
        }

        return collector.Build();
    }

    // var x = <value>;  or  var w = new S { Data = <value> };
    private void AddInitializer(VariableDeclaratorSyntax declarator, ExpressionSyntax value)
    {
        if (_model.GetDeclaredSymbol(declarator) is not ILocalSymbol local)
        {
            return;
        }

        if (local.Type.TryGetArrayLikeElementType(out _))
        {
            Add(new AliasKey(local, null), value);
        }

        AddObjectInitializer(local, value);
    }

    // x = <value>;  w.Data = <value>;  x += ...;  (a, b) = ...;
    private void AddAssignment(AssignmentExpressionSyntax assignment)
    {
        if (assignment.Parent is InitializerExpressionSyntax)
        {
            return;
        }

        if (assignment.Left is TupleExpressionSyntax or DeclarationExpressionSyntax)
        {
            foreach (var inner in assignment.Left.DescendantNodesAndSelf())
            {
                if (inner is IdentifierNameSyntax)
                {
                    MarkChanged((ExpressionSyntax)inner);
                }
            }

            return;
        }

        if (AliasKey.Of(assignment.Left, _model) is not { } key)
        {
            return;
        }

        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            _unreadable.Add(key);
            return;
        }

        if (key.Member == null && IsObjectCreation(assignment.Right))
        {
            // w = new S { Data = <value> } gives the fields of w their sources
            AddObjectInitializer(key.Variable, assignment.Right);
            return;
        }

        if (key.Member == null && !IsArrayLike(key.Variable))
        {
            // Any other value of a struct replaces its fields
            _structsChanged.Add(key.Variable);
            return;
        }

        Add(key, assignment.Right);
    }

    private static bool IsObjectCreation(ExpressionSyntax value) =>
        value is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax;

    private static bool IsArrayLike(ISymbol variable) => variable switch
    {
        ILocalSymbol local => local.Type.TryGetArrayLikeElementType(out _),
        IParameterSymbol parameter => parameter.Type.TryGetArrayLikeElementType(out _),
        _ => false
    };

    private void AddObjectInitializer(ISymbol variable, ExpressionSyntax value)
    {
        var initializer = value switch
        {
            ObjectCreationExpressionSyntax creation => creation.Initializer,
            ImplicitObjectCreationExpressionSyntax implicitCreation => implicitCreation.Initializer,
            _ => null
        };
        if (initializer == null)
        {
            return;
        }

        foreach (var member in initializer.Expressions)
        {
            if (member is AssignmentExpressionSyntax { Left: IdentifierNameSyntax name } assigned &&
                _model.GetSymbol(name) is IFieldSymbol { IsStatic: false } field)
            {
                Add(new AliasKey(variable, field), assigned.Right);
            }
        }
    }

    // A variable passed by ref or out, or deconstructed into: any value can end up in it
    private void MarkChanged(ExpressionSyntax expression)
    {
        if (AliasKey.Of(expression, _model) is not { } key)
        {
            return;
        }

        _unreadable.Add(key);
        _structsChanged.Add(key.Variable);
    }

    private void Add(AliasKey key, ExpressionSyntax source)
    {
        if (!_sources.TryGetValue(key, out var list))
        {
            _sources[key] = list = ImmutableArray.CreateBuilder<ExpressionSyntax>();
        }

        list.Add(source);
    }

    private Dictionary<AliasKey, ImmutableArray<ExpressionSyntax>> Build()
    {
        var result = new Dictionary<AliasKey, ImmutableArray<ExpressionSyntax>>();
        foreach (var pair in _sources)
        {
            var lostItsStruct = pair.Key.Member != null && _structsChanged.Contains(pair.Key.Variable);
            if (!_unreadable.Contains(pair.Key) && !lostItsStruct)
            {
                result[pair.Key] = pair.Value.ToImmutable();
            }
        }

        return result;
    }
}

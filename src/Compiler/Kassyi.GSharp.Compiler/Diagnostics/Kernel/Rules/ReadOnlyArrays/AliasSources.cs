namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;

/// <summary>
/// What each variable that can hold an array (<see cref="AliasKey"/>) is given in a method body. The readable values
/// are its initializer, the right side of each simple assignment or tuple-literal deconstruction, the members of an
/// object initializer, and the fields of a struct of the same type that is copied into it. A variable or struct that is
/// also given a value whose content is not read (an array element, a call, a property, a compound assignment, a
/// <c>ref</c> or <c>out</c> argument, a mutable <c>ref</c> or an address) holds what its declaration says on top of
/// those. A struct reset to its default value gives its fields <see langword="null"/>, which is neither.
/// </summary>
internal sealed class AliasSources
{
    private readonly SemanticModel _model;
    private readonly Dictionary<AliasKey, ImmutableArray<AliasSource>.Builder> _sources = new();

    // The variables and structs given a value whose content is not read
    private readonly HashSet<AliasKey> _unread = new();

    private AliasSources(SemanticModel model)
    {
        _model = model;
    }

    internal SemanticModel Model => _model;

    /// <summary>The readable sources recorded for each variable.</summary>
    public Dictionary<AliasKey, ImmutableArray<AliasSource>> Entries { get; } = new();

    /// <summary>Reads the sources of every variable of <paramref name="body"/>.</summary>
    public static AliasSources Collect(SyntaxNode body, SemanticModel model)
    {
        var collector = new AliasSources(model);
        foreach (var node in body.DescendantNodes())
        {
            collector.Visit(node);
        }

        foreach (var pair in collector._sources)
        {
            collector.Entries[pair.Key] = pair.Value.ToImmutable();
        }

        return collector;
    }

    /// <summary>
    /// Whether <paramref name="key"/>, or a struct that holds it, was given a value whose content is not read.
    /// </summary>
    public bool HoldsUnreadValue(AliasKey key) =>
        _unread
            .AsStackEnumerable()
            .Any(key, static (unread, target) => unread.Equals(target) || unread.IsProperPrefixOf(target));

    internal void Add(AliasKey key, AliasSource source)
    {
        if (!_sources.TryGetValue(key, out var list))
        {
            _sources[key] = list = ImmutableArray.CreateBuilder<AliasSource>();
        }

        list.Add(source);
    }

    // x = <value>;  w.Inner.Data = <value>;  var w = new S { Data = <value> };  w = other;
    internal void AddValue(AliasKey key, ExpressionSyntax value)
    {
        if (IsArrayLike(key.Last))
        {
            // x = x gives x nothing new; recorded, it would be a cycle that ReadOnlyArrayRoots takes as read-only
            if (AliasKey.Of(value, _model) is not { } sourceArrayKey || !key.Equals(sourceArrayKey))
            {
                Add(key, AliasSource.FromExpression(value));
            }

            return;
        }

        if (!IsStruct(key.Last))
        {
            return;
        }

        value = value.UnwrapParentheses();
        if (ObjectInitializerOf(value) is { } initializer)
        {
            AddObjectInitializer(key, initializer, !IsDefaultStructConstruction(value));
            return;
        }

        if (AliasStructCopySources.TryAdd(this, key, value) || IsDefaultStructValue(value))
        {
            return;
        }

        _unread.Add(key);
    }

    internal void MarkUnread(AliasKey key) => _unread.Add(key);

    private static InitializerExpressionSyntax? ObjectInitializerOf(ExpressionSyntax value) =>
        value.UnwrapParentheses() is BaseObjectCreationExpressionSyntax
        {
            Initializer: { RawKind: (int)SyntaxKind.ObjectInitializerExpression } initializer
        }
            ? initializer
            : null;

    private static bool IsArrayLike(ISymbol symbol) =>
        AliasStructCopySources.TypeOf(symbol) is { } type && type.TryGetArrayLikeElementType(out _);

    private static bool IsStruct(ISymbol symbol) =>
        AliasStructCopySources.TypeOf(symbol) is { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None };

    private void Visit(SyntaxNode node)
    {
        switch (node)
        {
            case VariableDeclaratorSyntax { Initializer.Value: { } value } declarator:
                AddInitializer(declarator, value);
                break;
            case AssignmentExpressionSyntax assignment:
                AddAssignment(assignment);
                break;
            case ArgumentSyntax
            {
                RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword
            } argument:
                AliasDeconstructionSources.MarkTargetUnread(this, argument.Expression);
                break;
            case RefExpressionSyntax reference when !IsReadOnlyRef(reference):
                AliasDeconstructionSources.MarkTargetUnread(this, reference.Expression);
                break;
            case PrefixUnaryExpressionSyntax addressOf when addressOf.IsKind(SyntaxKind.AddressOfExpression):
                AliasDeconstructionSources.MarkTargetUnread(this, addressOf.Operand);
                break;
        }
    }

    private void AddInitializer(VariableDeclaratorSyntax declarator, ExpressionSyntax value)
    {
        if (_model.GetDeclaredSymbol(declarator) is ILocalSymbol local)
        {
            AddValue(AliasKey.ForVariable(local), value);
        }
    }

    // x = <value>;  x += ...;  (a, b) = ...;
    private void AddAssignment(AssignmentExpressionSyntax assignment)
    {
        if (assignment.Parent is InitializerExpressionSyntax)
        {
            return;
        }

        if (assignment.Left is TupleExpressionSyntax or DeclarationExpressionSyntax)
        {
            AliasDeconstructionSources.AddDeconstruction(this, assignment.Left, assignment.Right);
            return;
        }

        if (AliasKey.Of(assignment.Left, _model) is not { } key)
        {
            return;
        }

        if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            AddValue(key, assignment.Right);
        }
        else
        {
            MarkUnread(key);
        }
    }

    // ref readonly var x = ref w.Inner; and x = ref w.Inner; for a ref readonly local cannot write through the alias
    private bool IsReadOnlyRef(RefExpressionSyntax reference)
    {
        SyntaxNode current = reference;
        while (current.Parent is ParenthesizedExpressionSyntax ||
               (current.Parent is ConditionalExpressionSyntax conditional && conditional.Condition != current))
        {
            current = current.Parent;
        }

        return current.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } =>
                _model.GetDeclaredSymbol(declarator) is ILocalSymbol { RefKind: RefKind.RefReadOnly },
            AssignmentExpressionSyntax assignment =>
                _model.GetSymbol(assignment.Left) is ILocalSymbol { RefKind: RefKind.RefReadOnly },
            _ => false
        };
    }

    private bool IsDefaultStructConstruction(ExpressionSyntax value) =>
        value.UnwrapParentheses() is BaseObjectCreationExpressionSyntax creation &&
        (creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0) &&
        _model.GetSymbol(creation) is IMethodSymbol
        {
            IsImplicitlyDeclared: true,
            ContainingType.IsValueType: true
        };

    private bool IsDefaultStructValue(ExpressionSyntax value)
    {
        value = value.UnwrapParentheses();
        return value switch
        {
            DefaultExpressionSyntax => true,
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.DefaultLiteralExpression) => true,
            BaseObjectCreationExpressionSyntax { Initializer: null } creation => IsDefaultStructConstruction(creation),
            _ => false
        };
    }

    // new S(...) { A = x, Inner = { B = y } }: the named fields get their values; with a constructor other than the
    // implicit one, the fields the initializer does not name hold what the constructor gave them
    private void AddObjectInitializer(AliasKey prefix, InitializerExpressionSyntax initializer, bool unnamedUnread)
    {
        HashSet<IFieldSymbol>? named = null;
        foreach (var member in initializer.Expressions)
        {
            if (member is not AssignmentExpressionSyntax { Left: IdentifierNameSyntax name } assigned ||
                _model.GetSymbol(name) is not IFieldSymbol { IsStatic: false } field)
            {
                continue;
            }

            (named ??= new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default)).Add(field);
            var memberKey = prefix.Append(field);
            if (assigned.Right.UnwrapParentheses() is InitializerExpressionSyntax nested &&
                nested.IsKind(SyntaxKind.ObjectInitializerExpression))
            {
                AddObjectInitializer(memberKey, nested, unnamedUnread);
            }
            else
            {
                AddValue(memberKey, assigned.Right);
            }
        }

        if (unnamedUnread && AliasStructCopySources.TypeOf(prefix.Last) is { } type)
        {
            MarkUnnamedFieldsUnread(prefix, type, named);
        }
    }

    private void MarkUnnamedFieldsUnread(AliasKey prefix, ITypeSymbol type, HashSet<IFieldSymbol>? named)
    {
        foreach (var member in type.GetMembers())
        {
            if (member is IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false } field &&
                (named is null || !named.Contains(field)))
            {
                _unread.Add(prefix.Append(field));
            }
        }
    }
}

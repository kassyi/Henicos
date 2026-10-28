namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.StackArrays;

/// <summary>How a child expression carries the reference of its parent's value.</summary>
internal enum Carry
{
    /// <summary>The child is the value itself, read the same way as the parent (<c>(a)</c>, <c>(int*)p</c>, <c>h.Field</c>).</summary>
    Through,

    /// <summary>The value of the child flows into the parent's value (an operand, an argument, an element).</summary>
    Value,

    /// <summary>The child is a variable whose address the parent takes (<c>&amp;a[0]</c>, <c>ref a[0]</c>).</summary>
    Address
}

/// <summary>
/// What an expression may refer to: the stack array storages reachable through the positions that carry a reference.
/// </summary>
/// <remarks>
/// A source is a stack array local (its own storage), a local or value parameter that holds storages, a
/// <c>stackalloc</c>, or a collection expression converted to a span. Any other expression passes the references of the
/// children that <see cref="AddChildren"/> names, one row per syntax kind; a kind without a row carries none. A child read
/// as a value is followed only when its type can hold a reference (<see cref="IsCarrier"/>), so an index, a length or an
/// argument of another type carries none.
/// </remarks>
internal sealed class StorageGatherer(SemanticModel model, Dictionary<ISymbol, List<Storage>> held)
{
    private readonly Dictionary<ITypeSymbol, bool> _carriers = new(SymbolEqualityComparer.Default);
    private readonly Stack<List<(ExpressionSyntax Child, Carry Carry)>> _childLists = new();

    public List<Storage> StorageOf(ExpressionSyntax value)
    {
        var storages = new List<Storage>();
        Gather(value, storages, Carry.Value);
        return storages;
    }

    /// <summary>
    /// Adds the storages that <paramref name="value"/> refers to; with <see cref="Carry.Address"/>, value is a variable
    /// whose address is taken, which refers to where the variable or element lives.
    /// </summary>
    public void Gather(ExpressionSyntax value, List<Storage> storages, Carry carry)
    {
        var address = carry == Carry.Address;

        // A ref expression has the type of the variable, which is often not a carrier (return ref a[0])
        if (!address && value is not RefExpressionSyntax && !IsCarrier(TypeOf(value)))
        {
            return;
        }

        if (TryAddSource(value, storages, address))
        {
            return;
        }

        var children = _childLists.Count > 0 ? _childLists.Pop() : [];
        try
        {
            AddChildren(value, children);
            foreach (var (child, childCarry) in children)
            {
                Gather(child, storages, childCarry == Carry.Through ? carry : childCarry);
            }
        }
        finally
        {
            children.Clear();
            _childLists.Push(children);
        }
    }

    // The sources: a node that is or holds a storage itself
    private bool TryAddSource(ExpressionSyntax value, List<Storage> storages, bool address)
    {
        switch (value)
        {
            case IdentifierNameSyntax identifier:
                GatherLocal(identifier, storages, address);
                return true;
            case StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax:
                storages.Add(new Storage(CppScopes.GetScope(value), StackArrayEscapeRule.StackAllocName));
                return true;
            case CollectionExpressionSyntax when model.GetTypeInfo(value).ConvertedType.IsArrayLikeType() &&
                                                 model.GetTypeInfo(value).ConvertedType is not IArrayTypeSymbol:
                storages.Add(new Storage(CppScopes.GetScope(value), StackArrayEscapeRule.CollectionExpressionName));
                return true;
            default:
                return false;
        }
    }

    // One row per syntax kind: the children that may carry the reference of the node's value, and how
    private static void AddChildren(ExpressionSyntax node, List<(ExpressionSyntax Child, Carry Carry)> children)
    {
        switch (node)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                children.Add((parenthesized.Expression, Carry.Through));
                break;
            case CastExpressionSyntax cast:
                children.Add((cast.Expression, Carry.Through));
                break;
            case CheckedExpressionSyntax checkedExpression:
                children.Add((checkedExpression.Expression, Carry.Through));
                break;
            case MemberAccessExpressionSyntax member:
                children.Add((member.Expression, Carry.Through));
                break;
            case ConditionalExpressionSyntax conditional:
                children.Add((conditional.WhenTrue, Carry.Through));
                children.Add((conditional.WhenFalse, Carry.Through));
                break;
            case BinaryExpressionSyntax binary:
                children.Add((binary.Left, Carry.Value));
                children.Add((binary.Right, Carry.Value));
                break;
            case PrefixUnaryExpressionSyntax prefix:
                children.Add((prefix.Operand, prefix.IsKind(SyntaxKind.AddressOfExpression) ? Carry.Address : Carry.Value));
                break;
            case PostfixUnaryExpressionSyntax postfix:
                children.Add((postfix.Operand, Carry.Value));
                break;
            case RefExpressionSyntax refExpression:
                children.Add((refExpression.Expression, Carry.Address));
                break;
            case ElementAccessExpressionSyntax element:
                children.Add((element.Expression, Carry.Value));
                break;
            case ConditionalAccessExpressionSyntax conditionalAccess:
                children.Add((conditionalAccess.Expression, Carry.Value));
                break;
            case AssignmentExpressionSyntax assignment:
                children.Add((assignment.Right, Carry.Value));
                break;
            case InvocationExpressionSyntax invocation:
                if (invocation.Expression is MemberAccessExpressionSyntax method)
                {
                    children.Add((method.Expression, Carry.Value));
                }

                AddArguments(invocation.ArgumentList, children);
                break;
            case BaseObjectCreationExpressionSyntax creation:
                if (creation.ArgumentList is { } creationArguments)
                {
                    AddArguments(creationArguments, children);
                }

                AddInitializer(creation.Initializer, children);
                break;
            case WithExpressionSyntax with:
                children.Add((with.Expression, Carry.Value));
                AddInitializer(with.Initializer, children);
                break;
            case TupleExpressionSyntax tuple:
                foreach (var argument in tuple.Arguments)
                {
                    children.Add((argument.Expression, Carry.Value));
                }

                break;
            case SwitchExpressionSyntax switchExpression:
                foreach (var arm in switchExpression.Arms)
                {
                    children.Add((arm.Expression, Carry.Value));
                }

                break;
        }
    }

    // The arguments of a call or a constructor: what the callee may return
    private static void AddArguments(ArgumentListSyntax arguments, List<(ExpressionSyntax Child, Carry Carry)> children)
    {
        foreach (var argument in arguments.Arguments)
        {
            children.Add((argument.Expression, Carry.Value));
        }
    }

    // The members of an object initializer (X = a) or a collection initializer element
    private static void AddInitializer(InitializerExpressionSyntax? initializer,
        List<(ExpressionSyntax Child, Carry Carry)> children)
    {
        if (initializer == null)
        {
            return;
        }

        foreach (var expression in initializer.Expressions)
        {
            children.Add((expression is AssignmentExpressionSyntax memberValue ? memberValue.Right : expression, Carry.Value));
        }
    }

    private void GatherLocal(IdentifierNameSyntax identifier, List<Storage> storages, bool address)
    {
        var symbol = model.GetSymbol(identifier);
        if (symbol is ILocalSymbol local && StackArrayLocals.IsStackArray(local, out var declarator))
        {
            storages.Add(new Storage(CppScopes.GetScope(declarator), local.Name));
        }

        // The address of a local that only holds a reference points at the local, not into a storage
        if (!address && symbol is ILocalSymbol or IParameterSymbol { RefKind: RefKind.None } &&
            held.TryGetValue(symbol, out var storagesOfSymbol))
        {
            storages.AddRange(storagesOfSymbol);
        }
    }

    /// <summary>
    /// The local function or lambda that an invocation runs, when the method declares it: a local function, or a delegate
    /// local initialized with a lambda.
    /// </summary>
    public IMethodSymbol? CalleeOfLocalCode(InvocationExpressionSyntax invocation)
    {
        switch (model.GetSymbol(invocation))
        {
            case IMethodSymbol { MethodKind: MethodKind.LocalFunction } localFunction:
                return localFunction;
            case IMethodSymbol { MethodKind: MethodKind.DelegateInvoke }
                when model.GetSymbol(invocation.Expression) is ILocalSymbol delegateLocal &&
                     delegateLocal.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var declarator) &&
                     declarator.Initializer?.Value is AnonymousFunctionExpressionSyntax lambda:
                return model.GetSymbol(lambda) as IMethodSymbol;
            default:
                return null;
        }
    }

    public ITypeSymbol? TypeOf(ExpressionSyntax expression)
    {
        var info = model.GetTypeInfo(expression);
        return info.ConvertedType ?? info.Type;
    }

    /// <summary>An array, a span, a pointer, or a struct with a field of such a type (tuples included) can refer to a storage.</summary>
    public bool IsCarrier(ITypeSymbol? type, int depth = 0)
    {
        switch (type)
        {
            case null:
                return false;
            case IArrayTypeSymbol or IPointerTypeSymbol:
                return true;
        }

        if (type.IsArrayLikeType())
        {
            return true;
        }

        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } || depth > 3)
        {
            return false;
        }

        if (_carriers.TryGetValue(type, out var known))
        {
            return known;
        }

        _carriers[type] = false;
        var carries = false;
        foreach (var member in type.GetMembers())
        {
            if (member is IFieldSymbol { IsStatic: false } field && IsCarrier(field.Type, depth + 1))
            {
                carries = true;
                break;
            }
        }

        _carriers[type] = carries;
        return carries;
    }

    public static bool TryFirst(List<Storage> storages, out Storage storage)
    {
        storage = storages.Count > 0 ? storages[0] : default;
        return storages.Count > 0;
    }
}

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Collects what one expression reads and writes, for <see cref="ExpressionEffects"/>. The bodies of the cases live in
/// the effect families (<see cref="OperatorEffects"/>, <see cref="MemberEffects"/>, <see cref="LocationEffects"/>,
/// <see cref="CallEffects"/>, <see cref="CreationEffects"/>), which take the walker by <c>ref</c>.
/// </summary>
internal struct EffectWalker(SemanticModel model)
{
    public VariableSet Reads;
    public VariableSet Writes;

    public readonly SemanticModel Model => model;

    public void AddRead(EffectRoot root)
    {
        foreach (var variable in root.Variables)
        {
            Reads.Add(variable);
        }

        if (root.IsMemory)
        {
            Reads.Memory = true;
        }
    }

    public void AddWrite(EffectRoot root)
    {
        foreach (var variable in root.Variables)
        {
            Writes.Add(variable);
        }

        if (root.IsMemory)
        {
            Writes.Memory = true;
        }
    }

    public void AddUnknown()
    {
        Reads.Memory = Reads.AllLocals = true;
        Writes.Memory = Writes.AllLocals = true;
    }

    // Whether a call of method (an accessor or an operator, or none) writes memory, as DeviceFunctionEffects says
    public readonly bool MayWriteMemory(IMethodSymbol? method) =>
        method != null && DeviceFunctionEffects.Classify(method, model.Compilation) == DeviceFunctionEffect.Memory;

    public void AddValue(ExpressionSyntax expression)
    {
        if (model.GetSafeConstantValue(expression).HasValue)
        {
            return;
        }

        OperatorEffects.AddConversions(ref this, expression);
        switch (expression)
        {
            case LiteralExpressionSyntax or DefaultExpressionSyntax or SizeOfExpressionSyntax or
                TypeOfExpressionSyntax or LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or
                DeclarationExpressionSyntax or BaseExpressionSyntax or PredefinedTypeSyntax:
                return;
            case ParenthesizedExpressionSyntax or CastExpressionSyntax or CheckedExpressionSyntax:
                AddValue(GetOperand(expression));
                return;
            case PostfixUnaryExpressionSyntax postfix:
                OperatorEffects.AddPostfix(ref this, postfix);
                return;
            case PrefixUnaryExpressionSyntax prefix:
                OperatorEffects.AddPrefix(ref this, prefix);
                return;
            case IdentifierNameSyntax identifier:
                MemberEffects.AddIdentifier(ref this, identifier);
                return;
            case MemberAccessExpressionSyntax access:
                AddMemberAccess(access);
                return;
            case ElementAccessExpressionSyntax element:
                MemberEffects.AddElementAccess(ref this, element);
                return;
            case AssignmentExpressionSyntax assignment:
                OperatorEffects.AddAssignment(ref this, assignment);
                return;
            case InvocationExpressionSyntax invocation:
                CallEffects.AddInvocation(ref this, invocation);
                return;
            case BaseObjectCreationExpressionSyntax creation:
                CreationEffects.AddObjectCreation(ref this, creation);
                return;
            case WithExpressionSyntax with:
                CreationEffects.AddWith(ref this, with);
                return;
            case ThisExpressionSyntax:
                Reads.Memory = true;
                return;
            case RefExpressionSyntax reference:
                AddRead(AddLocation(reference.Expression));
                return;
            default:
                AddChildren(expression);
                return;
        }
    }

    // The operand of a parenthesized, cast or checked expression, which evaluates to its operand alone
    private static ExpressionSyntax GetOperand(ExpressionSyntax expression) => expression switch
    {
        ParenthesizedExpressionSyntax parenthesized => parenthesized.Expression,
        CastExpressionSyntax cast => cast.Expression,
        CheckedExpressionSyntax @checked => @checked.Expression,
        _ => throw new InvalidOperationException($"Not a wrapping expression: {expression.Kind()}")
    };

    // Every expression under node, for the kinds whose operands are all evaluated (?:, binary operators, tuples,
    // switch and with expressions, patterns, initializers, collection expressions ...)
    public void AddChildren(SyntaxNode node)
    {
        foreach (var child in node.ChildNodes())
        {
            if (child is ExpressionSyntax expression)
            {
                AddValue(expression);
            }
            else if (child is not TypeSyntax)
            {
                AddChildren(child);
            }
        }
    }

    public void AddArgumentValues(SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        foreach (var argument in arguments)
        {
            AddValue(argument.Expression);
        }
    }

    private void AddMemberAccess(MemberAccessExpressionSyntax access)
    {
        if (MemberEffects.IsFrameAccess(access))
        {
            AddRead(AddLocation(access));
            return;
        }

        if (access.IsKind(SyntaxKind.PointerMemberAccessExpression))
        {
            MemberEffects.AddPointerMember(ref this, access);
            return;
        }

        switch (model.GetSymbol(access) ?? model.GetSymbol(access.Name))
        {
            case IFieldSymbol:
                AddRead(AddLocation(access));
                return;
            case IPropertySymbol { IsStatic: true } property:
                MemberEffects.AddPropertyRead(ref this, property);
                return;
            case IPropertySymbol property:
                MemberEffects.AddInstanceProperty(ref this, access, property);
                return;
            case IMethodSymbol { IsStatic: false }:
                AddValue(access.Expression);
                return;
            case null:
                MemberEffects.AddUnresolvedMember(ref this, access);
                return;
            default:
                return;
        }
    }

    /// <summary>
    /// Adds the reads that find the location of <paramref name="expression"/> (indices, receivers) and returns the
    /// variable it belongs to, without reading or writing the location itself.
    /// </summary>
    public EffectRoot AddLocation(ExpressionSyntax expression) => expression switch
    {
        ParenthesizedExpressionSyntax parenthesized => AddLocation(parenthesized.Expression),
        RefExpressionSyntax refExpression => AddLocation(refExpression.Expression),
        ConditionalExpressionSyntax conditional => LocationEffects.AddConditional(ref this, conditional),
        // A local of the state machine that lives in its frame: the local itself
        MemberAccessExpressionSyntax frame when MemberEffects.IsFrameAccess(frame) => AddLocation(frame.Name),
        DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax designation } =>
            LocationEffects.AddDeclaration(ref this, designation),
        IdentifierNameSyntax identifier => LocationEffects.AddIdentifier(ref this, identifier),
        MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression) =>
            LocationEffects.AddMember(ref this, access),
        ElementAccessExpressionSyntax element => LocationEffects.AddElement(ref this, element),
        PrefixUnaryExpressionSyntax indirection when indirection.IsKind(SyntaxKind.PointerIndirectionExpression) =>
            LocationEffects.AddIndirection(ref this, indirection),
        _ => LocationEffects.AddValue(ref this, expression)
    };
}

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// What evaluating an expression reads (<see cref="Reads"/>) and writes (<see cref="Writes"/>), as far as the order of
/// the operands of an enclosing expression can tell (<see cref="EvaluationOrder"/>).
/// </summary>
/// <remarks>
/// <para>
/// A local or by-value parameter is one variable; every other location (array and span elements, pointers, shared
/// memory, <c>ref</c> / <c>out</c> / <c>in</c> parameters, ref locals, static fields, fields of <c>this</c>) is
/// <see cref="VariableSet.Memory"/>. A field of a struct local is the local. A local or parameter that has another name
/// (its address is taken, a <c>ref</c> expression names it, or a lambda or local function captures it) is
/// <see cref="VariableSet.Memory"/> too, since it can be read and written without its name. A call reads <see cref="VariableSet.Memory"/>
/// (its callee may read anything) unless it is pure math over scalars, and writes what
/// <see cref="DeviceFunctionEffects"/> says: nothing, the variables of its <c>ref</c> / <c>out</c> arguments, or
/// <see cref="VariableSet.Memory"/>. Evaluating a <c>ref</c> / <c>out</c> / <c>in</c> argument only names its location
/// (<see cref="OfLocation"/>); a nested call reads the variable of its <c>ref</c> / <c>in</c> arguments and writes the
/// variable of its <c>ref</c> / <c>out</c> arguments as part of its own evaluation. A lambda is not evaluated.
/// </para>
/// <para>
/// [WHY] The same rule as Roslyn's <c>LocalRewriter.IsSafeForReordering</c> and <c>CanChangeValueBetweenReads</c>: a
/// read of a local is safe to reorder unless another operand writes that local. Unlike Roslyn, a call is classified by
/// its callee, since C++ does not keep the order even of positional arguments and most calls would otherwise need
/// temporaries.
/// </para>
/// </remarks>
internal readonly struct ExpressionEffects(VariableSet reads, VariableSet writes)
{
    public VariableSet Reads { get; } = reads;

    public VariableSet Writes { get; } = writes;

    /// <summary>Neither reads nor writes anything (a constant, <c>ThreadIndex.X</c>, a lambda).</summary>
    public bool IsEmpty => Reads.IsEmpty && Writes.IsEmpty;

    /// <summary>The effects of evaluating <paramref name="expression"/> for its value.</summary>
    public static ExpressionEffects Of(ExpressionSyntax expression, SemanticModel model)
    {
        var walker = new Walker(model);
        walker.AddValue(expression);
        return new ExpressionEffects(walker.Reads, walker.Writes);
    }

    /// <summary>
    /// The effects of evaluating <paramref name="expression"/> to the location it names (the target of an assignment, a
    /// <c>ref</c> / <c>out</c> / <c>in</c> argument): the reads of its index and receiver, without reading the location.
    /// With <paramref name="readsValue"/> the location is also read (the target of a compound assignment).
    /// </summary>
    public static ExpressionEffects OfLocation(ExpressionSyntax expression, SemanticModel model, bool readsValue = false)
    {
        var walker = new Walker(model);
        var root = walker.AddLocation(expression);
        if (readsValue)
        {
            walker.AddRead(root);
        }

        return new ExpressionEffects(walker.Reads, walker.Writes);
    }

    /// <summary>
    /// The variable that the location <paramref name="expression"/> belongs to, as a read of it alone (without the reads
    /// of its indices and receivers): what reading the target of a compound assignment reads.
    /// </summary>
    public static VariableSet RootOf(ExpressionSyntax expression, SemanticModel model)
    {
        var walker = new Walker(model);
        var root = walker.AddLocation(expression);
        var reads = default(VariableSet);
        foreach (var variable in root.Variables)
        {
            reads.Add(variable);
        }

        if (root.IsMemory)
        {
            reads.Memory = true;
        }

        return reads;
    }

    /// <summary>
    /// The effects of a call of <paramref name="target"/> whose arguments are already evaluated, with
    /// <paramref name="referenceArgument"/> (a local or parameter, or <see langword="null"/>) passed by <c>ref</c>: what
    /// evaluating an invocation adds for the call itself. An operator macro expansion uses it for the call of an inner
    /// operator's target with its context.
    /// </summary>
    public static ExpressionEffects OfCall(IMethodSymbol target, ISymbol? referenceArgument, SemanticModel model)
    {
        var walker = new Walker(model);
        CallEffects.AddCallOf(ref walker, target, referenceArgument);
        return new ExpressionEffects(walker.Reads, walker.Writes);
    }

    /// <summary>What evaluating this and <paramref name="other"/> reads and writes.</summary>
    public ExpressionEffects Union(in ExpressionEffects other) =>
        new(Reads.Union(other.Reads), Writes.Union(other.Writes));

    /// <summary>Whether this operand, evaluated before <paramref name="later"/>, could see or change what it does.</summary>
    public bool InterferesWith(in ExpressionEffects later) =>
        Writes.Overlaps(later.Reads) || Writes.Overlaps(later.Writes) || Reads.Overlaps(later.Writes);

    // The variables (for a ref conditional) that a location belongs to: symbols or names, and whether it touches memory
    internal readonly struct Root
    {
        public ImmutableArray<object> Variables { get; }

        public bool IsMemory { get; }

        public Root(object? variable, bool isMemory = false)
        {
            Variables = variable != null ? ImmutableArray.Create(variable) : ImmutableArray<object>.Empty;
            IsMemory = isMemory || variable == null;
        }

        public Root(ImmutableArray<object> variables, bool isMemory)
        {
            Variables = variables;
            IsMemory = isMemory || variables.IsEmpty;
        }

        public static Root Combine(Root whenTrue, Root whenFalse)
        {
            var isMemory = whenTrue.IsMemory || whenFalse.IsMemory;
            var builder = ImmutableArray.CreateBuilder<object>(whenTrue.Variables.Length + whenFalse.Variables.Length);
            builder.AddRange(whenTrue.Variables);
            foreach (var v in whenFalse.Variables)
            {
                if (!builder.Contains(v))
                {
                    builder.Add(v);
                }
            }

            return new Root(builder.ToImmutable(), isMemory);
        }
    }

    // Collects the reads and writes of one expression. The bodies of the cases live in the effect families
    // (OperatorEffects, MemberEffects, LocationEffects, CallEffects, CreationEffects), which take the walker by ref.
    internal struct Walker(SemanticModel model)
    {
        public VariableSet Reads;
        public VariableSet Writes;

        public readonly SemanticModel Model => model;

        public void AddRead(Root root)
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

        public void AddWrite(Root root)
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
        public Root AddLocation(ExpressionSyntax expression) => expression switch
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
}

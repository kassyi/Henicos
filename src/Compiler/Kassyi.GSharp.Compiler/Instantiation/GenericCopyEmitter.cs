namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// Writes the closed copy of a generic declaration from its <see cref="GenericCopyPlan"/>: the declaration without its
/// type parameters and constraints, under the copy name, with each planned node written as the plan says. It decides
/// nothing; every node keeps the original-span annotation and the node number of the node it came from.
/// </summary>
internal static class GenericCopyEmitter
{
    /// <summary>The annotation kind that marks the declaration of a copy.</summary>
    public const string CopyAnnotationKind = "GSharpGenericCopy";

    /// <summary>Writes the copy.</summary>
    /// <param name="declaration">
    /// The generic declaration with its nodes annotated (<see cref="LoweringAnnotations.AnnotateOriginalSpans"/> and
    /// <see cref="CopyNodeIndex.Annotate"/>).
    /// </param>
    /// <param name="plan">What to write differently.</param>
    /// <param name="name">The name of the copy (<see cref="GenericInstanceNames.GetCopyName"/>).</param>
    /// <param name="marker">
    /// A <see cref="CopyAnnotationKind"/> annotation of this copy alone, to find it again in the derived compilation.
    /// </param>
    public static MethodDeclarationSyntax Emit(
        MethodDeclarationSyntax declaration,
        GenericCopyPlan plan,
        string name,
        SyntaxAnnotation marker)
    {
        var body = new Writer(plan).Visit(declaration) as MethodDeclarationSyntax ??
                   throw new InvalidOperationException("Writing the copy of a method gave another kind of node.");
        return body
            .WithIdentifier(SyntaxFactory.Identifier(name).WithTriviaFrom(body.Identifier))
            .WithTypeParameterList(null)
            .WithConstraintClauses(default)
            .WithAdditionalAnnotations(marker);
    }

    private sealed class Writer(GenericCopyPlan plan) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (node is null || !CopyNodeIndex.TryGet(node, out var index) ||
                !plan.Rewrites.TryGetValue(index, out var rewrite))
            {
                return base.Visit(node);
            }

            var written = rewrite switch
            {
                CopyRewrite.TypeArgument typeArgument => WriteType(typeArgument.Type),
                CopyRewrite.ExplicitTypeArguments explicitArguments => WriteGenericName(
                    ((IdentifierNameSyntax)node).Identifier.ValueText, explicitArguments.TypeArguments),
                CopyRewrite.StaticExtensionCall call => WriteStaticCall(Inner<InvocationExpressionSyntax>(node), call),
                CopyRewrite.IdentityConstant constant =>
                    WriteConstant(constant.Type, IntLiteral(constant.Value), constant.Opaque),
                CopyRewrite.StaticMember member => WriteConstant(
                    member.Type,
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        WriteType(member.Type),
                        SyntaxFactory.IdentifierName(member.Name)),
                    member.Opaque),
                CopyRewrite.NameOf nameOf => SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(nameOf.Text)),
                CopyRewrite.ArgumentCall => Parenthesize(Inner<InvocationExpressionSyntax>(node).ArgumentList
                    .Arguments[0].Expression),
                CopyRewrite.FalseCall => SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression),
                CopyRewrite.MathCall math => WriteMathCall(Inner<InvocationExpressionSyntax>(node), math),
                CopyRewrite.NarrowedOperator narrowed =>
                    Narrow(Inner<ExpressionSyntax>(node), narrowed.Type, narrowed.Narrowing),
                CopyRewrite.NarrowedShift shift => Narrow(
                    WriteShift(Inner<BinaryExpressionSyntax>(node), shift.CountMask, shift.LeftMask),
                    shift.Type, shift.Narrowing),
                CopyRewrite.TypedNegation negation => WriteTypedNegation(Inner<PrefixUnaryExpressionSyntax>(node),
                    negation),
                CopyRewrite.NarrowedNegation negation => Narrow(
                    WriteZeroMinus(IntLiteral(0), Inner<PrefixUnaryExpressionSyntax>(node)),
                    negation.Type, negation.Narrowing),
                CopyRewrite.NarrowedCompoundDivision division =>
                    WriteCompound(Inner<AssignmentExpressionSyntax>(node), SyntaxKind.DivideExpression, division.Type,
                        division.Narrowing),
                CopyRewrite.NarrowedCompoundShift shift =>
                    WriteCompoundShift(Inner<AssignmentExpressionSyntax>(node), shift),
                _ => throw new InvalidOperationException($"No writer for the copy rewrite {rewrite.GetType().Name}."),
            };
            return Stamp(written.WithTriviaFrom(node), node);
        }

        // The node with its children written, without the node number: the number goes to what replaces it
        private TNode Inner<TNode>(SyntaxNode node) where TNode : SyntaxNode =>
            (base.Visit(node) as TNode ??
             throw new InvalidOperationException("Writing the children of a copied node changed its kind."))
            .WithoutAnnotations(CopyNodeIndex.AnnotationKind);

        private static TypeSyntax WriteType(ITypeSymbol type) => type.ToCsharpTypeSyntax();

        // <name><<type arguments>>
        private static SimpleNameSyntax WriteGenericName(string name, ImmutableArray<ITypeSymbol> arguments)
        {
            var types = SyntaxFactory.SeparatedList(arguments
                .AsStackEnumerable()
                .Select(static argument => argument.ToCsharpTypeSyntax())
                .ToArray()
            );
            return SyntaxFactory.GenericName(SyntaxFactory.Identifier(name), SyntaxFactory.TypeArgumentList(types));
        }

        // <extension class>.<name><<type arguments>>(<receiver>, <arguments>)
        private static ExpressionSyntax WriteStaticCall(
            InvocationExpressionSyntax call,
            CopyRewrite.StaticExtensionCall rewrite)
        {
            var access = (MemberAccessExpressionSyntax)call.Expression;
            var receiver = SyntaxFactory.Argument(access.Expression.WithoutTrivia());
            if (rewrite.Method.Parameters is [{ RefKind: RefKind.Ref }, ..])
            {
                receiver = receiver.WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword));
            }

            var name = rewrite.TypeArguments.IsEmpty
                ? (SimpleNameSyntax)SyntaxFactory.IdentifierName(rewrite.Method.Name)
                : WriteGenericName(rewrite.Method.Name, rewrite.TypeArguments);
            var target = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                WriteType(rewrite.Method.ContainingType),
                Stamp(name, access.Name)
            );
            var arguments = call.ArgumentList.WithArguments(call.ArgumentList.Arguments.Insert(0, receiver));
            return SyntaxFactory.InvocationExpression(Stamp(target, access), arguments);
        }

        // ((<type>)<value>); opaque: ((<type>)global::System.Math.Max(c, c)) with c the constant, a call that C# cannot
        // fold into the operation around it
        private static ExpressionSyntax WriteConstant(ITypeSymbol type, ExpressionSyntax value, bool opaque)
        {
            var constant = Parenthesize(SyntaxFactory.CastExpression(WriteType(type), value));
            if (!opaque)
            {
                return constant;
            }

            var max = SyntaxFactory.InvocationExpression(
                SyntaxFactory.ParseExpression("global::System.Math.Max"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList([
                    SyntaxFactory.Argument(constant),
                    SyntaxFactory.Argument(constant),
                ]))
            );
            return Parenthesize(SyntaxFactory.CastExpression(WriteType(type), max));
        }

        // global::System.Math.<name>(<arguments>), cast to the result type when there is one
        private static ExpressionSyntax WriteMathCall(InvocationExpressionSyntax call, CopyRewrite.MathCall math)
        {
            var written = SyntaxFactory.InvocationExpression(
                SyntaxFactory.ParseExpression("global::System.Math." + math.Name),
                call.ArgumentList.WithoutTrivia()
            );
            return math.ResultType is { } resultType
                ? Parenthesize(SyntaxFactory.CastExpression(WriteType(resultType), written))
                : written;
        }

        // (<left> op ((<count>) & <count mask>)), the left operand read as its low bits for >>> of a signed type
        private static BinaryExpressionSyntax WriteShift(BinaryExpressionSyntax shift, int countMask, int? leftMask)
        {
            var left = leftMask is { } mask ? Parenthesize(BitwiseAnd(shift.Left, mask)) : shift.Left;
            return shift.WithLeft(left).WithRight(Parenthesize(BitwiseAnd(shift.Right, countMask)));
        }

        // (((<type>)0) - (<operand>)) in the type itself
        private static ExpressionSyntax WriteTypedNegation(
            PrefixUnaryExpressionSyntax negation,
            CopyRewrite.TypedNegation rewrite) =>
            Parenthesize(WriteZeroMinus(
                Parenthesize(SyntaxFactory.CastExpression(WriteType(rewrite.Type), IntLiteral(0))),
                negation));

        // <zero> - (<operand>)
        private static BinaryExpressionSyntax WriteZeroMinus(
            ExpressionSyntax zero,
            PrefixUnaryExpressionSyntax negation) =>
            SyntaxFactory.BinaryExpression(SyntaxKind.SubtractExpression, zero, Parenthesize(negation.Operand));

        // <target> = <the operation on the target and the value, narrowed>
        private static ExpressionSyntax WriteCompound(
            AssignmentExpressionSyntax assignment,
            SyntaxKind binaryKind,
            ITypeSymbol type,
            Narrowing narrowing) =>
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                assignment.Left,
                Narrow(
                    SyntaxFactory.BinaryExpression(binaryKind, assignment.Left.WithoutTrivia(),
                        Parenthesize(assignment.Right)),
                    type,
                    narrowing));

        // <target> = <the shift of the target by the value, with its masks, narrowed>
        private static ExpressionSyntax WriteCompoundShift(
            AssignmentExpressionSyntax assignment,
            CopyRewrite.NarrowedCompoundShift compound) =>
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                assignment.Left,
                Narrow(
                    WriteShift(
                        SyntaxFactory.BinaryExpression(compound.Operator, assignment.Left.WithoutTrivia(),
                            assignment.Right),
                        compound.CountMask,
                        compound.LeftMask),
                    compound.Type,
                    compound.Narrowing));

        // Cast: ((<type>)(<value>)). Low bits: ((<type>)((<value>) & <mask>)). Sign-extended:
        // ((<type>)(((<value>) << <shift>) >> <shift>)). The last two never throw
        private static ExpressionSyntax Narrow(ExpressionSyntax value, ITypeSymbol type, Narrowing narrowing)
        {
            var inRange = narrowing switch
            {
                Narrowing.Cast => Parenthesize(value),
                Narrowing.LowBits lowBits => Parenthesize(BitwiseAnd(Parenthesize(value), lowBits.Mask)),
                Narrowing.SignExtended extended => Parenthesize(SignExtend(Parenthesize(value), extended.Shift)),
                _ => throw new InvalidOperationException($"No writer for the narrowing {narrowing.GetType().Name}."),
            };
            return Parenthesize(SyntaxFactory.CastExpression(WriteType(type), inRange));
        }

        // (<value> << <shift>) >> <shift>
        private static ExpressionSyntax SignExtend(ExpressionSyntax value, int shift)
        {
            var up = SyntaxFactory.BinaryExpression(SyntaxKind.LeftShiftExpression, value, IntLiteral(shift));
            return SyntaxFactory.BinaryExpression(SyntaxKind.RightShiftExpression, Parenthesize(up), IntLiteral(shift));
        }

        // <value> & <mask>
        private static ExpressionSyntax BitwiseAnd(ExpressionSyntax value, int mask) =>
            SyntaxFactory.BinaryExpression(SyntaxKind.BitwiseAndExpression, Parenthesize(value), IntLiteral(mask));

        private static LiteralExpressionSyntax IntLiteral(int value) =>
            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(value));

        private static ParenthesizedExpressionSyntax Parenthesize(ExpressionSyntax expression) =>
            expression as ParenthesizedExpressionSyntax ?? SyntaxFactory.ParenthesizedExpression(expression);

        // The node number of original at the root of what replaces it; its original span on every node written anew
        private static TNode Stamp<TNode>(TNode written, SyntaxNode original) where TNode : SyntaxNode
        {
            var spans = original.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind);
            var stamped = written.ReplaceNodes(
                written.DescendantNodes(),
                (_, rewritten) => rewritten.HasAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind)
                    ? rewritten
                    : rewritten.WithAdditionalAnnotations(spans)
            );
            var root = stamped
                .WithoutAnnotations(CopyNodeIndex.AnnotationKind)
                .WithoutAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind)
                .WithAdditionalAnnotations(spans);
            return CopyNodeIndex.TryGet(original, out var index)
                ? root.WithAdditionalAnnotations(CopyNodeIndex.Create(index))
                : root;
        }
    }
}

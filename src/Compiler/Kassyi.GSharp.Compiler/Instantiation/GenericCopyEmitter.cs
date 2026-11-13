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

            var written = rewrite.Kind switch
            {
                CopyRewriteKind.TypeArgument => WriteType(rewrite.Type),
                CopyRewriteKind.ExplicitTypeArguments => WriteGenericName(
                    ((IdentifierNameSyntax)node).Identifier.ValueText, rewrite.TypeArguments),
                CopyRewriteKind.StaticExtensionCall =>
                    WriteStaticCall(Inner<InvocationExpressionSyntax>(node), rewrite),
                CopyRewriteKind.IdentityConstant => WriteIdentity(rewrite),
                CopyRewriteKind.NameOfConstant => SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal((string?)rewrite.Constant ?? string.Empty)),
                CopyRewriteKind.NarrowedOperator => Narrow(Inner<ExpressionSyntax>(node), rewrite),
                CopyRewriteKind.NarrowedShift => Narrow(WriteMaskedShift(Inner<BinaryExpressionSyntax>(node), rewrite),
                    rewrite),
                CopyRewriteKind.NarrowedNegation => WriteNegation(Inner<PrefixUnaryExpressionSyntax>(node), rewrite),
                _ => throw new InvalidOperationException($"No writer for the copy rewrite {rewrite.Kind}."),
            };
            return Stamp(written.WithTriviaFrom(node), node);
        }

        // The node with its children written, without the node number: the number goes to what replaces it
        private TNode Inner<TNode>(SyntaxNode node) where TNode : SyntaxNode =>
            (base.Visit(node) as TNode ??
             throw new InvalidOperationException("Writing the children of a copied node changed its kind."))
            .WithoutAnnotations(CopyNodeIndex.AnnotationKind);

        private static TypeSyntax WriteType(ITypeSymbol? type) =>
            (type ?? throw new InvalidOperationException("A type rewrite of a generic copy has no type."))
            .ToCsharpTypeSyntax();

        // <name><<type arguments>>
        private static SimpleNameSyntax WriteGenericName(string name, ImmutableArray<ITypeSymbol> arguments)
        {
            if (arguments.IsDefaultOrEmpty)
            {
                return SyntaxFactory.IdentifierName(name);
            }

            var types = SyntaxFactory.SeparatedList(arguments
                .AsStackEnumerable()
                .Select(static argument => argument.ToCsharpTypeSyntax())
                .ToArray()
            );
            return SyntaxFactory.GenericName(SyntaxFactory.Identifier(name), SyntaxFactory.TypeArgumentList(types));
        }

        // <extension class>.<name><<type arguments>>(<receiver>, <arguments>)
        private static ExpressionSyntax WriteStaticCall(InvocationExpressionSyntax call, CopyRewrite rewrite)
        {
            var method = rewrite.Method ??
                         throw new InvalidOperationException(
                             "A static extension call of a generic copy has no method.");
            var access = (MemberAccessExpressionSyntax)call.Expression;
            var receiver = SyntaxFactory.Argument(access.Expression.WithoutTrivia());
            if (method.Parameters is [{ RefKind: RefKind.Ref }, ..])
            {
                receiver = receiver.WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword));
            }

            var target = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                WriteType(method.ContainingType),
                Stamp(WriteGenericName(method.Name, rewrite.TypeArguments), access.Name)
            );
            var arguments = call.ArgumentList.WithArguments(call.ArgumentList.Arguments.Insert(0, receiver));
            return SyntaxFactory.InvocationExpression(Stamp(target, access), arguments);
        }

        // ((<type>)<0 or 1>)
        private static ExpressionSyntax WriteIdentity(CopyRewrite rewrite)
        {
            var value = rewrite.Constant is int constant ? constant : 0;
            return Parenthesize(SyntaxFactory.CastExpression(WriteType(rewrite.Type), IntLiteral(value)));
        }

        // (<left> op ((<count>) & <mask>)), the left operand of >>> on a signed type as its low bits
        private static ExpressionSyntax WriteMaskedShift(BinaryExpressionSyntax shift, CopyRewrite rewrite)
        {
            var type = rewrite.Type ?? throw new InvalidOperationException("A narrowed shift has no type.");
            var left = shift.Left;
            if (shift.IsKind(SyntaxKind.UnsignedRightShiftExpression) && BuiltinNumerics.IsSignedPromoted(type))
            {
                left = Parenthesize(BitwiseAnd(left, BuiltinNumerics.GetWidthMask(type)));
            }

            var count = Parenthesize(BitwiseAnd(shift.Right, rewrite.Constant is int mask ? mask : 0));
            return shift.WithLeft(left).WithRight(count);
        }

        // ((<type>)0) - <operand> in the type itself, or brought back for a promoted type
        private static ExpressionSyntax WriteNegation(PrefixUnaryExpressionSyntax negation, CopyRewrite rewrite)
        {
            var type = rewrite.Type ?? throw new InvalidOperationException("A narrowed negation has no type.");
            var zero = BuiltinNumerics.IsPromoted(type)
                ? (ExpressionSyntax)IntLiteral(0)
                : Parenthesize(SyntaxFactory.CastExpression(WriteType(type), IntLiteral(0)));
            var difference = SyntaxFactory.BinaryExpression(SyntaxKind.SubtractExpression, zero,
                Parenthesize(negation.Operand));
            return BuiltinNumerics.IsPromoted(type) ? Narrow(difference, rewrite) : Parenthesize(difference);
        }

        // Checked: ((<type>)(<value>)). Unchecked: ((<type>)((<value>) & <mask>)) or
        // ((<type>)(((<value>) << <shift>) >> <shift>)), whose cast never throws
        private static ExpressionSyntax Narrow(ExpressionSyntax value, CopyRewrite rewrite)
        {
            var type = rewrite.Type ?? throw new InvalidOperationException("A narrowed operator has no type.");
            var inRange = Parenthesize(value);
            if (!rewrite.Checked)
            {
                inRange = BuiltinNumerics.IsSignedPromoted(type)
                    ? Parenthesize(SignExtend(inRange, BuiltinNumerics.GetSignShift(type)))
                    : Parenthesize(BitwiseAnd(inRange, BuiltinNumerics.GetWidthMask(type)));
            }

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

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

[Category(TestCategory.Compiler)]
public class IrSpellingTests
{
    private static Compilation Compilation => IrTestCases.Compile(IrTestCases.WhileCases);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Build_ParenthesizedOperands_CountsTheirParentheses()
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", "Spelling");
        var statements = method.Body.Statements;

        // Statement 1: var sum = ((a + (b)));
        var decl1 = Assert.IsType<IrLocalDeclaration>(statements[0]);
        var sumBinary = Assert.IsType<IrBinary>(decl1.Initializer);
        Assert.Equal(2, sumBinary.Parentheses);
        Assert.Equal(0, sumBinary.Left.Parentheses);
        Assert.Equal(1, sumBinary.Right.Parentheses);

        // Statement 3: data[(0)] = sum * named + 1_000;
        var exprStmt = Assert.IsType<IrExpressionStatement>(statements[2]);
        var assign = Assert.IsType<IrAssignment>(exprStmt.Expression);
        Assert.Equal(0, assign.Parentheses);
        Assert.Equal(0, assign.Value.Parentheses);

        var elementRef = Assert.IsType<IrElementReference>(assign.Target);
        Assert.Equal(1, elementRef.Index.Parentheses);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Build_ImplicitConversion_LeavesTheParenthesesToTheOperand()
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", "ImplicitConversion");
        var exprStmt = Assert.IsType<IrExpressionStatement>(method.Body.Statements[0]);
        var assign = Assert.IsType<IrAssignment>(exprStmt.Expression);

        var conv = Assert.IsType<IrConversion>(assign.Value);
        Assert.True(conv.IsImplicit);
        Assert.Equal(0, conv.Parentheses);
        Assert.Equal(1, conv.Operand.Parentheses);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Build_Literals_KeepTheirText()
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", "Spelling");
        var statements = method.Body.Statements;

        // Statement 2: int named = 0x10;
        var decl2 = Assert.IsType<IrLocalDeclaration>(statements[1]);
        var namedConst = Assert.IsType<IrConstant>(decl2.Initializer);
        Assert.Equal("0x10", namedConst.LiteralText);

        // Statement 3: data[(0)] = sum * named + 1_000;
        var exprStmt = Assert.IsType<IrExpressionStatement>(statements[2]);
        var assign = Assert.IsType<IrAssignment>(exprStmt.Expression);
        var target = Assert.IsType<IrElementReference>(assign.Target);
        var indexConst = Assert.IsType<IrConstant>(target.Index);
        Assert.Equal("0", indexConst.LiteralText);

        var addBinary = Assert.IsType<IrBinary>(assign.Value);
        var thousandConst = Assert.IsType<IrConstant>(addBinary.Right);
        Assert.Equal("1_000", thousandConst.LiteralText);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Build_CommentsBeforeAStatement_AreKeptInOrder()
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", "Spelling");
        var statements = method.Body.Statements;

        var expectedComments = ImmutableArray.Create("// first", "/* second */");
        Assert.True(expectedComments.SequenceEqual(statements[0].CommentsBefore));
        Assert.Empty(statements[1].CommentsBefore);
        Assert.Empty(statements[2].CommentsBefore);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Declarations", "deduced", nameof(IrDeclaredTypeForm.Deduced))]
    [InlineData("Declarations", "outDeduced", nameof(IrDeclaredTypeForm.Deduced))]
    [InlineData("Declarations", "named", nameof(IrDeclaredTypeForm.Named))]
    [InlineData("Declarations", "first", nameof(IrDeclaredTypeForm.Named))]
    [InlineData("Declarations", "second", nameof(IrDeclaredTypeForm.Named))]
    [InlineData("Declarations", "outNamed", nameof(IrDeclaredTypeForm.Named))]
    [InlineData("RefLocals", "deducedRef", nameof(IrDeclaredTypeForm.Deduced))]
    [InlineData("RefLocals", "namedRef", nameof(IrDeclaredTypeForm.Named))]
    [InlineData("Declarations", "data", nameof(IrDeclaredTypeForm.Named))]
    public void Build_Declarations_RecordHowTheTypeIsWritten(string methodName, string variableName,
        string expectedForm)
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", methodName);
        var variable = method.Parameters.FirstOrDefault(p => p.Name == variableName) ??
                       IrNodeChildren.DescendantsAndSelf(method.Body)
                           .OfType<IrLocalDeclaration>()
                           .Select(d => d.Variable)
                           .FirstOrDefault(v => v.Name == variableName) ??
                       IrNodeChildren.DescendantsAndSelf(method.Body)
                           .OfType<IrVariableReference>()
                           .Select(r => r.Variable)
                           .FirstOrDefault(v => v.Name == variableName) ??
                       method.Body.Locals.FirstOrDefault(v => v.Name == variableName);

        Assert.NotNull(variable);
        Assert.Equal(Enum.Parse<IrDeclaredTypeForm>(expectedForm), variable.DeclaredTypeForm);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void DeducesType_DeclarationForms_NameTheTypeWithoutAnInitializer()
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", "Declarations");
        var decls = method.Body.Statements.OfType<IrLocalDeclaration>().ToList();

        var deducedDecl = decls.Single(d => d.Variable.Name == "deduced");
        Assert.True(deducedDecl.DeducesType);

        var namedDecl = decls.Single(d => d.Variable.Name == "named");
        Assert.False(namedDecl.DeducesType);

        var uninitMethod = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops",
            "DeclaredWithoutInitializer");
        var laterDecl = uninitMethod.Body.Statements.OfType<IrLocalDeclaration>()
            .Single(d => d.Variable.Name == "later");
        Assert.False(laterDecl.DeducesType);

        var withoutInit = deducedDecl.Update(null);
        Assert.Equal(IrDeclaredTypeForm.Deduced, withoutInit.Variable.DeclaredTypeForm);
        Assert.False(withoutInit.DeducesType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CreateTemporary_AnyType_IsDeduced()
    {
        var intType = Compilation.GetSpecialType(SpecialType.System_Int32);
        var temp = IrVariable.CreateTemporary(intType, RefKind.None, "temp", default);
        Assert.Equal(IrDeclaredTypeForm.Deduced, temp.DeclaredTypeForm);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Rewrite_MarkedTree_KeepsEverySpellingAndUniformity()
    {
        var seenTypes = new HashSet<Type>();
        var methods = IrTestCases.BuildAll(IrTestCases.BuilderCases, "IrCases.Ir")
            .Concat(IrTestCases.BuildAll(IrTestCases.OrderCases, "OrderCases.Order"))
            .Concat(IrTestCases.BuildAll(IrTestCases.WhileCases, "IrWhileCases.Loops"))
            .ToList();

        Assert.NotEmpty(methods);

        foreach (var method in methods)
        {
            var marker = new MarkingRewriter();
            var markedBody = marker.VisitStatement(method.Body);
            var markedMethod = new IrMethod(method.Symbol, method.Parameters, method.This, (IrBlock)markedBody,
                method.Source);

            var rebuilder = new RebuildingRewriter();
            var rebuiltBody = rebuilder.VisitStatement(markedMethod.Body);

            bool hasRebuiltLeaf = IrNodeChildren.DescendantsAndSelf(markedMethod.Body).Any(n =>
                n is IrConstant or IrVariableReference or IrDefaultValue or IrDiscard or IrInitializedObject);
            if (hasRebuiltLeaf)
            {
                Assert.NotSame(markedMethod.Body, rebuiltBody);
            }
            else
            {
                Assert.Same(markedMethod.Body, rebuiltBody);
            }

            var markedNodes = IrNodeChildren.DescendantsAndSelf(markedMethod.Body).ToList();
            var rebuiltNodes = IrNodeChildren.DescendantsAndSelf(rebuiltBody).ToList();
            Assert.Equal(markedNodes.Count, rebuiltNodes.Count);

            for (int i = 0; i < markedNodes.Count; i++)
            {
                var marked = markedNodes[i];
                var rebuilt = rebuiltNodes[i];
                Assert.Equal(marked.GetType(), rebuilt.GetType());

                if (marked != rebuilt)
                {
                    seenTypes.Add(rebuilt.GetType());
                }

                if (marked is IrExpression markedExpr && rebuilt is IrExpression rebuiltExpr)
                {
                    Assert.Equal(markedExpr.Parentheses, rebuiltExpr.Parentheses);
                }

                if (marked is IrStatement markedStmt && rebuilt is IrStatement rebuiltStmt)
                {
                    Assert.True(markedStmt.CommentsBefore.SequenceEqual(rebuiltStmt.CommentsBefore));
                }

                if (marked is IrConstant markedConst && rebuilt is IrConstant rebuiltConst)
                {
                    Assert.Equal(markedConst.LiteralText, rebuiltConst.LiteralText);
                }

                if (marked is IrIf markedIf && rebuilt is IrIf rebuiltIf)
                {
                    Assert.Equal(markedIf.Uniformity, rebuiltIf.Uniformity);
                }

                if (marked is IrWhile markedWhile && rebuilt is IrWhile rebuiltWhile)
                {
                    Assert.Equal(markedWhile.Uniformity, rebuiltWhile.Uniformity);
                }

                if (marked is IrConditional markedCond && rebuilt is IrConditional rebuiltCond)
                {
                    Assert.Equal(markedCond.Uniformity, rebuiltCond.Uniformity);
                }

                if (marked is IrLogical markedLog && rebuilt is IrLogical rebuiltLog)
                {
                    Assert.Equal(markedLog.Uniformity, rebuiltLog.Uniformity);
                }

                if (marked is IrCoalesce markedCoal && rebuilt is IrCoalesce rebuiltCoal)
                {
                    Assert.Equal(markedCoal.Uniformity, rebuiltCoal.Uniformity);
                }
            }
        }

        // Test IrSequence manually since builder does not produce it
        var intType = Compilation.GetSpecialType(SpecialType.System_Int32);
        var dummySource = methods[0].Body.Source;
        var seqChild = new IrConstant(1, null, intType, dummySource);
        var seq = (IrSequence)new IrSequence(ImmutableArray<IrVariable>.Empty, ImmutableArray<IrStatement>.Empty,
            seqChild, IrCategory.Value, dummySource).WithParentheses(42);
        var newSeqChild = new IrConstant(2, null, intType, dummySource);
        var updatedSeq = seq.Update(seq.SideEffects, newSeqChild);
        Assert.NotSame(seq, updatedSeq);
        Assert.Equal(42, updatedSeq.Parentheses);
        seenTypes.Add(typeof(IrSequence));

        Type[] requiredTypes =
        [
            typeof(IrAddressOf),
            typeof(IrArrayCreation),
            typeof(IrAssignment),
            typeof(IrBinary),
            typeof(IrCall),
            typeof(IrCoalesce),
            typeof(IrCompoundAssignment),
            typeof(IrConditional),
            typeof(IrConversion),
            typeof(IrElementReference),
            typeof(IrFieldReference),
            typeof(IrIncrement),
            typeof(IrLogical),
            typeof(IrNullTest),
            typeof(IrPropertyReference),
            typeof(IrSequence),
            typeof(IrStructCreation),
            typeof(IrUnary),
            typeof(IrBlock),
            typeof(IrExpressionStatement),
            typeof(IrIf),
            typeof(IrLocalDeclaration),
            typeof(IrReturn),
            typeof(IrWhile)
        ];

        var missing = requiredTypes.Except(seenTypes).Select(t => t.Name).ToList();
        Assert.Empty(missing);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Expression")]
    [InlineData("Statement")]
    public void WithSplit_MarkedNode_KeepsTheSpelling(string kind)
    {
        Assert.NotEmpty(kind);
        var method = IrTestCases.BuildCase("Loop");
        if (kind == "Statement")
        {
            var opaqueStmt = IrNodeChildren.DescendantsAndSelf(method.Body)
                .OfType<IrOpaqueStatement>()
                .First();
            var marked = (IrOpaqueStatement)opaqueStmt.WithCommentsBefore(ImmutableArray.Create("// mark"));
            var updated = marked.WithSplit(IrOpaqueSplit.HoldsSplit);
            Assert.NotSame(marked, updated);
            Assert.Equal(marked.CommentsBefore, updated.CommentsBefore);
        }
        else
        {
            var builderMethods = IrTestCases.BuildAll(IrTestCases.BuilderCases, "IrCases.Ir");
            var opaqueExpr = builderMethods
                .SelectMany(m => IrNodeChildren.DescendantsAndSelf(m.Body))
                .OfType<IrOpaqueExpression>()
                .First();
            var marked = (IrOpaqueExpression)opaqueExpr.WithParentheses(7);
            var updated = marked.WithSplit(IrOpaqueSplit.HoldsSplit);
            Assert.NotSame(marked, updated);
            Assert.Equal(7, updated.Parentheses);
        }
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("IfElse", typeof(IrIf))]
    [InlineData("WhileWithBarrier", typeof(IrWhile))]
    [InlineData("ConditionalShuffle", typeof(IrConditional))]
    [InlineData("LogicalAnd", typeof(IrLogical))]
    [InlineData("Coalesce", typeof(IrCoalesce))]
    public void WithUniformity_MarkedNode_KeepsTheSpellingAndSetsTheValue(string methodName, Type expectedType)
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", methodName);
        var node = IrNodeChildren.DescendantsAndSelf(method.Body).First(n => n.GetType() == expectedType);
        Assert.NotNull(node);

        if (node is IrExpression expr)
        {
            var marked = expr.WithParentheses(9);
            IrExpression withUniformity;
            if (expectedType == typeof(IrConditional))
            {
                withUniformity = ((IrConditional)marked).WithUniformity(IrUniformity.Uniform);
            }
            else if (expectedType == typeof(IrLogical))
            {
                withUniformity = ((IrLogical)marked).WithUniformity(IrUniformity.Uniform);
            }
            else if (expectedType == typeof(IrCoalesce))
            {
                withUniformity = ((IrCoalesce)marked).WithUniformity(IrUniformity.Uniform);
            }
            else
            {
                throw new InvalidOperationException();
            }

            Assert.NotSame(marked, withUniformity);
            Assert.Equal(9, withUniformity.Parentheses);

            IrExpression same;
            if (expectedType == typeof(IrConditional))
            {
                same = ((IrConditional)withUniformity).WithUniformity(IrUniformity.Uniform);
            }
            else if (expectedType == typeof(IrLogical))
            {
                same = ((IrLogical)withUniformity).WithUniformity(IrUniformity.Uniform);
            }
            else if (expectedType == typeof(IrCoalesce))
            {
                same = ((IrCoalesce)withUniformity).WithUniformity(IrUniformity.Uniform);
            }
            else
            {
                throw new InvalidOperationException();
            }

            Assert.Same(withUniformity, same);
        }
        else if (node is IrStatement stmt)
        {
            var marked = stmt.WithCommentsBefore(ImmutableArray.Create("// test"));
            IrStatement withUniformity;
            if (expectedType == typeof(IrIf))
            {
                withUniformity = ((IrIf)marked).WithUniformity(IrUniformity.Uniform);
            }
            else if (expectedType == typeof(IrWhile))
            {
                withUniformity = ((IrWhile)marked).WithUniformity(IrUniformity.Uniform);
            }
            else
            {
                throw new InvalidOperationException();
            }

            Assert.NotSame(marked, withUniformity);
            Assert.Equal(marked.CommentsBefore, withUniformity.CommentsBefore);

            IrStatement same;
            if (expectedType == typeof(IrIf))
            {
                same = ((IrIf)withUniformity).WithUniformity(IrUniformity.Uniform);
            }
            else if (expectedType == typeof(IrWhile))
            {
                same = ((IrWhile)withUniformity).WithUniformity(IrUniformity.Uniform);
            }
            else
            {
                throw new InvalidOperationException();
            }

            Assert.Same(withUniformity, same);
        }
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("IfElse", typeof(IrIf))]
    [InlineData("WhileWithBarrier", typeof(IrWhile))]
    [InlineData("ConditionalShuffle", typeof(IrConditional))]
    [InlineData("LogicalAnd", typeof(IrLogical))]
    [InlineData("Coalesce", typeof(IrCoalesce))]
    public void Update_ChangedChild_KeepsTheUniformity(string methodName, Type expectedType)
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", methodName);
        var node = IrNodeChildren.DescendantsAndSelf(method.Body).First(n => n.GetType() == expectedType);

        if (node is IrIf @if)
        {
            var withUniformity = @if.WithUniformity(IrUniformity.ThreadVariant);
            var updated = withUniformity.Update(
                new IrDefaultValue(@if.Condition.Type, IrCategory.Value, @if.Condition.Source),
                withUniformity.Then, withUniformity.Else);
            Assert.NotSame(withUniformity, updated);
            Assert.Equal(IrUniformity.ThreadVariant, updated.Uniformity);
        }
        else if (node is IrWhile @while)
        {
            var withUniformity = @while.WithUniformity(IrUniformity.ThreadVariant);
            var updated = withUniformity.Update(
                new IrDefaultValue(@while.Condition.Type, IrCategory.Value, @while.Condition.Source),
                withUniformity.Body);
            Assert.NotSame(withUniformity, updated);
            Assert.Equal(IrUniformity.ThreadVariant, updated.Uniformity);
        }
        else if (node is IrConditional cond)
        {
            var withUniformity = cond.WithUniformity(IrUniformity.ThreadVariant);
            var updated = withUniformity.Update(
                new IrDefaultValue(cond.Condition.Type, IrCategory.Value, cond.Condition.Source),
                withUniformity.WhenTrue, withUniformity.WhenFalse);
            Assert.NotSame(withUniformity, updated);
            Assert.Equal(IrUniformity.ThreadVariant, updated.Uniformity);
        }
        else if (node is IrLogical log)
        {
            var withUniformity = log.WithUniformity(IrUniformity.ThreadVariant);
            var updated = withUniformity.Update(withUniformity.Left,
                new IrDefaultValue(log.Right.Type, IrCategory.Value, log.Right.Source));
            Assert.NotSame(withUniformity, updated);
            Assert.Equal(IrUniformity.ThreadVariant, updated.Uniformity);
        }
        else if (node is IrCoalesce coal)
        {
            var withUniformity = coal.WithUniformity(IrUniformity.ThreadVariant);
            var updated = withUniformity.Update(coal.Value,
                new IrDefaultValue(coal.WhenNull.Type, IrCategory.Value, coal.WhenNull.Source));
            Assert.NotSame(withUniformity, updated);
            Assert.Equal(IrUniformity.ThreadVariant, updated.Uniformity);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void WithSpelling_MovedToAnotherNode_LeavesTheOriginal()
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", "Spelling");
        var stmt1 = method.Body.Statements[0];
        Assert.Equal(2, stmt1.CommentsBefore.Length);

        // Move comments
        var emptied = stmt1.WithCommentsBefore(ImmutableArray<string>.Empty);
        Assert.NotSame(stmt1, emptied);
        Assert.Empty(emptied.CommentsBefore);
        Assert.Equal(2, stmt1.CommentsBefore.Length);

        var stmt2 = method.Body.Statements[1];
        var movedStmt2 = stmt2.WithCommentsBefore(stmt1.CommentsBefore);
        Assert.Equal(stmt1.CommentsBefore, movedStmt2.CommentsBefore);

        // Move parentheses
        var decl = Assert.IsType<IrLocalDeclaration>(stmt1);
        var binary = Assert.IsType<IrBinary>(decl.Initializer);
        Assert.Equal(2, binary.Parentheses);

        var inner = binary.WithParentheses(0);
        Assert.NotSame(binary, inner);
        Assert.Equal(0, inner.Parentheses);
        Assert.Equal(2, binary.Parentheses);

        var seq = (IrSequence)new IrSequence(ImmutableArray<IrVariable>.Empty, ImmutableArray<IrStatement>.Empty, inner,
            inner.Category, binary.Source).WithParentheses(binary.Parentheses);
        Assert.Equal(2, seq.Parentheses);
        Assert.Equal(0, seq.Value.Parentheses);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Build_NullForgivingAndParentheses_RecordsOuterSpellingSpanAndParentheses()
    {
        const string Source = """
            #nullable enable
            public static class OuterSpelling
            {
                public static void Cases(int[]? a, int[] b, int i, int[] r)
                {
                    r[0] = a![i];
                    r[1] = (a ?? b)![i];
                    r[2] = (a!)[i];
                    r[3] = ((a ?? b))[i];
                }
            }
            """;
        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var built = IrBuilder.TryBuild(IrTestCases.FindMethod(compilation, "OuterSpelling", "Cases"), compilation,
            CancellationToken.None);
        Assert.NotNull(built);

        (string Text, int Parentheses)[] expected =
        [
            ("a!", 0),
            ("(a ?? b)!", 1),
            ("(a!)", 0),
            ("((a ?? b))", 2)
        ];
        for (var k = 0; k < expected.Length; k++)
        {
            var stmt = Assert.IsType<IrExpressionStatement>(built.Body.Statements[k]);
            var assign = Assert.IsType<IrAssignment>(stmt.Expression);
            var element = Assert.IsType<IrElementReference>(assign.Value);
            var receiver = element.Receiver;
            var location = receiver.SpellingSource.ToLocation(compilation);
            Assert.Equal(expected[k].Text, location.SourceTree?.GetText().ToString(location.SourceSpan));
            Assert.Equal(expected[k].Parentheses, receiver.Parentheses);

            var updated = element.Update(receiver.WithParentheses(receiver.Parentheses + 1), element.Index);
            var withSpan = IrExpression.WithSpelling(element, 1, new TextSpan(0, 1));
            var rebuilt = withSpan.Update(element.Receiver, element.Index.WithParentheses(1));
            Assert.Equal(new TextSpan(0, 1), rebuilt.SpellingSpan);
            Assert.Equal(expected[k].Text,
                updated.Receiver.SpellingSource.ToLocation(compilation).SourceTree?.GetText()
                    .ToString(updated.Receiver.SpellingSpan));
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Classify_SpelledStatements_KeepTheirSpelling()
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", "CommentedBarrier");
        var compilation = Compilation;
        var classified = IrClassifier.Classify(method, compilation);

        Assert.True(ImmutableArray.Create("// wait for the block")
            .SequenceEqual(classified.Body.Statements[0].CommentsBefore));

        var stmt2 = Assert.IsType<IrExpressionStatement>(classified.Body.Statements[1]);
        var assign = Assert.IsType<IrAssignment>(stmt2.Expression);
        var call = Assert.IsType<IrCall>(assign.Value);

        Assert.True(call.Classification.IsClassified);
        Assert.Equal(1, call.Parentheses);

        var unclassifiedStmt2 = Assert.IsType<IrExpressionStatement>(method.Body.Statements[1]);
        var unclassifiedAssign = Assert.IsType<IrAssignment>(unclassifiedStmt2.Expression);
        Assert.NotSame(unclassifiedAssign.Value, call);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Apply_ConditionalWithAUniformity_GivesItToTheIf()
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", "ConditionalShuffle");
        var compilation = Compilation;
        var classified = IrClassifier.Classify(method, compilation);

        var condMarker = new ConditionalUniformityRewriter();
        var markedMethod = new IrMethod(classified.Symbol, classified.Parameters, classified.This,
            (IrBlock)condMarker.VisitStatement(classified.Body), classified.Source);

        var plans = IrSpillPlanner.Plan(markedMethod, compilation);
        Assert.Single(plans.Statements);

        var rewritten = IrSpillRewriter.Apply(plans.Statements[0]);
        Assert.Equal(IrSpillApplySkip.None, rewritten.Skip);

        Assert.True(rewritten.TryGetLowered(out var lowered));
        var ifStmt = Assert.Single(lowered.Statements.OfType<IrIf>());
        Assert.Equal(IrUniformity.ThreadVariant, ifStmt.Uniformity);
    }

    private sealed class MarkingRewriter : IrRewriter
    {
        private int _counter;

        public override IrExpression VisitExpression(IrExpression expression)
        {
            var visited = base.VisitExpression(expression);
            int k = ++_counter;
            visited = visited.WithParentheses(k);
            if (visited is IrConditional cond)
            {
                visited = cond.WithUniformity((k % 2 == 0) ? IrUniformity.Uniform : IrUniformity.ThreadVariant);
            }
            else if (visited is IrLogical log)
            {
                visited = log.WithUniformity((k % 2 == 0) ? IrUniformity.Uniform : IrUniformity.ThreadVariant);
            }
            else if (visited is IrCoalesce coal)
            {
                visited = coal.WithUniformity((k % 2 == 0) ? IrUniformity.Uniform : IrUniformity.ThreadVariant);
            }
            return visited;
        }

        public override IrStatement VisitStatement(IrStatement statement)
        {
            var visited = base.VisitStatement(statement);
            int k = ++_counter;
            visited = visited.WithCommentsBefore(ImmutableArray.Create("// " + k));
            if (visited is IrIf @if)
            {
                visited = @if.WithUniformity((k % 2 == 0) ? IrUniformity.Uniform : IrUniformity.ThreadVariant);
            }
            else if (visited is IrWhile @while)
            {
                visited = @while.WithUniformity((k % 2 == 0) ? IrUniformity.Uniform : IrUniformity.ThreadVariant);
            }
            return visited;
        }
    }

    private sealed class RebuildingRewriter : IrRewriter
    {
        public override IrExpression VisitExpression(IrExpression expression)
        {
            switch (expression)
            {
                case IrConstant c:
                    return new IrConstant(c.Value, c.Origin, c.Type, c.Source, c.LiteralText)
                        .WithParentheses(c.Parentheses);
                case IrVariableReference v:
                    return new IrVariableReference(v.Variable, v.IsDeclaration, v.Category, v.ConstantValue,
                        v.Source).WithParentheses(v.Parentheses);
                case IrDefaultValue d:
                    return new IrDefaultValue(d.Type, d.Category, d.Source)
                        .WithParentheses(d.Parentheses);
                case IrDiscard dis:
                    return new IrDiscard(dis.Type, dis.Source)
                        .WithParentheses(dis.Parentheses);
                case IrInitializedObject o:
                    return new IrInitializedObject(o.Type, o.Source)
                        .WithParentheses(o.Parentheses);
                default:
                    return base.VisitExpression(expression);
            }
        }
    }

    private sealed class ConditionalUniformityRewriter : IrRewriter
    {
        public override IrExpression VisitExpression(IrExpression expression)
        {
            var visited = base.VisitExpression(expression);
            if (visited is IrConditional cond)
            {
                return cond.WithUniformity(IrUniformity.ThreadVariant);
            }
            return visited;
        }
    }
}

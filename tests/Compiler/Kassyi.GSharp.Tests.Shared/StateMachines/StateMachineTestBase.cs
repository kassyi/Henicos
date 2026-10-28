#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Dialects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VerifyXunit;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

public abstract class StateMachineTestBase
{
    protected static Task VerifyTransformation(Action action, Func<BlockSyntax, SyntaxNode> transform,
        [CallerArgumentExpression(nameof(action))]
        string source = "",
        [CallerMemberName] string methodName = "")
    {
        var lambda = (ParenthesizedLambdaExpressionSyntax)SyntaxFactory.ParseExpression(source);
        // No semantic model binds the block: its barriers and shuffles are annotated by spelling (TestSyncAnnotator)
        var block = TestSyncAnnotator.Annotate((BlockSyntax)lambda.Body);
        var transformed = transform(block);
        var formatted = transformed.NormalizeWhitespace().ToFullString();

        var wrapped = $$"""
            public class {{methodName}}_Snapshot
            {
                public void LoweredMethod()
            {{formatted}}
            }
            """;
        return Verifier.Verify(wrapped, "cs").UseFastHashSnapshotName(methodName);
    }

    protected static Task VerifyPipelineLowerFromTestCase(
        string testCaseFile,
        Func<BlockSyntax, SemanticModel, BlockSyntax>? preLowerTransform = null,
        Action<LoweredStateMachine, SemanticModel>? inspect = null,
        [CallerMemberName] string methodName = "")
    {
        var (lowered, model) = LowerTestCase(testCaseFile, preLowerTransform);

        inspect?.Invoke(lowered, model);

        var dialect = new StandardCppDialect();
        var fieldsComment = string.Join(
            Environment.NewLine,
            lowered.ContextFields.Select(f =>
            {
                var symbol = model.GetDeclaredSymbol<ISymbol>(f);
                var region = LoweringIdentity.GetRegion(f);
                var cppName = CppNameResolver.Resolve(symbol, f.Identifier.ValueText, dialect, region);
                return $"    // {f.Identifier.ValueText} -> {cppName}";
            }));
        var formatted = lowered.LoweredBody.NormalizeWhitespace().ToFullString();

        var wrapped = $$"""
            public class {{methodName}}_Snapshot
            {
            {{fieldsComment}}
                public void LoweredMethod()
            {{formatted}}
            }
            """;
        return Verifier.Verify(wrapped, "cs").UseFastHashSnapshotName(methodName);
    }

    /// <summary>
    /// Lowers a <c>TestCases/StateMachines</c> kernel like <see cref="VerifyPipelineLowerFromTestCase"/>, then names its
    /// context fields and rewrites its context accesses as the managed C# output does
    /// (<see cref="ManagedContextFields"/>, <see cref="ContextLocalRewriter"/>). The snapshot lists each hoisted
    /// declarator with its managed field name, followed by the rewritten body. <paramref name="inspect"/> receives the
    /// names of the ref struct locals declared on every resume, the names of the context class fields, and the body.
    /// </summary>
    protected static Task VerifyManagedPipelineLowerFromTestCase(
        string testCaseFile,
        Func<BlockSyntax, SemanticModel, BlockSyntax>? preLowerTransform = null,
        Action<IReadOnlyList<string>, IReadOnlyList<string>, SyntaxNode>? inspect = null,
        [CallerMemberName] string methodName = "")
    {
        var (lowered, model) = LowerTestCase(testCaseFile, preLowerTransform);

        var (localInits, regularFields, unhoistedLocals, renamedLocals) =
            ManagedContextFields.ProcessContextFields(lowered.ContextFields, model, methodName);
        var rewritten = new ContextLocalRewriter(new HashSet<DeclarationKey>(unhoistedLocals.Keys), renamedLocals, model, methodName,
                                ManagedCSharpTranspiler.CreateResumeInitialization(unhoistedLocals, zeroFill: true))
                            .Visit(lowered.LoweredBody)
                        ?? throw new InvalidOperationException("Context local rewriting removed the lowered body.");

        inspect?.Invoke(
            localInits.Select(static f => f.Name).ToArray(),
            regularFields.Select(static f => f.Name).ToArray(),
            rewritten);

        var fields = localInits.Concat(regularFields).ToArray();

        var fieldsComment = string.Join(
            Environment.NewLine,
            fields.Select(static f => $"    // {f.Declarator.Identifier.ValueText} -> {f.Name}"));
        var formatted = rewritten.NormalizeWhitespace().ToFullString();

        var wrapped = $$"""
            public class {{methodName}}_Snapshot
            {
            {{fieldsComment}}
                public void LoweredMethod()
            {{formatted}}
            }
            """;
        return Verifier.Verify(wrapped, "cs").UseFastHashSnapshotName(methodName);
    }

    private static (LoweredStateMachine Lowered, SemanticModel Model) LowerTestCase(
        string testCaseFile,
        Func<BlockSyntax, SemanticModel, BlockSyntax>? preLowerTransform)
    {
        var (compilation, tree, model) = RoslynTestUtility.GetTestCaseContext(
            Path.Combine("StateMachines", testCaseFile));
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First();

        if (preLowerTransform == null)
        {
            return (AstLoweringPipeline.Lower(method, model), model);
        }

        var body = method.Body ?? throw new InvalidOperationException("Test case method must have a block body.");
        var annotated = LoweringAnnotations.AnnotateOriginalSpans(body, compilation);
        var expanded = preLowerTransform(annotated, model);
        return (AstLoweringPipeline.Lower(expanded, model), model);
    }

    protected static Task VerifyStateMachineLower(Action action,
        [CallerArgumentExpression(nameof(action))]
        string source = "", [CallerMemberName] string methodName = "") =>
        VerifyTransformation(action, b => new StateMachineLowerer().Lower(b), source, methodName);

    protected static Task VerifyBranchRewrite(Action action,
        [CallerArgumentExpression(nameof(action))]
        string source = "", [CallerMemberName] string methodName = "") =>
        VerifyTransformation(action, b => new BranchRewriter().Rewrite(b), source, methodName);

    protected static Task VerifyLoopRewrite(Action action,
        [CallerArgumentExpression(nameof(action))]
        string source = "", [CallerMemberName] string methodName = "") =>
        VerifyTransformation(action, b => new LoopRewriter().Rewrite(b), source, methodName);

    protected static Task VerifyWarpRewrite(Action action,
        [CallerArgumentExpression(nameof(action))]
        string source = "",
        [CallerMemberName] string methodName = "") =>
        VerifyTransformation(action, b => new WarpInstructionRewriter().Rewrite(b), source, methodName);

    // Without a semantic model only the barriers and warp shuffles written in the block split the state machine
    // (annotated by TestSyncAnnotator)
    protected static Task VerifyPipelineLower(Action action,
        [CallerArgumentExpression(nameof(action))]
        string source = "", [CallerMemberName] string methodName = "") =>
        VerifyTransformation(action, b => AstLoweringPipeline.Lower(b).LoweredBody, source, methodName);

    protected static Task VerifyHoist(Action action,
        [CallerArgumentExpression(nameof(action))]
        string source = "", [CallerMemberName] string methodName = "") =>
        VerifyTransformation(action, b =>
        {
            var (rewrittenBlock, _) = new ContextHoister().Hoist(b);
            return new StateMachineLowerer().Lower(rewrittenBlock);
        }, source, methodName);
}

// Mock class to allow compiling test delegates containing Gpu.* calls without depending on the actual Intrinsics.
public static class Gpu
{
    public delegate void UnrollAction<TContext>(ref TContext ctx, int index);

    public static void SyncThreads()
    {
    }

    public static void SyncWarp()
    {
    }

    public static void SyncGrid()
    {
    }

    public static void SyncGrid(Action body)
    {
    }

    public static void ForUnroll(int start, int count, Action<int> body)
    {
    }

    public static void ForUnroll<TContext>(ref TContext ctx, int start, int count, UnrollAction<TContext> body)
    {
    }
}

// Mock of the warp intrinsics with the argument layout the state machine lowering expects (mask, value, lane)
public static class Warp
{
    public static int ShuffleSync(uint mask, int value, int srcLane) => value;
    public static int ShuffleXorSync(uint mask, int value, int laneMask) => value;
}

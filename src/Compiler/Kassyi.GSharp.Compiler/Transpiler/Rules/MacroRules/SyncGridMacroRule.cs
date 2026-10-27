namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Transpiles a <c>Gpu.SyncGrid(body)</c> statement (CUDA): the statements of the body with a cooperative grid sync
/// (<c>cg::this_grid().sync();</c>) between its top-level statements.
/// </summary>
/// <remarks>
/// <para>
/// The statements and the gaps that get a sync come from <see cref="SyncGridBody"/>, which the CPU lowering
/// (<see cref="PreLoweringExpander"/>) reads too, so both dialects synchronize at the same places. A nested block
/// (<c>for</c>, <c>if</c>, a loop macro body, a local function) is one statement and is emitted as usual, without syncs
/// inside it: a grid sync that only some threads reach deadlocks (#626). A <c>Gpu.SyncGrid(body)</c> written inside
/// such a block is a statement of its own and gets its own syncs.
/// </para>
/// <para>
/// A <c>Gpu.SyncGrid(body)</c> that is itself a statement of the body is flattened into the gaps of the enclosing body,
/// and its statements are emitted in a scope of their own, as the lambda scopes its locals in C#.
/// </para>
/// </remarks>
internal sealed class SyncGridMacroRule : IMacroTranspilerRule
{
    private const string GridSync = "cg::this_grid().sync();";

    public bool TryTranspile(
        InvocationExpressionSyntax invocation,
        ExpressionStatementSyntax statement,
        GpuMacroCall call,
        ref TranspileContext context)
    {
        if (call is not { Kind: GpuMacroKind.SyncGrid, HasBody: true })
        {
            return false;
        }

        if (!GpuCodeConstraints.TryGetSyncGridBody(invocation, out var lambdaExpr))
        {
            var args = invocation.ArgumentList.Arguments;
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, args.Count > 0 ? args[0] : invocation,
                GpuCodeConstraints.SyncGridRequiresLambda);
            return false;
        }

        var statements = SyncGridBody.Flatten(lambdaExpr, context.Query.SemanticModel);

        // [WHY] Completely eliminate empty Gpu.SyncGrid(() => {}) as a No-op (zero CUDA C++ output).
        if (statements.HasExecutableStatements)
        {
            var index = 0;
            var bodyIds = new int[statements.BodyParents.Length];
            EmitBody(statements, bodyIndex: 0, bodyIds, ref index, ref context);
        }

        return true;
    }

    // Emits, in a scope, the statements of body bodyIndex from elements[index] on: its own statements, and the bodies
    // nested in it through EmitBody again. Returns with index at the first statement outside the body. A body that
    // returns declares its done flag first; a declaration that a guard would hide is declared there too.
    private static void EmitBody(
        SyncGridStatements statements,
        int bodyIndex,
        int[] bodyIds,
        ref int index,
        ref TranspileContext context)
    {
        var elements = statements.Elements;
        var model = context.Query.SemanticModel;
        bodyIds[bodyIndex] = context.Emitter.SyncGridBodyCounter++;
        using (context.Emitter.Writer.Scope())
        {
            if (statements.BodyHasReturn(bodyIndex))
            {
                context.Emitter.Writer.AppendLine($"bool {DoneFlagName(bodyIds[bodyIndex])} = false;");
            }

            DeclareSplitLocals(statements, bodyIndex, index, ref context);
            while (index < elements.Length && statements.IsWithin(elements[index].BodyIndex, bodyIndex))
            {
                var element = elements[index];
                if (element.BodyIndex == bodyIndex)
                {
                    EmitStatement(statements, index, bodyIds, ref context);
                    index++;
                }
                else
                {
                    var child = statements.ChildOnPath(element.BodyIndex, bodyIndex);
                    EmitBody(statements, child, bodyIds, ref index, ref context);
                }

                // The sync after a statement goes where the next statement is: after the scopes that end here, before
                // the ones that start with it
                if (index < elements.Length &&
                    statements.IsWithin(elements[index].BodyIndex, bodyIndex) &&
                    SyncGridBody.IsSyncPoint(elements, index - 1, model))
                {
                    context.Emitter.Writer.AppendLine(GridSync);
                }
            }
        }
    }

    // <type> <name>; for each declarator of the guarded declarations of the body and each out variable that a guarded
    // statement declares into the body, which the guards only assign
    private static void DeclareSplitLocals(SyncGridStatements statements, int bodyIndex, int first, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        for (var i = first;
             i < statements.Elements.Length && statements.IsWithin(statements.Elements[i].BodyIndex, bodyIndex);
             i++)
        {
            var element = statements.Elements[i];
            if (element.BodyIndex != bodyIndex || statements.GetGuardBodies(i, model).IsEmpty)
            {
                continue;
            }

            if (IsSplitDeclaration(statements, i, model, out var declaration))
            {
                foreach (var declarator in declaration.Declaration.Variables)
                {
                    DeclareLocal(declarator, declarator.Identifier, ref context);
                }
            }

            var outVariables = ImmutableArray.CreateBuilder<DeclarationExpressionSyntax>();
            SyncGridBody.AddMovedOutVariables(element.Node, outVariables);
            foreach (var variable in outVariables)
            {
                // Only single variable designations are collected (SyncGridBody.AddMovedOutVariables)
                var designation = (SingleVariableDesignationSyntax)variable.Designation;
                DeclareLocal(designation, designation.Identifier, ref context);
            }
        }
    }

    // <type> <name>;
    private static void DeclareLocal(SyntaxNode declaration, SyntaxToken identifier, ref TranspileContext context)
    {
        if (context.Query.SemanticModel.GetDeclaredSymbol<ILocalSymbol>(declaration) is not { } local)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, declaration,
                GpuCodeConstraints.SyncGridUnsplittableDeclaration);
            return;
        }

        StructTranspiler.RegisterUsedStructIfApplicable(local.Type, ref context);
        context.Emitter.Dialect.AppendType(ref context, local.Type);
        context.Emitter.Writer.Append(' ');
        CppNameResolver.AppendDeclaredName(declaration, identifier, ref context);
        context.Emitter.Writer.AppendLine(";");
    }

    // One statement: in the guard of the bodies that returned before it, its returns rewritten, the label after it when it
    // returns; a split declaration only assigns its initializers
    private static void EmitStatement(SyncGridStatements statements, int index, int[] bodyIds, ref TranspileContext context)
    {
        var element = statements.Elements[index];
        var model = context.Query.SemanticModel;
        var guards = statements.GetGuardBodies(index, model);
        var bodyId = bodyIds[element.BodyIndex];
        var label = $"{GSharpInternalNames.Lowering.SyncGridNextLabelPrefix}{bodyId}_{index}";
        if (guards.IsEmpty)
        {
            EmitContent(statements, index, label, bodyId, guarded: false, ref context);
        }
        else
        {
            context.Emitter.Writer.Append("if");
            using (context.Emitter.Writer.Parentheses())
            {
                for (var g = 0; g < guards.Length; g++)
                {
                    context.Emitter.Writer.AppendIf(g > 0, "&&").Append($"!{DoneFlagName(bodyIds[guards[g]])}");
                }
            }

            context.Emitter.Writer.AppendLine();
            using (context.Emitter.Writer.Scope())
            {
                EmitContent(statements, index, label, bodyId, guarded: true, ref context);
            }
        }

        if (element.HasBodyReturn)
        {
            context.Emitter.Writer.AppendLine($"{label}:;");
        }
    }

    private static void EmitContent(
        SyncGridStatements statements,
        int index,
        string label,
        int bodyId,
        bool guarded,
        ref TranspileContext context)
    {
        var element = statements.Elements[index];
        var model = context.Query.SemanticModel;
        if (IsSplitDeclaration(statements, index, model, out var declaration))
        {
            // <name> = <initializer>; for each declarator with one
            foreach (var declarator in declaration.Declaration.Variables)
            {
                if (declarator.Initializer == null)
                {
                    continue;
                }

                CppNameResolver.AppendDeclaredName(declarator, declarator.Identifier, ref context);
                DeclarationTranspilerHelper.WriteDeclaratorInitializer(declarator, ref context);
                context.Emitter.Writer.AppendLine(";");
            }

            return;
        }

        var node = element.Node;
        var outVariables = ImmutableArray.CreateBuilder<DeclarationExpressionSyntax>();
        if (guarded)
        {
            SyncGridBody.AddMovedOutVariables(node, outVariables);
        }

        if (node is StatementSyntax statement && (element.HasBodyReturn || outVariables.Count > 0))
        {
            // The rewritten statement is outside the compilation; its annotations map it back to the user's source
            var rewritten = LoweringAnnotations.AnnotateOriginalSpans(statement, model.Compilation);
            if (element.HasBodyReturn)
            {
                rewritten = SyncGridBody.RewriteReturns(rewritten, DoneFlagName(bodyId), label);
            }

            // out var <name> -> out <name>, declared at the top of the body (DeclareSplitLocals)
            var moved = ImmutableArray.CreateBuilder<DeclarationExpressionSyntax>();
            if (guarded)
            {
                SyncGridBody.AddMovedOutVariables(rewritten, moved);
            }

            node = SyncGridBody.RewriteOutVariables(rewritten, moved);
        }

        context.Visitor.Visit(node);
        // The expression of an expression-bodied lambda is a statement of the body
        context.Emitter.Writer.AppendIf(node is ExpressionSyntax, ";").AppendLine();
    }

    // A guarded local declaration that is declared at the top of its body and assigned in the guard
    private static bool IsSplitDeclaration(
        SyncGridStatements statements,
        int index,
        SemanticModel model,
        [NotNullWhen(true)] out LocalDeclarationStatementSyntax? declaration)
    {
        declaration = statements.Elements[index].Node as LocalDeclarationStatementSyntax;
        return declaration != null &&
               SyncGridBody.CanSplit(declaration, model) &&
               !statements.GetGuardBodies(index, model).IsEmpty;
    }

    private static string DoneFlagName(int bodyId) => $"{GSharpInternalNames.Lowering.SyncGridDonePrefix}{bodyId}";
}

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Locals;

/// <summary>
/// Finds the expression variables and the local functions that a switch section declares and another section uses
/// (#842), the other half of <see cref="SwitchSectionLocals"/>.
/// </summary>
/// <remarks>
/// [WHY] C# gives the switch block one scope for the locals of its sections, and the expression variables of an
/// expression statement (<c>out var v</c>, <c>var (a, b) = ...</c>) and the local functions of a section too. C++ braces
/// each section, so the block around the switch declares them: a variable as <c>&lt;type&gt; &lt;name&gt;;</c> (the
/// prelude of its statement then writes nothing), a local function as its lambda, in front of the switch. A local that
/// a shared local function uses, and a local function that a shared one calls, are shared too, so that the lambda
/// captures names that are declared before it. A shared local function is written as a plain lambda in the order of
/// the source; one that is recursive or that is used above its declaration is not ordered here.
/// </remarks>
internal static class SwitchSectionSharedNames
{
    /// <summary>Whether the block around the switch declares the expression variable <paramref name="designation"/>.</summary>
    public static bool IsDeclaredBeforeSwitch(SingleVariableDesignationSyntax designation, SemanticModel model) =>
        TryGetSection(designation, out var section) &&
        IsShared(designation, section, model) &&
        model.GetDeclaredSymbol<ILocalSymbol>(designation) is { } local &&
        SwitchSectionLocals.CanDeclareLocal(local);

    /// <summary>Whether the block around the switch declares the local function <paramref name="function"/>.</summary>
    public static bool IsSharedFunction(LocalFunctionStatementSyntax function, SemanticModel model) =>
        function.Parent is SwitchSectionSyntax { Parent: SwitchStatementSyntax switchStatement } &&
        SharedFunctions(switchStatement, model).Contains(function);

    /// <summary>Whether a shared local function of the switch uses the local <paramref name="local"/>.</summary>
    public static bool IsReferencedBySharedFunction(SwitchSectionLocals.LocalIdentity local,
        SwitchStatementSyntax switchStatement, SemanticModel model)
    {
        foreach (var function in SharedFunctions(switchStatement, model))
        {
            if (References(function, local))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reports the shared expression variables that cannot be declared before the switch, and returns whether the
    /// block around the switch has an expression variable or a local function to declare.
    /// </summary>
    public static bool ReportAndFind(SwitchStatementSyntax switchStatement, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        var found = SharedFunctions(switchStatement, model).Count > 0;
        foreach (var (section, designation) in ExpressionVariables(switchStatement))
        {
            if (!IsShared(designation, section, model))
            {
                continue;
            }

            if (IsDeclaredBeforeSwitch(designation, model))
            {
                found = true;
            }
            else
            {
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, designation,
                    GpuCodeConstraints.SwitchSectionLocalNotDeclarable);
            }
        }

        return found;
    }

    /// <summary>Writes the shared expression variables, then the shared local functions, in the order of the source.</summary>
    public static void WriteDeclarations(SwitchStatementSyntax switchStatement, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        foreach (var (_, designation) in ExpressionVariables(switchStatement))
        {
            if (!IsDeclaredBeforeSwitch(designation, model) ||
                model.GetDeclaredSymbol<ILocalSymbol>(designation) is not { } local)
            {
                continue;
            }

            // <type> <name>;
            context.Emitter.Dialect.AppendType(ref context, local.Type);
            context.Emitter.Writer.Append(' ');
            CppNameResolver.AppendDeclaredName(designation, designation.Identifier, ref context);
            context.Emitter.Writer.AppendLine(";");
        }

        var shared = SharedFunctions(switchStatement, model);
        foreach (var section in switchStatement.Sections)
        {
            foreach (var statement in section.Statements)
            {
                if (statement is LocalFunctionStatementSyntax function && shared.Contains(function))
                {
                    context.Visitor.Visit(function);
                    context.Emitter.Writer.AppendLine();
                }
            }
        }
    }

    // The section of a statement that leaks its expression variables into the switch block: an expression statement
    // or a local declaration directly in the section, with no block or lambda between it and the variable
    private static bool TryGetSection(SingleVariableDesignationSyntax designation, out SwitchSectionSyntax section)
    {
        section = null!; // Assigned on every path that returns true
        for (SyntaxNode? node = designation.Parent; node != null; node = node.Parent)
        {
            if (node is AnonymousFunctionExpressionSyntax or BlockSyntax)
            {
                return false;
            }

            if (node is not StatementSyntax statement)
            {
                continue;
            }

            if (statement is not (ExpressionStatementSyntax or LocalDeclarationStatementSyntax) ||
                statement.Parent is not SwitchSectionSyntax { Parent: SwitchStatementSyntax } found)
            {
                return false;
            }

            section = found;
            return true;
        }

        return false;
    }

    private static bool IsShared(SingleVariableDesignationSyntax designation, SwitchSectionSyntax section,
        SemanticModel model)
    {
        if (designation.Parent is not DeclarationExpressionSyntax &&
            !StatementPrelude.IsDeconstructionVariable(designation))
        {
            return false;
        }

        var identity = SwitchSectionLocals.LocalIdentity.Of(designation, designation.Identifier.ValueText, model);
        if (section.Parent is not SwitchStatementSyntax switchStatement)
        {
            return false;
        }

        foreach (var other in switchStatement.Sections)
        {
            if (other != section && References(other, identity))
            {
                return true;
            }
        }

        return IsReferencedBySharedFunction(identity, switchStatement, model);
    }

    private static bool References(SyntaxNode scope, SwitchSectionLocals.LocalIdentity identity)
    {
        foreach (var node in scope.DescendantNodes())
        {
            if (identity.IsReferencedBy(node))
            {
                return true;
            }
        }

        return false;
    }

    // The local functions that another section uses, and the ones that those use
    private static HashSet<LocalFunctionStatementSyntax> SharedFunctions(SwitchStatementSyntax switchStatement,
        SemanticModel model)
    {
        HashSet<LocalFunctionStatementSyntax> shared = [];
        foreach (var section in switchStatement.Sections)
        {
            foreach (var statement in section.Statements)
            {
                if (statement is LocalFunctionStatementSyntax function &&
                    IsUsedByOtherSection(function, section, switchStatement, model))
                {
                    shared.Add(function);
                }
            }
        }

        var grew = shared.Count > 0;
        while (grew)
        {
            grew = false;
            foreach (var section in switchStatement.Sections)
            {
                foreach (var statement in section.Statements)
                {
                    if (statement is LocalFunctionStatementSyntax function &&
                        !shared.Contains(function) &&
                        IsUsedBySharedFunction(function, shared, model))
                    {
                        shared.Add(function);
                        grew = true;
                    }
                }
            }
        }

        return shared;
    }

    private static bool IsUsedBySharedFunction(LocalFunctionStatementSyntax function,
        HashSet<LocalFunctionStatementSyntax> shared, SemanticModel model)
    {
        var identity = SwitchSectionLocals.LocalIdentity.Of(function, function.Identifier.ValueText, model);
        foreach (var user in shared)
        {
            if (References(user, identity))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUsedByOtherSection(LocalFunctionStatementSyntax function, SwitchSectionSyntax section,
        SwitchStatementSyntax switchStatement, SemanticModel model)
    {
        var identity = SwitchSectionLocals.LocalIdentity.Of(function, function.Identifier.ValueText, model);
        foreach (var other in switchStatement.Sections)
        {
            if (other != section && References(other, identity))
            {
                return true;
            }
        }

        return false;
    }

    // Each expression variable of an expression statement or a local declaration directly in a section
    private static List<(SwitchSectionSyntax Section, SingleVariableDesignationSyntax Designation)>
        ExpressionVariables(SwitchStatementSyntax switchStatement)
    {
        List<(SwitchSectionSyntax, SingleVariableDesignationSyntax)> found = [];
        foreach (var section in switchStatement.Sections)
        {
            foreach (var statement in section.Statements)
            {
                if (statement is not (ExpressionStatementSyntax or LocalDeclarationStatementSyntax))
                {
                    continue;
                }

                foreach (var node in statement.DescendantNodes(static n => n is not AnonymousFunctionExpressionSyntax))
                {
                    if (node is SingleVariableDesignationSyntax designation &&
                        (designation.Parent is DeclarationExpressionSyntax ||
                         StatementPrelude.IsDeconstructionVariable(designation)))
                    {
                        found.Add((section, designation));
                    }
                }
            }
        }

        return found;
    }
}

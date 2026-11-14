using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Locals;

/// <summary>
/// Finds the expression variables and the local functions that a switch section declares and another section uses
/// (#842), the other half of <see cref="SwitchSectionLocals"/>.
/// </summary>
/// <remarks>
/// [WHY] C# gives the switch block one scope for the locals of its sections, and the expression variables of an
/// expression statement (<c>out var v</c>, <c>var (a, b) = ...</c>) and the local functions of a section too. C++
/// braces each section, so the block around the switch declares them: a variable as
/// <c>&lt;type&gt; &lt;name&gt;;</c> (the prelude of its statement then writes nothing), a local function as its
/// lambda, in front of the switch. A local that a shared local function uses, and a local function that a shared one
/// calls, are shared too, so that the lambda captures names that are declared before it. What a switch shares is
/// decided once, by <see cref="SharedNames"/>; the writers only follow it. A shared local function is written as a
/// plain lambda in the order of the source.
/// </remarks>
internal static class SwitchSectionSharedNames
{
    private static readonly ConditionalWeakTable<SwitchStatementSyntax, SharedNames> s_plans = new();

    /// <summary>What the block around a switch declares for its sections.</summary>
    /// <param name="Variables">The expression variables that another section uses, in the order of the source.</param>
    /// <param name="Unsupported">
    /// The expression variables that another section uses and that cannot be declared first.
    /// </param>
    /// <param name="Functions">The local functions that another section calls, or that a shared one calls.</param>
    internal sealed record SharedNames(
        List<(SingleVariableDesignationSyntax Designation, ILocalSymbol Local)> Variables,
        List<SingleVariableDesignationSyntax> Unsupported,
        HashSet<LocalFunctionStatementSyntax> Functions);

    /// <summary>
    /// Whether the block around the switch declares the expression variable <paramref name="designation"/>.
    /// </summary>
    public static bool IsDeclaredBeforeSwitch(SingleVariableDesignationSyntax designation, SemanticModel model) =>
        TryGetSection(designation, out var section) &&
        section.Parent is SwitchStatementSyntax switchStatement &&
        Find(switchStatement, model).Variables
            .AsStackEnumerable()
            .Any(designation, static (variable, d) => variable.Designation == d);

    /// <summary>
    /// Whether the block around the switch declares the local function <paramref name="function"/>.
    /// </summary>
    public static bool IsSharedFunction(LocalFunctionStatementSyntax function, SemanticModel model) =>
        function.Parent is SwitchSectionSyntax { Parent: SwitchStatementSyntax switchStatement } &&
        Find(switchStatement, model).Functions.Contains(function);

    /// <summary>Whether a shared local function of the switch uses the local <paramref name="local"/>.</summary>
    public static bool IsReferencedBySharedFunction(SwitchSectionLocals.LocalIdentity local,
        SwitchStatementSyntax switchStatement, SemanticModel model) =>
        Find(switchStatement, model).Functions
            .AsStackEnumerable()
            .Any(local, static (function, l) => References(function, l));

    /// <summary>
    /// Reports the shared expression variables that cannot be declared before the switch, and returns whether the
    /// block around the switch has an expression variable or a local function to declare.
    /// </summary>
    public static bool ReportAndFind(SwitchStatementSyntax switchStatement, ref TranspileContext context)
    {
        var shared = Find(switchStatement, context.Query.SemanticModel);
        foreach (var designation in shared.Unsupported)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, designation,
                GpuCodeConstraints.SwitchSectionLocalNotDeclarable);
        }

        return shared.Variables.Count > 0 || shared.Functions.Count > 0;
    }

    /// <summary>
    /// Writes the shared expression variables, then the shared local functions, in the order of the source.
    /// </summary>
    public static void WriteDeclarations(SwitchStatementSyntax switchStatement, ref TranspileContext context)
    {
        var shared = Find(switchStatement, context.Query.SemanticModel);
        foreach (var (designation, local) in shared.Variables)
        {
            // <type> <name>;
            context.Emitter.Dialect.AppendType(ref context, local.Type);
            context.Emitter.Writer.Append(' ');
            CppNameResolver.AppendDeclaredName(designation, designation.Identifier, ref context);
            context.Emitter.Writer.AppendLine(";");
        }

        foreach (var function in AllFunctions(switchStatement))
        {
            if (shared.Functions.Contains(function))
            {
                context.Visitor.Visit(function);
                context.Emitter.Writer.AppendLine();
            }
        }
    }

    // What the switch shares: found once per switch and kept with its syntax node
    private static SharedNames Find(SwitchStatementSyntax switchStatement, SemanticModel model)
    {
        return s_plans.GetValue(switchStatement, node => Build(node, model));
    }

    private static SharedNames Build(SwitchStatementSyntax switchStatement, SemanticModel model)
    {
        var functions = FindFunctions(switchStatement, model);
        List<(SingleVariableDesignationSyntax, ILocalSymbol)> variables = [];
        List<SingleVariableDesignationSyntax> unsupported = [];
        foreach (var (section, designation) in ExpressionVariables(switchStatement))
        {
            var identity = SwitchSectionLocals.LocalIdentity.Of(designation, designation.Identifier.ValueText, model);
            if (!IsReferencedByOtherSection(switchStatement, section, identity) &&
                !functions.AsStackEnumerable().Any(identity, static (function, l) => References(function, l)))
            {
                continue;
            }

            if (model.GetDeclaredSymbol<ILocalSymbol>(designation) is { } local &&
                SwitchSectionLocals.CanDeclareLocal(local, initializer: null))
            {
                variables.Add((designation, local));
            }
            else
            {
                unsupported.Add(designation);
            }
        }

        return new SharedNames(variables, unsupported, functions);
    }

    // The section of a statement that leaks its expression variables into the switch block: an expression statement
    // or a local declaration directly in the section, with no block or lambda between it and the variable
    private static bool TryGetSection(SingleVariableDesignationSyntax designation,
        [NotNullWhen(true)] out SwitchSectionSyntax? section)
    {
        section = null;
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

    // Another section names the declaration: C# forbids two locals of one name whose scopes overlap, so a matching
    // name is a reference to it unless it binds to something else (a member name, a lambda parameter)
    private static bool IsReferencedByOtherSection(SwitchStatementSyntax switchStatement, SwitchSectionSyntax section,
        SwitchSectionLocals.LocalIdentity identity) =>
        switchStatement.Sections
            .AsStackEnumerable()
            .Where(section, static (other, own) => other != own)
            .Any(identity, static (other, id) => References(other, id));

    private static bool References(SyntaxNode scope, SwitchSectionLocals.LocalIdentity identity) =>
        scope
            .DescendantNodes()
            .AsStackEnumerable()
            .Any(identity, static (node, id) => id.IsReferencedBy(node));

    // The local functions that another section calls, and the ones that those call
    private static HashSet<LocalFunctionStatementSyntax> FindFunctions(SwitchStatementSyntax switchStatement,
        SemanticModel model)
    {
        HashSet<LocalFunctionStatementSyntax> shared = [];
        foreach (var function in AllFunctions(switchStatement))
        {
            var identity = SwitchSectionLocals.LocalIdentity.Of(function, function.Identifier.ValueText, model);
            if (function.Parent is SwitchSectionSyntax section &&
                IsReferencedByOtherSection(switchStatement, section, identity))
            {
                shared.Add(function);
            }
        }

        var grew = shared.Count > 0;
        while (grew)
        {
            grew = false;
            foreach (var function in AllFunctions(switchStatement))
            {
                var identity = SwitchSectionLocals.LocalIdentity.Of(function, function.Identifier.ValueText, model);
                if (!shared.Contains(function) &&
                    shared.AsStackEnumerable().Any(identity, static (user, id) => References(user, id)))
                {
                    shared.Add(function);
                    grew = true;
                }
            }
        }

        return shared;
    }

    // The local functions declared directly in a section, in the order of the source
    private static List<LocalFunctionStatementSyntax> AllFunctions(SwitchStatementSyntax switchStatement)
    {
        List<LocalFunctionStatementSyntax> functions = [];
        switchStatement.Sections
            .AsStackEnumerable()
            .SelectMany(static section => section.Statements.AsStackEnumerable())
            .OfType<LocalFunctionStatementSyntax>()
            .AddTo(functions);
        return functions;
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
                         StatementPrelude.IsDeconstructionVariable(designation)) &&
                        TryGetSection(designation, out var own) &&
                        own == section)
                    {
                        found.Add((section, designation));
                    }
                }
            }
        }

        return found;
    }
}

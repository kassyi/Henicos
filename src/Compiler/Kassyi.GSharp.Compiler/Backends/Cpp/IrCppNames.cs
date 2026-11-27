using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// The plan of the C++ names of the variables of an IR body for the route it was lowered for, made before a writer
/// spells anything: the writer reads a name, it never makes one.
/// </summary>
internal sealed class IrCppNames
{
    private readonly Dictionary<IrVariable, string> _names;

    private IrCppNames(IrBodyRoute route, Dictionary<IrVariable, string> names)
    {
        Route = route;
        _names = names;
    }

    /// <summary>The backend route this naming plan was created for.</summary>
    public IrBodyRoute Route { get; }

    /// <summary>The C++ name of <paramref name="variable"/>.</summary>
    /// <exception cref="InvalidOperationException">The plan has no name for the variable.</exception>
    public string this[IrVariable variable] =>
        _names.TryGetValue(variable, out var cpp)
            ? cpp
            : throw new InvalidOperationException("The plan of names has no name for a variable outside the body.");

    /// <summary>The C++ names of every variable of <paramref name="body"/>, on its route.</summary>
    public static IrCppNames Create(IrBody body, IIrCppNameSource userNames) =>
        Create(body, userNames, IrCppTemporaryNamers.Namers);

    /// <summary>The same plan with <paramref name="namers"/> for the temporaries.</summary>
    internal static IrCppNames Create(IrBody body, IIrCppNameSource userNames, IIrCppTemporaryNamer[] namers)
    {
        var method = body.Sequenced;
        var state = new IrCppNamingState(method.Body);
        var names = new Dictionary<IrVariable, string>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in method.Parameters)
        {
            Add(parameter, body.Profile.Dialect, userNames, state, namers, names, taken);
        }

        foreach (var node in IrNodeChildren.DescendantsAndSelf(method.Body))
        {
            foreach (var variable in VariablesOf(node))
            {
                Add(variable, body.Profile.Dialect, userNames, state, namers, names, taken);
            }
        }

        return new IrCppNames(body.Profile.Route, names);
    }

    /// <summary>The C++ name of <paramref name="variable"/>, when the plan has one.</summary>
    public bool TryGet(IrVariable variable, out string cpp)
    {
        if (_names.TryGetValue(variable, out var name))
        {
            cpp = name;
            return true;
        }

        cpp = "";
        return false;
    }

    // The variables a node declares or reads
    private static ImmutableArray<IrVariable> VariablesOf(IrNode node) => node switch
    {
        IrBlock block => block.Locals,
        IrWhile loop => loop.Locals,
        IrSequence sequence => sequence.Locals,
        IrLocalDeclaration declaration => [declaration.Variable],
        IrVariableReference reference => [reference.Variable],
        _ => ImmutableArray<IrVariable>.Empty
    };

    private static void Add(IrVariable variable, ITranspilerDialect dialect, IIrCppNameSource userNames,
        IrCppNamingState state, IIrCppTemporaryNamer[] namers, Dictionary<IrVariable, string> names,
        HashSet<string> taken)
    {
        // The variable of this has no name of its own: the C++ writer does not spell it
        if (variable.Kind == IrVariableKind.This || names.ContainsKey(variable))
        {
            return;
        }

        var cpp = variable.Kind == IrVariableKind.Temporary
            ? NameTemporary(variable, state, namers)
            : userNames.LocalName(
                variable.Symbol ?? throw new InvalidOperationException("A user variable has a symbol."),
                variable.Name, dialect, variable.Region);
        if (!taken.Add(cpp))
        {
            throw new InvalidOperationException($"Two variables of the body take the C++ name '{cpp}'.");
        }

        names.Add(variable, cpp);
    }

    private static string NameTemporary(IrVariable variable, IrCppNamingState state, IIrCppTemporaryNamer[] namers)
    {
        foreach (var namer in namers)
        {
            if (namer.TryName(variable, state, out var cpp))
            {
                return cpp;
            }
        }

        throw new InvalidOperationException(
            "No namer takes a temporary of the body: a body of the IR route holds sequence temporaries only.");
    }
}

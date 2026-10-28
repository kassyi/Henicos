using Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.StackArrays;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports a use that lets a stack array outlive the C++ scope that holds it (GSH0091). G# keeps a local array
/// (<c>int[] a = new int[4]</c>, an array initializer, a collection expression), the stack array of a span
/// (<c>Span&lt;int&gt; s = stackalloc int[4]</c>) and the storage of a <c>stackalloc</c> in a C++ array of the innermost
/// scope around it, where C# keeps the array on the heap or until the method returns.
/// </summary>
/// <remarks>
/// <para>
/// Each local (and each value parameter of a local function or lambda the method calls) gets the set of storages it
/// may point into, propagated from declarations, assignments, the arguments of those calls and the <c>ref</c> /
/// <c>out</c> arguments of every call until nothing changes (the safe-context of a local in Roslyn's
/// <c>RefSafetyAnalysis</c>, with the scopes of the emitted C++ in place of C#'s): <see cref="StackArrayEscapeAnalysis"/>.
/// A value refers to a storage only through the positions that carry a reference (<see cref="StorageGatherer"/>).
/// </para>
/// <para>
/// Reported: reassigning a stack array local or passing it by <c>ref</c> / <c>out</c>; returning a value that refers to a
/// stack array (<c>return ref a[0]</c> included); storing it where it outlives the storage (a local of an outer scope, a
/// parameter, a field or element of something that is not such a local, a <c>ref</c> local, <c>*p</c>); and a call whose
/// <c>ref</c> / <c>out</c> argument outlives a storage that another argument refers to, which the callee may store there
/// (the "method arguments must match" rule of C#).
/// </para>
/// <para>
/// The scopes are those of the emitted C++ (<see cref="CppScopes"/>). The emitted C++ flattens a nested block, which only
/// makes a storage live longer, so a check on the C# blocks stays sound.
/// </para>
/// </remarks>
internal sealed class StackArrayEscapeRule : ISyntaxValidationRule<MethodDeclarationSyntax>
{
    public static readonly StackArrayEscapeRule Instance = new();

    /// <summary>The name of the storage of a <c>stackalloc</c> in the message.</summary>
    public const string StackAllocName = "stackalloc";

    /// <summary>The name of the storage of a collection expression in the message.</summary>
    public const string CollectionExpressionName = "collection expression";

    // The uses, as the second argument of the message ("The stack array '{0}' cannot be {1}")
    public const string Reassigned = "reassigned";
    public const string PassedByReference = "passed by ref or out";
    public const string Returned = "returned";
    public const string StoredOutside = "stored in a field, an element, a parameter or a ref local";
    public const string AssignedToOuterVariable = "assigned to a variable declared outside its scope";
    public const string PassedWithLongerLivedReference = "passed to a call with a ref or out argument that outlives it";

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.StackArrayEscapes);

    private StackArrayEscapeRule()
    {
    }

    public void Analyze(MethodDeclarationSyntax node, ref ValidationContext context)
    {
        if (node.GetBodyNode() is not { } body)
        {
            return;
        }

        foreach (var finding in new StackArrayEscapeAnalysis(context.SemanticModel).Run(body))
        {
            context.ReportDiagnostic(GSharpDiagnostics.StackArrayEscapes, finding.Node, finding.Name, finding.Reason);
        }
    }
}

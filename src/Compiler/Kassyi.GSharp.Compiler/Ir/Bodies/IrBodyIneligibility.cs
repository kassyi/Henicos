namespace Kassyi.GSharp.Compiler.Ir.Bodies;

/// <summary>
/// Why a method takes no IR route to a backend writer; the values are in the order they are checked, and the first that
/// applies is the reason.
/// </summary>
internal enum IrBodyIneligibility
{
    /// <summary>The method takes the IR route.</summary>
    None,

    /// <summary>The builder made no IR of the method.</summary>
    IrNotBuilt,

    /// <summary>The body calls the mutant switch of Stryker.NET, which the syntax route writes as it is.</summary>
    StrykerMutant,

    /// <summary>
    /// The body calls a barrier, a warp operation or a suspending device function that the route's split policy
    /// excludes.
    /// </summary>
    SplitCall,

    /// <summary>The body holds a construct the builder keeps opaque.</summary>
    OpaqueNode,

    /// <summary>The body calls a method whose calls C# removes (a conditional method, a partial method).</summary>
    RemovedCall,

    /// <summary>The body calls a macro of <c>Gpu</c>, which the syntax route expands.</summary>
    Macro,

    /// <summary>The body declares a local of a shared memory type.</summary>
    SharedMemoryLocal,

    /// <summary>The writer of the route cannot spell a node of the body.</summary>
    UnsupportedForm
}

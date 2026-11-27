namespace Kassyi.GSharp.Compiler.Ir.Bodies;

/// <summary>A stage of the IR body pipeline, as a benchmark measures it.</summary>
internal enum IrBodyStage
{
    /// <summary>Building the IR from the operations of the method.</summary>
    Build,

    /// <summary>Classifying the calls, effects and traps.</summary>
    Classify,

    /// <summary>The walk that collects the facts of a body.</summary>
    Scan,

    /// <summary>The decision pass for the route.</summary>
    Decide,

    /// <summary>The sequence pass for the route.</summary>
    Sequence,

    /// <summary>The check that the writer of the route spells every node.</summary>
    Support,

    /// <summary>Spelling the body with the writer.</summary>
    Write
}

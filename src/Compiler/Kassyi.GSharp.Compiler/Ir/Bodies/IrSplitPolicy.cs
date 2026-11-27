namespace Kassyi.GSharp.Compiler.Ir.Bodies;

/// <summary>How a backend route treats split calls found during the IR body scan.</summary>
internal enum IrSplitPolicy
{
    /// <summary>Any call whose split classification is split makes the body ineligible.</summary>
    ExcludeEverySplit
}

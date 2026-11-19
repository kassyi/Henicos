namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>Plan for a simple assignment to a one-dimensional array element (#1561).</summary>
/// <param name="BindsArrayByReference">Whether the receiver is a stack array bound by reference.</param>
/// <param name="Site">The array-null error site when the receiver is not proven non-null.</param>
internal readonly record struct IrElementStore(bool BindsArrayByReference, IrErrorSite? Site);

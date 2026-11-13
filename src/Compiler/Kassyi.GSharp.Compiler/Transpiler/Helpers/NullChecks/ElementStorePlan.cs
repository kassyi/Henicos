namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;

/// <summary>
/// How a simple assignment stores into an element of an array whose array and index were evaluated into temporaries
/// before the right side (<see cref="ArrayNullCheck.PlanElementStore"/>): whether the array temporary binds the array
/// by reference, and the null check the store makes, if any.
/// </summary>
/// <param name="BindsArrayByReference">
/// The array is a stack array (a C array, which a copy would decay to a pointer): its temporary is <c>auto&amp;&amp;</c>.
/// </param>
/// <param name="Site">
/// The place of the null check of the store (<c>__gsharp_array_at</c>), or <see langword="null"/> when the array cannot
/// be null and the store is a plain <c>[]</c>.
/// </param>
internal readonly record struct ElementStorePlan(bool BindsArrayByReference, NullCheckSite? Site);

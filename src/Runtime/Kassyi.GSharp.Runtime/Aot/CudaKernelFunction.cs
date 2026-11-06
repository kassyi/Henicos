using System.ComponentModel;

namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// The function (<c>CUfunction</c>) of one kernel of an AOT fatbin module, resolved in the CUDA context that is current
/// for the launch.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> A module and its functions belong to the context they were loaded in. A handle kept from the first
/// launch is invalid in another context, and after that context is destroyed or the module unloaded, the driver crashes
/// in <c>cuLaunchKernel</c> instead of returning an error. Each launcher therefore resolves its function through
/// <see cref="GpuKernelLoader"/>, which loads the module once per context.
/// </para>
/// <para>
/// The last function resolved is kept with the identity of its context, so a launch in the same context compares two
/// numbers and makes no driver call or dictionary lookup.
/// </para>
/// </remarks>
/// <param name="kernelName">The entry point of the kernel in the module.</param>
/// <param name="fatbin">Returns the AOT fatbin module that holds the kernel.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CudaKernelFunction(string kernelName, Func<byte[]> fatbin)
{
    private Resolved? _last;

    /// <summary>Returns the function in the context current for a launch on this thread, loading its module there if needed.</summary>
    public IntPtr Resolve() => Resolve([]);

    /// <summary>
    /// Returns the function in the context current for a launch on this thread, loading its module there if needed, and
    /// registers <paramref name="errorSites"/> for the module when it resolves the function.
    /// </summary>
    public IntPtr Resolve(ReadOnlySpan<GSharpErrorSite> errorSites)
    {
        var context = CudaModuleCache.CurrentContextId;
        var last = Volatile.Read(ref _last);
        if (last is not null && last.ContextId == context)
        {
            return last.Function;
        }

        var module = fatbin();
        if (!errorSites.IsEmpty)
        {
            GpuKernelLoader.RegisterErrorSites(module, errorSites);
        }

        var function = GpuKernelLoader.LoadAotKernel(module, kernelName);
        Volatile.Write(ref _last, new Resolved(context, function));
        return function;
    }

    private sealed record Resolved(long ContextId, IntPtr Function);
}

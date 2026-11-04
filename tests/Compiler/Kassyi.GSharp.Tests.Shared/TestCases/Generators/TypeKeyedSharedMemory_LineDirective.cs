namespace Repro;

using Kassyi.GSharp.Core;

public struct LineDirectiveHolder<T> where T : unmanaged
{
    public LineDirectiveHolder(int n)
    {
#line 500 "mapped_file.cs"
        var smem = new SharedMemory<int>(n);
    }
}

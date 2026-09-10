using System;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Core;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CustomLayoutStruct
{
    public byte A;
    public double B;
    public float C;
}

public class COR_01_StructLayoutKernels
{
    [GpuKernel]
    public static void ModifyStructLayoutKernel(CustomLayoutStruct[] data)
    {
        int tid = ThreadIndex.X;
        
        data[tid].A += 10;
        data[tid].B *= 2.0;
        data[tid].C -= 1.0f;
    }
}

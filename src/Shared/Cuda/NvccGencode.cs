namespace Kassyi.GSharp.Cuda;

/// <summary>One nvcc <c>-gencode arch=&lt;Arch&gt;,code=&lt;Code&gt;</c> option.</summary>
internal readonly record struct NvccGencode(string Arch, string Code);

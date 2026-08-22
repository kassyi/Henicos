namespace Kassyi.GSharp.Core;

/// <summary>
/// GPU上でのみ実行可能なコード（G#の糖衣構文や組み込みAPI）を、CPU上で実行しようとした際にスローされる例外です。
/// </summary>
public class GpuOnlyException() : InvalidOperationException("This code can only be executed on the GPU.")
{
    public static void Throw() => throw new GpuOnlyException();

    public static T Throw<T>() => throw new GpuOnlyException();
}

namespace Kassyi.GSharp.Core;

/// <summary>
/// G#トランスパイラ専用のダミー構造体。
/// AST解析時に __shared__ 宣言に変換されます。CPU上では実行できません。
/// </summary>
public readonly ref struct SharedMemory<T> where T : unmanaged
{
    public SharedMemory(int size)
    {
    }

    public T this[int index]
    {
        get => GpuOnlyException.Throw<T>();
        set => GpuOnlyException.Throw();
    }
}

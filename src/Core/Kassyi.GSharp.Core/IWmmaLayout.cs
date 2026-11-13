namespace Kassyi.GSharp.Core;

/// <summary>
/// Marker interface representing the in-memory storage layout of a <see cref="WmmaMatrixA{TShape, T, TLayout}"/> or
/// <see cref="WmmaMatrixB{TShape, T, TLayout}"/> fragment.
/// </summary>
/// <remarks>
/// Layout marker structs carry no runtime data and exist purely as compile-time type tags. They must only be used as
/// the <c>TLayout</c> type argument of <see cref="WmmaMatrixA{TShape, T, TLayout}"/> and
/// <see cref="WmmaMatrixB{TShape, T, TLayout}"/>. Only the layouts declared in this namespace are WMMA layouts; a
/// type of your own that implements this interface is an ordinary type.
/// </remarks>
public interface IWmmaLayout;

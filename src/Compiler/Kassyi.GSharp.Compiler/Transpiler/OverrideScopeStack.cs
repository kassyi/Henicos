namespace Kassyi.GSharp.Compiler.Transpiler;
using System.Collections;

/// <summary>
/// A zero-allocation or minimal-allocation stack for local variable overrides during macro transpilation.
/// </summary>
/// <remarks>
/// An override pushed with a symbol replaces only the references to that symbol: a parameter of a nested lambda or local
/// function that has the same name is another symbol and keeps its own value (#1266).
/// </remarks>
public sealed class OverrideScopeStack : IReadOnlyCollection<(string Name, OverrideValue Value)>
{
    private (string Name, ISymbol? Symbol, OverrideValue Value)[] _items = new (string, ISymbol?, OverrideValue)[16];
    private int _count;

    /// <summary>
    /// Gets the number of override entries currently on the stack.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Pushes an override value into the active scope and returns a token representing the current count. With
    /// <paramref name="symbol"/>, the override applies only to references that resolve to it.
    /// </summary>
    public int Push(string name, OverrideValue value, ISymbol? symbol = null)
    {
        var cookie = _count;
        if (_count == _items.Length)
        {
            Array.Resize(ref _items, _items.Length * 2);
        }

        _items[_count++] = (name, symbol?.OriginalDefinition, value);
        return cookie;
    }

    /// <summary>
    /// Restores the stack back to the specified count cookie, releasing references.
    /// </summary>
    public void Pop(int cookie)
    {
        if (cookie < _count)
        {
            Array.Clear(_items, cookie, _count - cookie);
            _count = cookie;
        }
    }

    /// <summary>
    /// Clears all entries from the stack.
    /// </summary>
    public void Clear()
    {
        if (_count > 0)
        {
            Array.Clear(_items, 0, _count);
            _count = 0;
        }
    }

    /// <summary>
    /// Tries to resolve the override of a reference named <paramref name="name"/> that resolves to
    /// <paramref name="symbol"/>, searching from innermost to outermost scope. An override pushed with a symbol matches
    /// only that symbol; a reference without a symbol matches by name.
    /// </summary>
    public bool TryGet(string name, ISymbol? symbol, out OverrideValue value)
    {
        var original = symbol?.OriginalDefinition;
        for (var i = _count - 1; i >= 0; i--)
        {
            var item = _items[i];
            if (item.Name == name && (item.Symbol is null || original is null || item.Symbol.IsEqualTo(original)))
            {
                value = item.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Returns an enumerator that iterates through the override items.
    /// </summary>
    public IEnumerator<(string Name, OverrideValue Value)> GetEnumerator()
    {
        for (var i = 0; i < _count; i++)
        {
            yield return (_items[i].Name, _items[i].Value);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

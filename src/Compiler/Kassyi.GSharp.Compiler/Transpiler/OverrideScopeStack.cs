namespace Kassyi.GSharp.Compiler.Transpiler;
using System.Collections;

/// <summary>
/// A zero-allocation or minimal-allocation stack for local variable overrides during macro transpilation.
/// </summary>
public sealed class OverrideScopeStack : IReadOnlyCollection<(string Name, OverrideValue Value)>
{
    private (string Name, OverrideValue Value)[] _items = new (string, OverrideValue)[16];
    private int _count;

    /// <summary>
    /// Gets the number of override entries currently on the stack.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Pushes an override value into the active scope and returns a token representing the current count.
    /// </summary>
    public int Push(string name, OverrideValue value)
    {
        var cookie = _count;
        if (_count == _items.Length)
        {
            Array.Resize(ref _items, _items.Length * 2);
        }

        _items[_count++] = (name, value);
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
    /// Tries to resolve a local variable override by name, searching from innermost to outermost scope.
    /// </summary>
    public bool TryGet(string name, out OverrideValue value)
    {
        for (var i = _count - 1; i >= 0; i--)
        {
            if (_items[i].Name == name)
            {
                value = _items[i].Value;
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
            yield return _items[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

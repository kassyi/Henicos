namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>Collects the measurements of one detector run over one syntax tree.</summary>
internal sealed class MeasurementSink
{
    private readonly List<Measurement> _items = [];

    /// <summary>Gets the measurements in the order they were added.</summary>
    public IReadOnlyList<Measurement> Items => _items;

    /// <summary>Adds a measurement.</summary>
    public void Add(Measurement measurement) => _items.Add(measurement);
}

#nullable enable
using System.Linq;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Discovers test traits from <see cref="TestTargetAttribute"/>.
/// Emits both 'TestTarget' and 'Category' keys for seamless CLI filtering and backward compatibility.
/// </summary>
public sealed class TestTargetDiscoverer : ITraitDiscoverer
{
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        var args = traitAttribute.GetConstructorArguments().ToArray();
        if (args.Length == 0 || args[0] == null)
        {
            yield break;
        }

        string? valueStr = args[0] switch
        {
            ExecutionTarget target => target.ToString(),
            int intVal => Enum.GetName(typeof(ExecutionTarget), intVal),
            _ => args[0]?.ToString()
        };

        if (valueStr is null || valueStr.Length == 0)
        {
            yield break;
        }

        yield return new KeyValuePair<string, string>("TestTarget", valueStr);
        yield return new KeyValuePair<string, string>("Category", valueStr);
    }
}

/// <summary>
/// Discovers test traits from <see cref="CategoryAttribute"/>.
/// Emits the 'Category' key.
/// </summary>
public sealed class CategoryDiscoverer : ITraitDiscoverer
{
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        var args = traitAttribute.GetConstructorArguments().ToArray();
        if (args.Length == 0 || args[0] == null)
        {
            yield break;
        }

        string? valueStr = args[0] switch
        {
            TestCategory category => category.ToString(),
            int intVal => Enum.GetName(typeof(TestCategory), intVal),
            _ => args[0]?.ToString()
        };

        if (valueStr is null || valueStr.Length == 0)
        {
            yield break;
        }

        yield return new KeyValuePair<string, string>("Category", valueStr);
    }
}

/// <summary>
/// Discovers test traits from <see cref="ScenarioAttribute"/>.
/// Emits both 'Scenario' and 'Category' keys for seamless CLI filtering and backward compatibility.
/// </summary>
public sealed class ScenarioDiscoverer : ITraitDiscoverer
{
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        var args = traitAttribute.GetConstructorArguments().ToArray();
        if (args.Length == 0 || args[0] == null)
        {
            yield break;
        }

        string? valueStr = args[0] switch
        {
            TestScenario scenario => scenario.ToString(),
            int intVal => Enum.GetName(typeof(TestScenario), intVal),
            _ => args[0]?.ToString()
        };

        if (valueStr is null || valueStr.Length == 0)
        {
            yield break;
        }

        yield return new KeyValuePair<string, string>("Scenario", valueStr);
        yield return new KeyValuePair<string, string>("Category", valueStr);
    }
}

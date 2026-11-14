using System.Collections;
using System.Globalization;
using System.Reflection;
using TelaRec.App.Wpf.Resources;

namespace TelaRec.Tests;

public sealed class ResourceParityTests
{
    [Fact]
    public void Pt_br_and_en_us_have_the_same_keys()
    {
        var ptBr = Keys("pt-BR");
        var enUs = Keys("en-US");

        Assert.NotEmpty(ptBr);
        Assert.Equal(ptBr, enUs);
    }

    [Fact]
    public void Every_strings_property_has_a_resource()
    {
        var keys = Keys("en-US");
        var properties = typeof(Strings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(properties, keys);
    }

    [Fact]
    public void Localized_values_are_not_empty()
    {
        foreach (var culture in new[] { "pt-BR", "en-US" })
        {
            var set = ResourceSet(culture);
            foreach (DictionaryEntry entry in set)
                Assert.False(string.IsNullOrWhiteSpace(entry.Value as string), $"{culture}:{entry.Key} está vazio.");
        }
    }

    private static string[] Keys(string culture)
    {
        var set = ResourceSet(culture);
        return set.Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }

    private static System.Resources.ResourceSet ResourceSet(string culture) =>
        Strings.ResourceManager.GetResourceSet(new CultureInfo(culture), createIfNotExists: true, tryParents: false)
        ?? throw new InvalidOperationException($"Recursos não encontrados para {culture}.");
}

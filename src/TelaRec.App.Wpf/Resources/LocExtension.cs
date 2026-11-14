using System.Windows.Markup;

namespace TelaRec.App.Wpf.Resources;

/// <summary>
/// Markup extension para usar textos localizados diretamente no XAML:
/// <c>Text="{resources:Loc Key=SectionVideo}"</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}

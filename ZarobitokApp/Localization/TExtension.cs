namespace ZarobitokApp.Localization;

/// <summary>XAML: Text="{loc:T 'Український текст'}" — переклад на час створення сторінки.</summary>
[ContentProperty(nameof(Text))]
public sealed class TExtension : IMarkupExtension<string>
{
    public string Text { get; set; } = string.Empty;

    public string ProvideValue(IServiceProvider serviceProvider) => L.T(Text);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}

using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.UI;
using MyDocs.UI.Layout;
using MyDocs.UI.Pages;

namespace MyDocs.Tests;

public sealed class UiTests : BunitContext
{
    public UiTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMyDocsUi();
    }

    [Fact]
    public void HomeShowsTheAreasWithoutThoseOnlyInMyLife()
    {
        var home = Render<Home>();

        Assert.Contains("Willkommen bei myDocs", home.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(home.FindAll("[data-testid=area-4]"));
        Assert.Contains("Versicherungen", home.Markup, StringComparison.Ordinal);
        Assert.Empty(home.FindAll("[data-testid=area-11]"));
        Assert.Empty(home.FindAll("[data-testid=area-12]"));
    }

    [Fact]
    public void LayoutShowsTheLogoInTheAppBar()
    {
        var layout = Render<MainLayout>(p => p.Add(l => l.Body, "<p>Inhalt</p>"));

        var logo = layout.Find("[data-testid=app-logo]");
        Assert.Equal("myDocs", logo.GetAttribute("aria-label"));
        Assert.Contains("Inhalt", layout.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TextsFollowTheLanguage()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");

        var home = Render<Home>();

        Assert.Contains("Welcome to myDocs", home.Markup, StringComparison.Ordinal);
        Assert.Contains("Insurance", home.Markup, StringComparison.Ordinal);
    }
}

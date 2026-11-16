using Bunit;
using FrontEnd.Pages;
using Xunit;

namespace TruckerReward.Tests;

public sealed class SettingsPageTests : TestContext
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LoadsSavedThemeAndSwitchesIt(bool saved)
    {
        JSInterop.Setup<bool>("truckerTheme.getDarkMode").SetResult(saved);
        JSInterop.SetupVoid("truckerTheme.setDarkMode", _ => true).SetVoidResult();
        var page = RenderComponent<Settings>();
        page.WaitForAssertion(() => Assert.False(page.Find("#dark-mode").HasAttribute("disabled")));
        Assert.Equal(saved, page.Find("#dark-mode").HasAttribute("checked"));
        page.Find("#dark-mode").Change(!saved);
        page.WaitForAssertion(() => Assert.Equal(!saved, page.Find("#dark-mode").HasAttribute("checked")));
        Assert.Equal(!saved, JSInterop.Invocations["truckerTheme.setDarkMode"].Single().Arguments[0]);
    }
}

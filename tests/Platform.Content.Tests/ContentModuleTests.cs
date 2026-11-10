using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

namespace Platform.Content.Tests;

public sealed class ContentModuleTests
{
    [Fact]
    public void AddContentModule_ReturnsTheCollection_SoItChainsLikeTheOtherModules()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddContentModule());
    }

    [Fact]
    public void AddContentModule_MakesThisAssembliesControllersPartOfTheApp()
    {
        var services = new ServiceCollection();

        services.AddContentModule();

        var manager = (ApplicationPartManager)services.Single(d => d.ServiceType == typeof(ApplicationPartManager)).ImplementationInstance!;
        Assert.Contains(manager.ApplicationParts.OfType<AssemblyPart>(), part => part.Assembly == typeof(ContentModule).Assembly);
    }

    [Fact]
    public void AddContentModule_CanBeCalledTwice()
    {
        var services = new ServiceCollection();

        services.AddContentModule();
        var exception = Record.Exception(() => services.AddContentModule());

        Assert.Null(exception);
    }
}

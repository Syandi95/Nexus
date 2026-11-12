namespace Nexus.Contracts.Tests;

public sealed class Phase5BaselineContractTests
{
    [Fact]
    public void Module_lifecycle_contract_remains_minimal_and_async()
    {
        var methods = typeof(IModuleLifecycle)
            .GetMethods()
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Collection(
            methods,
            method => AssertMethod(method, "OnNavigatedFromAsync"),
            method => AssertMethod(method, "OnNavigatedToAsync"));
    }

    [Fact]
    public void Job_scope_contract_remains_minimal_and_async_disposable()
    {
        Assert.Contains(typeof(IAsyncDisposable), typeof(IJobScope).GetInterfaces());

        var property = Assert.Single(typeof(IJobScope).GetProperties());
        Assert.Equal("CancellationToken", property.Name);
        Assert.Equal(typeof(CancellationToken), property.PropertyType);
        Assert.Null(property.SetMethod);
        Assert.Empty(typeof(IJobScope).GetMethods().Where(method => !method.IsSpecialName));
    }

    [Fact]
    public void Image_engine_load_contract_uses_an_immutable_descriptor_and_opaque_session_id()
    {
        var method = Assert.Single(
            typeof(IGenericImageEngine)
                .GetMethods()
                .Where(candidate =>
                    candidate.Name == nameof(IGenericImageEngine.LoadModelAsync)));
        var parameters = method.GetParameters();

        Assert.Equal(typeof(ResolvedModelDescriptor), parameters[0].ParameterType);
        Assert.Equal(typeof(EngineSessionOptions), parameters[1].ParameterType);
        Assert.Equal(typeof(Task<string>), method.ReturnType);

        var mutableProperty = typeof(ResolvedModelDescriptor)
            .GetProperties()
            .FirstOrDefault(property =>
                property.SetMethod is not null &&
                !property.SetMethod.ReturnParameter
                    .GetRequiredCustomModifiers()
                    .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)));
        Assert.Null(mutableProperty);
    }

    private static void AssertMethod(
        System.Reflection.MethodInfo method,
        string expectedName)
    {
        Assert.Equal(expectedName, method.Name);
        Assert.Equal(typeof(Task), method.ReturnType);
        Assert.Empty(method.GetParameters());
    }
}

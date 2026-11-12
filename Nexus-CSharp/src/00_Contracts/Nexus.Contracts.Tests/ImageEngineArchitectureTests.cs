using System.Reflection;

namespace Nexus.Contracts.Tests;

public sealed class ImageEngineArchitectureTests
{
    [Fact]
    public void LoadModelAsync_has_only_the_descriptor_based_contract()
    {
        var methods = typeof(IGenericImageEngine)
            .GetMethods()
            .Where(method => method.Name == nameof(IGenericImageEngine.LoadModelAsync))
            .ToArray();

        var method = Assert.Single(methods);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(ResolvedModelDescriptor), parameters[0].ParameterType);
        Assert.Equal(typeof(EngineSessionOptions), parameters[1].ParameterType);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        Assert.DoesNotContain(
            methods,
            candidate => candidate.GetParameters()[0].ParameterType == typeof(string));
    }

    [Fact]
    public void ResolvedModelDescriptor_exposes_only_init_only_setters()
    {
        var mutableProperty = typeof(ResolvedModelDescriptor)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(property =>
            {
                var setter = property.SetMethod;
                return setter is not null &&
                    !setter.ReturnParameter
                        .GetRequiredCustomModifiers()
                        .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));
            });

        Assert.Null(mutableProperty);
    }
}

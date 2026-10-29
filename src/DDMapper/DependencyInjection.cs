using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace AutoMapper.Extensions.Microsoft.DependencyInjection;

/// <summary>Registers DDMapper with a Microsoft DI container.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the configuration and mapper as singletons from a configuration action.</summary>
    public static IServiceCollection AddDDMapper(this IServiceCollection services, Action<IMapperConfigurationExpression> configure)
    {
        var configuration = new MapperConfiguration(configure);
        services.AddSingleton<IConfigurationProvider>(configuration);
        services.AddSingleton(configuration);
        services.AddSingleton<IMapper>(sp => configuration.CreateMapper());
        return services;
    }

    /// <summary>Drop-in alias for <see cref="AddDDMapper(IServiceCollection, Action{IMapperConfigurationExpression})"/>.</summary>
    public static IServiceCollection AddAutoMapper(this IServiceCollection services, Action<IMapperConfigurationExpression> configure) =>
        services.AddDDMapper(configure);

    /// <summary>Registers the configuration and mapper, scanning the given assemblies for profiles.</summary>
    public static IServiceCollection AddDDMapper(this IServiceCollection services, params Assembly[] assemblies)
    {
        return services.AddDDMapper(cfg => cfg.AddProfiles(assemblies));
    }

    /// <summary>Registers the configuration and mapper from explicit profile types.</summary>
    public static IServiceCollection AddDDMapper(this IServiceCollection services, params Type[] profileTypes)
    {
        return services.AddDDMapper(cfg => cfg.AddProfiles(profileTypes));
    }
}

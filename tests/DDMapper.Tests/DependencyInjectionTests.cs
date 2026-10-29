using AutoMapper;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>DI registration (AddDDMapper / AddAutoMapper). The extension namespace
/// is referenced fully-qualified on purpose: this suite's drop-in rule is that
/// test files carry only `using AutoMapper;` plus framework usings, and the DI
/// extensions live in their own namespace by design. No new package is needed:
/// the fake container below implements IServiceCollection / IServiceProvider
/// from the Abstractions package the library already references.</summary>
public class DependencyInjectionTests
{
    public class Src
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public class Dst
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public class TestProfile : Profile
    {
        public TestProfile()
        {
            CreateMap<Src, Dst>();
        }
    }

    private sealed class FakeServices
        : System.Collections.Generic.List<Microsoft.Extensions.DependencyInjection.ServiceDescriptor>,
            Microsoft.Extensions.DependencyInjection.IServiceCollection
    {
    }

    private sealed class FakeProvider : System.IServiceProvider
    {
        private readonly FakeServices _services;
        public FakeProvider(FakeServices services) { _services = services; }

        public object? GetService(System.Type serviceType)
        {
            var descriptor = _services.LastOrDefault(d => d.ServiceType == serviceType);
            if (descriptor == null) return null;
            if (descriptor.ImplementationInstance != null) return descriptor.ImplementationInstance;
            if (descriptor.ImplementationFactory != null) return descriptor.ImplementationFactory(this);
            var type = descriptor.ImplementationType!;
            var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            var args = ctor.GetParameters().Select(p => GetService(p.ParameterType)).ToArray();
            return System.Activator.CreateInstance(type, args);
        }
    }

    [Fact]
    public void AddDDMapperRegistersResolvableMapper()
    {
        var services = new FakeServices();
        AutoMapper.Extensions.Microsoft.DependencyInjection.ServiceCollectionExtensions.AddDDMapper(
            services, cfg => cfg.CreateMap<Src, Dst>());
        var provider = new FakeProvider(services);
        var mapper = (IMapper)provider.GetService(typeof(IMapper))!;
        var dest = mapper.Map<Dst>(new Src { Id = 1, Name = "Ada" });
        Assert.Equal(1, dest.Id);
        Assert.Equal("Ada", dest.Name);
    }

    [Fact]
    public void AddAutoMapperAliasRegistersSameServices()
    {
        var services = new FakeServices();
        AutoMapper.Extensions.Microsoft.DependencyInjection.ServiceCollectionExtensions.AddAutoMapper(
            services, cfg => cfg.CreateMap<Src, Dst>());
        var provider = new FakeProvider(services);
        var mapper = (IMapper)provider.GetService(typeof(IMapper))!;
        Assert.Equal("Bo", mapper.Map<Dst>(new Src { Name = "Bo" }).Name);
        // Configuration and provider resolve to the same registered instance.
        var config = (IConfigurationProvider)provider.GetService(typeof(IConfigurationProvider))!;
        var concrete = (MapperConfiguration)provider.GetService(typeof(MapperConfiguration))!;
        Assert.Same(config, concrete);
    }

    [Fact]
    public void AddDDMapperAssemblyScanFindsProfiles()
    {
        var services = new FakeServices();
        AutoMapper.Extensions.Microsoft.DependencyInjection.ServiceCollectionExtensions.AddDDMapper(
            services, typeof(TestProfile).Assembly);
        var provider = new FakeProvider(services);
        var mapper = (IMapper)provider.GetService(typeof(IMapper))!;
        Assert.Equal("Cy", mapper.Map<Dst>(new Src { Name = "Cy" }).Name);
    }
}

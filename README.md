# DDMapper

An original, from-scratch object-object mapper for .NET 8, written clean-room style.
It is **not** a fork or port of any existing mapper: every line of implementation,
every exception message, and every doc page here was written fresh for this project.
The public API shape deliberately mirrors the familiar mapper idiom (configuration,
profiles, `IMapper`) so existing consumer code using `using AutoMapper;` compiles
unchanged against this library — the swap for a consumer is the package reference
only, not their code.

- **Assembly / package:** `DDMapper` (assembly name and NuGet package id stay `DDMapper`)
- **Namespaces:** `AutoMapper` and its sub-namespaces (see Namespace layout below)
- **Target:** net8.0, nullable enabled, MIT licensed (see `LICENSE`)

## Quick start

```csharp
using AutoMapper;

var config = new MapperConfiguration(cfg =>
{
    cfg.CreateMap<Order, OrderDto>()
        .ForMember(d => d.CustomerName, o => o.MapFrom(s => s.Customer.Name))
        .ForMember(d => d.InternalNotes, o => o.Ignore());
});
var mapper = config.CreateMapper();
var dto = mapper.Map<OrderDto>(order);
```

```csharp
// Profiles group related maps; declare them in the constructor or in Configure().
public class ShopProfile : Profile
{
    public ShopProfile()
    {
        CreateMap<Order, OrderDto>();
        CreateMap<Customer, CustomerDto>();
    }
}

var config = new MapperConfiguration(cfg => cfg.AddProfile<ShopProfile>());
```

```csharp
// Dependency injection (Microsoft DI)
services.AddDDMapper(cfg => cfg.AddProfile<ShopProfile>());
// AddAutoMapper(...) is an alias with the same behaviour.
```

## What DDMapper includes

- Configuration API: `MapperConfiguration`, `MapperConfigurationExpression`,
  `Profile`, `IMapperConfigurationExpression`, `IConfigurationProvider`
- Per-map options: `ForMember` / `ForPath` (`MapFrom`, `Ignore`, `Condition`,
  `PreCondition`, `NullSubstitute`, `UseDestinationValue`, `ConvertUsing`),
  `ConvertUsing` (whole type), `ConstructUsing`, `BeforeMap` / `AfterMap`,
  `IncludeBase` / `Include`, `MaxDepth`, `PreserveReferences`, `ReverseMap` (basic),
  `ForCtorParam`, `DisableCtorValidation`, `ForSourceMember`
- Expression-tree plan builder: one compiled `MappingPlan` delegate per type pair,
  cached in a thread-safe dictionary, built lazily on first `Map` or eagerly via
  `CompileMappings()`
- Destination construction: parameterless constructor, constructor mapping matched
  by parameter name, `ConstructUsing`, or a runtime-generated proxy for interface
  and abstract destinations (Reflection.Emit, with `INotifyPropertyChanged`)
- Null handling flags: `AllowNullDestinationValues`, `AllowNullCollections`
- Cycle handling: reference cache when `PreserveReferences()` is on, plus a
  cycle guard that returns the default value instead of recursing forever
- Validation: `AssertConfigurationIsValid()` reports every unmapped destination
  member (respecting `MemberList` and ignores) and dry-runs each plan against a
  default instance where one can be constructed, collecting all errors into one
  `AutoMapperConfigurationException`
- Naming conventions: PascalCase default, exact match, snake_case sources, plus
  `RecognizePrefixes` / `RecognizePostfixes` (and destination variants)
- Flattening: `Customer.Name` maps to `CustomerName` via naming-convention splitting
- Fallback chain (exact order in `docs/ARCHITECTURE.md`): collections, assignable,
  nullable unwrapping, `ToString`, `Convert`, string/enum conversions, `Parse`,
  dictionary key/value mapping, constructor fallback, conversion operators, and
  dynamic / string-dictionary mapping in both directions
- Generics: closed-generic pairs work; maps declared for open generic definitions
  (e.g. `CreateMap(typeof(Source<>), typeof(Dest<>))`) are closed at plan time
  (conventional matching plus hooks/converters/transformers; custom member
  expressions recorded against open parameters cannot be replayed — see below)
- `ProjectTo` / `IQueryable` projection: a LINQ expression tree built from the
  configuration's TypeMaps, with null-safe chain rewriting, polymorphic dispatch
  over included-derived maps, and clear projection exceptions for anything that
  cannot translate to a query (delegate conditions, resolver instances, member
  converters) instead of silent client evaluation
- Attribute-driven configuration (`AutoMapper.Configuration.Annotations`):
  ignore, source-member redirect, mapping order, null substitute,
  use-destination-value, converter/resolver attributes, map-at-runtime —
  precedence is fluent config > destination attribute > source attribute >
  convention, applied per behavior
- Features system (`AutoMapper.Features`): global features run once at build,
  mapping features wrap each plan invocation, runtime features are reachable
  from resolvers via `ResolutionContext`
- Full resolver / converter / transformer surface: value resolvers, member value
  resolvers, type converters, value converters, and value transformers (global,
  map-level, then member-level ordering), all instantiated through
  `ConstructServicesUsing` when one is registered
- DI helper in the same project: `AutoMapper.Extensions.Microsoft.DependencyInjection`

## Known limits (documented, by design)

- Projection runs against in-memory providers in this repo's tests; a real
  EF/database provider was not tested here, and a provider that cannot translate
  a given expression will fail at its own translation step.
- Attribute-driven configuration applies to property maps only, not path maps
  or constructor parameters.
- A path-map member resolver receives a null source-member value (paths have
  no source chain).
- Custom member expressions recorded against open generic type parameters
  cannot be replayed for closed maps: .NET expression trees cannot hold a
  lambda over an open generic type, and the public open-map handle exposes no
  per-member surface to record one on. Closed maps still inherit hooks,
  converters, transformers, and conventional matching.
- The non-generic map handle has no type-based `ConvertUsing<TConverter>`
  (instance/function forms only).

## Sealing

A configuration is sealed when it is built: after that, further `CreateMap` calls
throw `AutoMapperConfigurationException`, and further per-map calls on a sealed
map throw the same. Configure everything before creating a mapper.

## Namespace layout

| Area | Namespace |
| --- | --- |
| Public API (config, mapper, maps, naming, exceptions) | `AutoMapper` |
| Configuration expressions and annotations | `AutoMapper.Configuration`, `AutoMapper.Configuration.Annotations` |
| Plan builder, proxies, execution helpers | `AutoMapper.Execution` |
| Internal runtime types | `AutoMapper.Internal` |
| LINQ projection | `AutoMapper.QueryableExtensions` |
| Features system | `AutoMapper.Features` |
| DI helper | `AutoMapper.Extensions.Microsoft.DependencyInjection` |

All code lives under `AutoMapper.*`, so consumer code with `using AutoMapper;`
compiles unchanged against this library; only the package reference changes.

## Building and testing

```sh
~/workspace/.dotnet/dotnet build ~/workspace/ddmapper/DDMapper.sln
```

`dotnet test` does not run in this sandbox (the vstest testhost channel is
broken by the sandbox egress proxy). The same xUnit `[Fact]` methods are
executed by the reflection runner instead, which loads the test DLL and
invokes every fact directly:

```sh
cd ~/workspace/ddmapper/tools/reflection-runner
~/workspace/.dotnet/dotnet run
```

On a machine where `dotnet test` works, prefer `dotnet test` — the runner
exists only to give honest execution evidence where vstest cannot connect.

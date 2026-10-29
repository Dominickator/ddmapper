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

## What Stage 1 includes

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
  by parameter name, or `ConstructUsing`
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
  (conventional matching plus hooks/converters; custom member expressions recorded
  against open parameters are not replayed)
- DI helper in the same project: `AutoMapper.Extensions.Microsoft.DependencyInjection`

## What Stage 1 does NOT include (out of scope, by design)

- `ProjectTo` / `IQueryable` projection
- Runtime proxy generation for interface destinations — mapping to an interface or
  abstract destination throws a clear `AutoMapperMappingException` (map to a
  concrete type instead)
- Attribute-driven configuration, the features system, and the full
  value-resolver / converter / transformer surface beyond the basics above

## Sealing

A configuration is sealed when it is built: after that, further `CreateMap` calls
throw `AutoMapperConfigurationException`, and further per-map calls on a sealed
map throw the same. Configure everything before creating a mapper.

## Namespace layout

| Area | Namespace |
| --- | --- |
| Public API (config, mapper, maps, naming, exceptions) | `AutoMapper` |
| DI helper | `AutoMapper.Extensions.Microsoft.DependencyInjection` |
| Internal plan cache / plan builder / runtime helpers | `AutoMapper` (internal types; no separate internal namespace is exposed in Stage 1) |

Sub-namespaces mirroring the familiar shape (`AutoMapper.Configuration`,
`AutoMapper.Execution`, `AutoMapper.Internal`, `AutoMapper.QueryableExtensions`)
are reserved for later stages as those surfaces land; Stage 1 keeps one library
project with all code under `AutoMapper.*`, so no consumer `using` directive
needs to change when those namespaces appear.

## Building and testing

```sh
~/workspace/.dotnet/dotnet build ~/workspace/ddmapper/DDMapper.sln
~/workspace/.dotnet/dotnet test ~/workspace/ddmapper/DDMapper.sln
```

The test project currently holds smoke tests only (basic mapping, flattening,
string-to-enum fallback, `MapFrom`, `ReverseMap`, missing-map error, validation
error). The full test suite is owned by a later stage.

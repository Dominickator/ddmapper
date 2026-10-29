# DDMapper Architecture (Stage 1)

This document is the Stage 1 specification. It describes behaviour derived from
the project's own design and from the behavioural evidence inventory (type list,
method names, and call-graph order) — not from any other implementation's source.

## Pipeline

```
Configuration (MapperConfigurationExpression)
    -> TypeMap model (sealed on build)
    -> Plan compilation (expression tree, per type pair, cached)
    -> Execution (Mapper.MapCore -> compiled MappingPlan delegate)
    -> Fallback chain (only when no TypeMap plan matches)
```

### 1. Configuration

`MapperConfiguration` takes an `Action<IMapperConfigurationExpression>`. Inside
the action, callers register maps with `CreateMap<TSource, TDestination>()`,
add profiles with `AddProfile` / `AddProfiles`, set naming conventions and
null-handling flags, register prefix/postfix recognition, and optionally
register a service constructor with `ConstructServicesUsing`.

Profiles may declare maps in their constructor (recorded by a deferred mapping
expression and replayed when the profile is added) or by overriding `Configure()`.

When the `MapperConfiguration` constructor returns, the configuration is sealed:

- Conventional source chains are resolved for every destination member
  (see Member matching below).
- Every `TypeMap` is marked sealed. Further `CreateMap` calls throw
  `AutoMapperConfigurationException`, and further per-map fluent calls throw the
  same exception. This is the sealed-throws policy, chosen over silently ignoring
  late configuration so mistakes surface immediately.

### 2. TypeMap model

One `TypeMap` per (source, destination) pair holds:

- `PropertyMaps`: one per destination property, each with a kind
  (Auto / Custom / Ignored / Flattened / Path / CtorOnly), an optional custom
  expression or resolver, a source member chain, condition / precondition,
  null substitute, and an optional member converter.
- `PathMaps`: destination paths (`Address.City`) with the same per-path options.
- `ConstructorMap`: the chosen constructor (or none) plus per-parameter maps.
- Hooks: `BeforeMap` / `AfterMap` actions, run around member assignment.
- Flags: `MaxDepth`, `PreserveReferences`, ignored / unvalidated source members,
  included base pair, included derived pairs.
- Whole-type conversion: `ConvertUsing` (function or converter object) and
  `ConstructUsing` (function, with or without the resolution context).

`ReverseMap()` creates the reversed pair and mirrors simple member-access custom
maps back; anything more complex should be configured explicitly on the reverse
map. That is the documented "basic" scope for Stage 1.

### 3. Plan compilation

For each type pair, `PlanBuilder` builds one expression tree and compiles it to a
`MappingPlan` delegate with the signature

```csharp
delegate object? MappingPlan(object? source, object? destination, ResolutionContext context);
```

The tree encodes, in order:

1. Typed source/destination locals.
2. Destination construction (see below) via `PlanExecutor.ConstructDestination`.
3. Reference registration: when `PreserveReferences()` is on, the new destination
   is stored in the context's reference cache *before* members are mapped, so a
   cycle back to the same source object resolves to the in-progress destination.
4. `BeforeMap` hooks (map hooks, then per-operation hooks).
5. One assignment per non-ignored property map. The value comes from
   `PlanExecutor.MapMemberValue`, which applies precondition, custom expression
   or resolver, source-chain walk, condition, null substitute, null-handling
   flags, member converter, and finally nested mapping through `MapperRuntime`.
6. One `ApplyPath` call per path map, which walks/creates intermediate
   destination objects and sets the leaf value.
7. `AfterMap` hooks, then the destination is returned.

Plans are cached per configuration (a dictionary on the `MapperConfiguration`,
keyed by (source, destination) and guarded by a lock), so a pair is compiled once
per configuration lifetime — two configurations mapping the same pair with
different member options never share a plan. Plans are built lazily on first
`Map` and eagerly by `CompileMappings()`.

Whole-type converter maps short-circuit plan building: the plan is the converter
itself, since there are no members to assign.

#### Destination construction

In order: an existing destination (mapping onto an object) is reused;
`ConstructUsing` (with context, then without) wins next; then the parameterless
constructor; then the constructor whose parameters all match source members
(case-insensitive) or explicit `ForCtorParam` maps. Constructor parameters that
match a destination property suppress that property's later assignment. If no
constructor applies, the plan throws a clear `AutoMapperMappingException` at map
time. Interface and abstract destinations are rejected with a clear
`NotSupported`-style mapping exception (Stage 1 has no proxy generation).

#### Null propagation

- A null source member with a `NullSubstitute` yields the substitute.
- With `AllowNullDestinationValues = false`, null source members are skipped
  (the existing destination value is kept).
- With `AllowNullCollections = false`, a null source collection maps to an empty
  destination collection instead of null.
- A null anywhere along a flattened chain yields a null member value.
- Null source objects at the top level map to null (or the default value type).

#### Depth and cycles

`ResolutionContext` carries the current depth, the set of sources currently being
mapped, and the reference cache, shared across the whole top-level operation.
`MaxDepth` (per map, else the configuration default; 0 means unlimited) caps
nesting: deeper values map to the default. A source object already on the active
stack returns its preserved destination when references are preserved, and the
default value otherwise — cycles never recurse forever.

### 4. Execution: MapCore semantics

`Mapper` implements `IMapper`. Every `Map` overload funnels into `MapCore`:

1. Null source maps to null / default.
2. The plan is resolved once per type pair: the runtime source type is tried
   first (so a derived source dispatches to its own map), then the declared
   source type, then included-derived maps registered on the base map.
3. The compiled delegate is invoked with (source, existing destination, context).
4. If no plan matches, the fallback chain runs (below).
5. If nothing handles the pair, a clear `AutoMapperMappingException` is thrown
   naming both types and suggesting a `CreateMap` entry.

Mapping onto an existing destination reuses that object; the plan's construction
step returns it unchanged and members are assigned onto it.

### 5. Fallback chain (exact order)

When no `TypeMap` plan matches, fallbacks are tried in exactly this order.
The first fallback that applies wins; later fallbacks are not consulted.

1. **Collection** — `IEnumerable` source to list/array/collection destination,
   mapping each element recursively.
2. **Assignable** — destination type is assignable from the source type: pass through.
3. **NullableSource** — unwrap a nullable source type and map its underlying value.
4. **ToString** — destination is `string`: use the source's `ToString()`.
5. **NullableDestination** — wrap a mapped value into a nullable destination type.
6. **Convert** — `IConvertible` / `System.Convert` conversion.
7. **StringToEnum** — parse a string (case-insensitive) into an enum.
8. **EnumToEnum** — match by enum member name, else convert by numeric value.
9. **ParseString** — destination type exposes a static `Parse(string)` method.
10. **UnderlyingTypeEnum** — enum to/from its underlying numeric type.
11. **KeyValue** — dictionary-entry / key-value pair mapping (dictionary pairs are
    handled by the dictionary branch in `Mapper` before this chain; this slot
    preserves the ordering contract for pair-shaped values).
12. **Constructor** — destination constructor whose parameters match source
    members by name.
13. **ConversionOperator** — `implicit` / `explicit` operators declared on either side.
14. **FromDynamic** — `ExpandoObject` / `IDictionary<string, object>` source to a
    concrete destination, matched by member name.
15. **ToDynamic** — source to `ExpandoObject`, copying readable members by name.
16. **FromStringDictionary** — `IDictionary<string, string>` source to a concrete
    destination.
17. **ToStringDictionary** — source to `IDictionary<string, string>`, copying
    readable members as strings.

Dictionary-to-dictionary mapping (key and value mapped recursively) runs before
the generic chain, since dictionaries are also enumerables and must not be
treated as plain collections.

### 6. Member matching, conventions, and flattening

For each destination member without an explicit map, matching proceeds:

1. Direct match (case-insensitive), after stripping configured destination
   prefixes/postfixes and source prefixes/postfixes.
2. Flattening: the destination name is split into PascalCase words and walked
   as a member chain, consuming the longest matching prefix at each level
   (`CustomerName` -> `Customer` + `Name`; deeper chains work the same way).
3. Underscore handling: a snake_case source member converts to PascalCase for
   comparison (`customer_name` -> `CustomerName`), and the lower-underscore
   destination convention compares underscore-joined words.

`SourceNamingConvention` / `DestinationNamingConvention` select the splitting
strategy: PascalCase (default), exact match, or lower-underscore.

### 7. Validation

`AssertConfigurationIsValid()` walks every `TypeMap`:

- With `MemberList.Destination` (the default), every writable destination member
  must be mapped (explicitly, conventionally, or via a constructor parameter)
  or ignored; otherwise it is reported.
- With `MemberList.Source`, every readable source member must be mapped or
  ignored / marked do-not-validate.
- With `MemberList.None`, no unmapped-member check runs.
- Each plan is then dry-run against a default-constructed source instance where
  one can be constructed; failures are collected, not thrown one at a time.

All errors are combined into a single `AutoMapperConfigurationException` whose
message lists every problem, in the library's own wording.

### 8. Generics

Closed-generic pairs (e.g. `Wrapper<int>` -> `WrapperDto<int>`) map like any
other pair. A map declared for open generic definitions
(`CreateMap(typeof(Source<>), typeof(Dest<>))`) is closed at plan time: the
closed `TypeMap` inherits hooks, converters, `MaxDepth`, and
`PreserveReferences`, and its property maps are resolved conventionally against
the closed types. Custom member expressions recorded against open type
parameters are not replayed for closed maps — that is a documented Stage 1
limitation, not a silent behaviour change.

### 9. Dependency injection

`AutoMapper.Extensions.Microsoft.DependencyInjection.ServiceCollectionExtensions`
registers `IConfigurationProvider`, the concrete `MapperConfiguration`, and
`IMapper` as singletons. `AddDDMapper` and its alias `AddAutoMapper` accept a
configuration action, assemblies to scan for profiles, or explicit profile types.

### 10. Out of scope for Stage 1

`ProjectTo` / `IQueryable` projection, runtime proxy generation for interface
destinations, attribute-driven configuration, the features system, and the full
value-resolver / converter / transformer surface beyond the basics listed in the
README. Those are later stages; Stage 1 throws clear, original-worded exceptions
rather than silently degrading where a later-stage feature is requested.

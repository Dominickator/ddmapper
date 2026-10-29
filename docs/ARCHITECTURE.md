# DDMapper Architecture

This document is the architecture specification. It describes behaviour derived from
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

## 11. Stage 2: runtime proxy generation for interface/abstract destinations

Stage 1's rejection is replaced by `AutoMapper.Execution.ProxyGenerator`
(Reflection.Emit, original code). For an interface destination it emits a
concrete class implementing every property (including base-interface
properties) with a private backing field per property and a parameterless
constructor; for an abstract destination it emits a subclass overriding every
abstract property the same way and calling the base parameterless constructor.
Every proxy implements `INotifyPropertyChanged` and raises
`PropertyChanged` from every emitted setter, including when the destination
interface does not itself extend `INotifyPropertyChanged`. Generated types are
cached per destination type under a lock, so one interface always maps to one
proxy type. The plan's destination-construction step (`PlanExecutor
.ConstructDestination`) constructs the proxy; `ConstructUsing` and an existing
destination still win over it. Additionally, an *unmapped* interface/abstract
destination is satisfied by a conventional proxy copy in `Mapper.MapInternal`
(matching source members by name, mapped recursively), which is what makes
nested interface members and interface collection elements work without an
explicit map for that pair; a source that already implements the destination
keeps the Stage 1 assignable behaviour and is returned as-is.

Documented unproxyable boundary (all raised as clear
`AutoMapperMappingException` from the proxy generator, naming the offending
member): interface or abstract members that cannot be auto-implemented —
ordinary methods, events other than `PropertyChanged`, indexers, properties
declared with conflicting types across base interfaces, abstract classes with
no parameterless constructor, abstract classes leaving interface methods
unimplemented, and open generic destinations. Collection interfaces (e.g.
`IList<T>`) fall under the methods rule and are not proxy destinations;
concrete collections and arrays remain the way to map collections. Limitation:
when an abstract base already supplies a concrete `INotifyPropertyChanged`
implementation, its event is inherited and overridden setters cannot raise
through the base's private backing field, so no notification is raised for
those setters.

## 12. Stage 3: attribute-driven configuration and the features system

### 12.1 Attributes (`AutoMapper.Configuration.Annotations`, `src/DDMapper/Attributes.cs`)

Eight attributes, all usable on properties and fields, all inherited:

- `IgnoreAttribute` — the member is skipped and counts as ignored for validation.
- `SourceMemberAttribute(name)` — redirects a destination member to a named
  source member; the name may be a dotted chain (`"Customer.Name"`), resolved
  as a flattened source chain. A name that resolves to nothing throws an
  `AutoMapperConfigurationException` at build. Meaningful only on destination
  members.
- `MappingOrderAttribute(order)` — ordered members are assigned first, in
  ascending order; unordered members keep their original relative order after
  them. The plan builder iterates `TypeMap.PropertyMaps` in list order, so
  `ResolveConventionalMaps` stable-sorts the list to make this observable.
- `NullSubstituteAttribute(substitute)` — the value used when the resolved
  source value is null (attribute arguments must be compile-time constants).
- `UseDestinationValueAttribute` — keeps the existing destination value.
- `ValueConverterAttribute(type)` — resolves the member through a converter
  type exposing a `Convert` method, instantiated at build through the
  configuration's service constructor (`ConstructServicesUsing`).
- `ValueResolverAttribute(type)` — resolves the member through a resolver
  type exposing a `Resolve` method, instantiated the same way. A resolver
  member is assigned even when no source member matches by name.
- `MapAtRuntimeAttribute` — the source chain is re-resolved by name against
  the runtime source type at map time (most-derived member wins), so a derived
  runtime source whose member hides the declared member supplies its own
  value; with no plan-time chain the destination name is used for the lookup.
  The fluent `MapAtRuntime` flag gains this same runtime behavior (previously
  it was stored but inert).

Precedence, highest first, applied per member in `TypeMap.ResolveConventionalMaps`:

1. Explicit fluent configuration (`ForMember` / `ForPath` / `ForSourceMember`).
   Detected at build time by any fluent-set field (custom expression,
   resolver, converter, condition, null substitute, `UseDestinationValue`,
   `MapAtRuntime`) or an Ignored/Custom kind. Fluent members are left
   untouched by attributes, but a fluent member that only sets a condition,
   substitute, converter, or flag still gets its conventional source chain
   (the pre-Stage-3 behavior).
2. Attributes on the destination member.
3. Attributes on the terminal source member of the resolved chain
   (`SourceMember` redirection on a source member has no effect).
4. Convention (direct match / flattening).

Attribute application happens once, at seal time (and for closed-generic maps
at first plan build). The plan guard was extended so resolver and
`MapAtRuntime` members are assigned even with an empty source chain.

### 12.2 Features (`AutoMapper.Features`, `src/DDMapper/Features.cs`)

- `IGlobalFeature.Configure(IMapperConfigurationExpression)` — runs once per
  `MapperConfiguration`, after the user's configure action and before
  sealing, from a snapshot of the registered features. It may register maps
  or further features; features registered by a global feature are not
  themselves run in the same build.
- `IMappingFeature.BeforeMap` / `AfterMap` — run around each configured map
  operation in `PlanExecutor.InvokeBefore` / `InvokeAfter`, outermost around
  the map's own hooks: features-before, map hooks, member assignment,
  map hooks, features-after. They fire for every plan invocation, including
  nested maps reached while mapping a member.
- `IRuntimeFeature` — a marker. Runtime features are not invoked; they are
  reachable from resolvers, converters, and hooks through
  `ResolutionContext.Features` (alias `RuntimeFeatures`), which returns the
  configuration's registry (empty for a non-DDMapper provider).
- `Features<T>` — an ordered registry with `Add`, `Get<TFeature>`,
  `TryGet<TFeature>`, `All`, and `Count`.

Registration is on the configuration expression: `AddFeature` overloads for
each feature interface, plus generic `AddFeature<TFeature>` which registers
a feature in every registry whose interface it implements (and rejects a
type implementing none of them). The plain overloads are ambiguous for a
type implementing two feature interfaces; the generic overload with an
explicit type argument is the intended call there. The configuration exposes
`GlobalFeatures`, `MappingFeatures`, and `RuntimeFeatures`.

Limitations carried forward: attribute-driven configuration applies to
property maps only, not to path maps or constructor parameters; `IncludeBase`
copies member options (including the new order flag) at fluent time, so
attribute values on a base map's destination type are not re-applied to the
derived map — the derived destination member's own attributes are read
instead. A resolver/converter attribute without a configured service
constructor fails at build with the existing "no service constructor"
configuration error.

## 13. Stage 4: full resolver / converter / transformer surface, service construction, and generics edges

### 13.1 Resolver / converter / transformer surface (object mapping)

- `IValueResolver<TSource, TDestination, TMember>` — fluent instance
  (`MapFrom(resolver)`) and type-based (`MapFrom<TResolver>()`) forms.
  The resolver receives the source, the destination, the current
  destination member value, and the context; the context's `Items`
  dictionary is now public (`ResolutionContext.Items`), so resolvers
  read per-operation items supplied through `Map` options.
- `IMemberValueResolver<TSource, TDestination, TSourceMember, TDestinationMember>`
  — fluent instance (`MapFrom<TResolver, TSourceMember>(resolver)`) and
  type-based (`MapFromMember<TResolver, TSourceMember>()`) forms. It
  receives the source member value as well as the destination member
  value. The source member value comes from a `MapFrom` expression when
  one is also configured, else from the conventional chain (re-resolved
  at runtime for `MapAtRuntime` members), else the default when the
  member has no source chain — never the whole source object, which is
  what an empty chain walk would return. A member-resolver result is
  already in destination-member form, so it skips nested mapping but
  still passes through a member converter (when one is also configured)
  and the value transformers. A member-resolver map now gets its
  conventional source chain resolved at build time like any other
  fluent member (previously, `Custom`-kind maps skipped chain
  resolution entirely, which left member resolvers with no source
  value).
- `ITypeConverter<TSource, TDestination>` (whole-type) — instance
  (`ConvertUsing(converter)`), function (`ConvertUsing(func)`), and
  type-based (`ConvertUsing<TConverter>()`) forms. Converter invocation
  is duck-typed (`ConverterInvoker`): `Convert(source, destination,
  context)`, then `Convert(source, destination)`, then
  `Convert(source)` — so converter classes without a context parameter
  work through the type-based form, while the interface shape keeps
  working through the instance form.
- `IValueConverter<TSourceMember, TDestinationMember>` (member-level)
  — `ConvertUsing(converter)` on a member, plus type-based
  `ConvertUsing<TConverter>()`. It is applied after the source value is
  resolved and before assignment; its result is not mapped again.
  Member converters (both interface shapes) also work on nested
  members, because nested member values flow through the same
  `MapMemberValue` path, and on path maps (`ForPath`), where the
  converter runs after resolution and before the nested mapping step.
- Value transformers — `AddValueTransformer` at three levels:
  configuration-global (`cfg.AddValueTransformer<TMember>(func)`),
  map-level (`CreateMap(...).AddValueTransformer<TMember>(func)`), and
  per-member (`ForMember(..., m => m.AddValueTransformer(func))`).
  They run, in registration order within each level, global first,
  then map-level, then member-level. A global or map-level transformer
  applies only to members whose destination type its member type
  covers (assignable check); a member transformer applies to its own
  member. Order relative to the rest of the pipeline: resolver /
  converter first, then nested mapping, then transformers, so a
  transformer sees the final mapped value. Only values the map actually
  produces are transformed: a null substitute is transformed, but a
  kept destination value (`UseDestinationValue`, failed precondition
  or condition, skipped null) is returned untouched.
- `IMappingAction<TSource, TDestination>` — instance
  (`BeforeMap(action)` / `AfterMap(action)`) and type-based
  (`BeforeMap<TAction>()` / `AfterMap<TAction>()`) hooks, running in
  the same slots as the delegate hooks (features outermost, then map
  hooks, in registration order).

### 13.2 Service construction (`ConstructServicesUsing`)

Every place the runtime instantiates a user type now routes through
the configuration's service constructor when one is registered:

- Fluent type-based resolvers, member resolvers, member converters,
  whole-type converters, and mapping actions are recorded as types and
  instantiated once at build time (`TypeMap.ResolveDeferredServices`,
  called from `ResolveConventionalMaps`, so closed-generic maps get
  the same treatment at first plan build). The resolution is
  idempotent: each type is cleared after instantiation.
- Attribute resolvers and converters (unchanged mechanism) and
  profile types without a parameterless constructor (new: `AddProfiles`
  falls back to the service constructor when default construction
  fails).
- Whole-type converter types are also resolved in `PlanBuilder` as a
  fallback for maps compiled without the seal-time step.

The default service constructor changed in this stage, superseding the
Stage 3 note: with no `ConstructServicesUsing` registered, it now
default-constructs the type, and only when that is impossible does it
raise the existing clear `AutoMapperConfigurationException` (naming
the type and `ConstructServicesUsing`). A parameterless resolver,
converter, or action therefore works with no explicit constructor;
a type needing constructor arguments without a registered constructor
still fails clearly at build time, not at first map.

### 13.3 Projection interaction

Nothing in this stage is silently dropped or client-evaluated in
`ProjectTo`. Each category either projects (when genuinely
translatable) or is rejected with `AutoMapperProjectionException`:

- Works: a converter expressed as a translatable `MapFrom` expression
  (the pre-existing projection path).
- Rejected: value resolvers, member value resolvers, member
  converters, value converters, whole-type converters (now including
  the type-based form), mapping actions / BeforeMap / AfterMap hooks
  (previously hooks were silently dropped in projection; they are now
  rejected, including on the derived-dispatch base body), value
  transformers at member, map, and global level (a global transformer
  rejects only members whose destination type it covers; a global
  transformer covering no member of the projected pair does not block
  projection), and converters / resolvers / transformers on path maps
  (carried into nested path bindings so nested rejections fire too).

### 13.4 Generics edges

- Closed-generic maps now inherit more than hooks and whole-type
  converters from their open map: map-level transformers, mapping
  action types, the whole-type converter type, construction functions,
  ignored / unvalidated source members, and included-derived pairs.
  The non-generic map handle also gained `BeforeMap`, `AfterMap`, and
  `AddValueTransformer`, so hooks and transformers on an open map are
  reachable through the public API.
- Generic collection element mapping, nested generics
  (`Wrapper<Wrapper<int>>`), generic interface destinations via
  proxies, and generic derived-type dispatch all map through the
  existing collection / proxy / dispatch paths (tested).
- Nested member dispatch now mirrors the top level: a nested value
  tries its runtime type's map first, then the declared member type's
  map. Previously nested values dispatched on the runtime type only,
  so a derived nested source with only a base map failed instead of
  mapping through the base plan — which is also what makes
  `MapAtRuntime` on a nested map observe the derived runtime source.
  Existing behaviour is preserved when a runtime map exists.

### 13.5 Open-generic custom-expression limit: documented, with reason

The Stage 1 limit stays a limit, and the reason is now precise rather
than "not replayed":

1. .NET expression trees cannot hold a lambda over an open generic
   type at all: `Expression.Parameter` rejects a generic definition
   and also rejects any type containing a generic parameter (both
   verified against the framework). There is therefore no expression
   tree form in which a custom member expression "recorded against
   open type parameters" could exist.
2. The public open-map handle (`CreateMap(Type, Type)`) exposes no
   per-member surface, so no custom member expression can be recorded
   on an open map through the public API in the first place.

Consequence: closed maps rely on conventional matching plus the
inherited map-level state listed in 13.4, and never silently carry a
stale custom expression. A test pins both halves: building a parameter
over the open definition (or over a generic-parameter instantiation)
throws `ArgumentException`, and a closed pair from an open map still
maps its members conventionally. Implementing a replay pass was
considered and rejected as unsound: the only expressions that could
be copied would be ones over closed types, which rebasing onto a
different closed pair would misbind.

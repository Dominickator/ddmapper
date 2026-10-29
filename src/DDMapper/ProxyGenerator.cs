using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace AutoMapper.Execution;

/// <summary>
/// Runtime proxy generation for interface and abstract-class mapping destinations
/// (DDMapper Stage 2).
///
/// For a destination interface, this emits a concrete class that implements every
/// property of the interface (including properties inherited from base interfaces)
/// with a private backing field per property, plus a parameterless constructor.
/// For an abstract destination class, it emits a subclass that overrides every
/// abstract property the same way and forwards to the base parameterless
/// constructor. In both cases the proxy also implements
/// <see cref="INotifyPropertyChanged"/> and raises
/// <see cref="INotifyPropertyChanged.PropertyChanged"/> from every emitted
/// setter, so mapped assignments are observable.
///
/// Generated types are cached per destination type (one proxy type per
/// destination, shared by every mapper and configuration), and generation is
/// serialized so concurrent first uses of the same destination agree on one type.
/// </summary>
public static class ProxyGenerator
{
    private static readonly object CacheLock = new();
    private static readonly Dictionary<Type, Type> Cache = new();
    private static readonly ModuleBuilder Module;

    static ProxyGenerator()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("DDMapper.DynamicProxies"), AssemblyBuilderAccess.Run);
        Module = assembly.DefineDynamicModule("DDMapper.DynamicProxies");
    }

    /// <summary>Returns the cached proxy type for a destination, generating it on first use.</summary>
    /// <exception cref="AutoMapperMappingException">The destination cannot be proxied (see message).</exception>
    public static Type GetProxyType(Type destinationType)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(destinationType, out var cached)) return cached;
            var generated = EmitProxy(destinationType);
            Cache[destinationType] = generated;
            return generated;
        }
    }

    /// <summary>Creates an instance of the proxy type for a destination.</summary>
    public static object CreateProxy(Type destinationType)
    {
        var proxyType = GetProxyType(destinationType);
        return Activator.CreateInstance(proxyType)
            ?? throw new AutoMapperMappingException(
                $"Cannot create a proxy for {destinationType.Name}: its generated type has no usable parameterless constructor.");
    }

    /// <summary>Like <see cref="CreateProxy"/>, but returns false instead of throwing for unproxyable destinations.</summary>
    public static bool TryCreateProxy(Type destinationType, out object? proxy)
    {
        try
        {
            proxy = CreateProxy(destinationType);
            return true;
        }
        catch (AutoMapperMappingException)
        {
            proxy = null;
            return false;
        }
    }

    /// <summary>
    /// All settable/gettable destination properties for a type, flattened across
    /// inherited interfaces. For classes this is the ordinary public instance
    /// property list; for interfaces it also includes properties declared on base
    /// interfaces (which a plain <c>GetProperties</c> call is not guaranteed to
    /// surface), most-derived declaration winning on a name clash.
    /// </summary>
    public static IEnumerable<PropertyInfo> GetDestinationProperties(Type destinationType)
    {
        if (!destinationType.IsInterface)
            return destinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<PropertyInfo>();
        var queue = new Queue<Type>();
        queue.Enqueue(destinationType);
        var visited = new HashSet<Type> { destinationType };
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var prop in current.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var getter = prop.GetGetMethod(true);
                var setter = prop.GetSetMethod(true);
                if ((getter == null || getter.IsStatic) && (setter == null || setter.IsStatic)) continue;
                if (seen.Add(prop.Name)) result.Add(prop);
            }
            foreach (var parent in current.GetInterfaces())
                if (visited.Add(parent)) queue.Enqueue(parent);
        }
        return result;
    }

    // ---- Analysis -------------------------------------------------------

    private sealed class PropertySpec
    {
        public required string Name { get; init; }
        public required Type PropertyType { get; init; }
        public required MethodInfo? Getter { get; init; }
        public required MethodInfo? Setter { get; init; }
        public required List<MethodInfo> GetterOverrides { get; init; }
        public required List<MethodInfo> SetterOverrides { get; init; }
        public required bool IsAbstractOverride { get; init; }
    }

    private static Type EmitProxy(Type destinationType)
    {
        if (destinationType.ContainsGenericParameters)
            throw new AutoMapperMappingException(
                $"Cannot create a proxy for {destinationType.Name}: it is an open generic type. Map to a closed type instead.");
        if (destinationType.IsInterface)
            return EmitInterfaceProxy(destinationType);
        if (destinationType.IsAbstract)
            return EmitAbstractProxy(destinationType);
        throw new AutoMapperMappingException(
            $"Cannot create a proxy for {destinationType.Name}: it is neither an interface nor an abstract class.");
    }

    private static Type EmitInterfaceProxy(Type destinationType)
    {
        var interfaces = AllInterfaces(destinationType);
        var accessors = new HashSet<MethodInfo>();
        foreach (var iface in interfaces)
        {
            foreach (var prop in iface.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var getter = prop.GetGetMethod(true);
                var setter = prop.GetSetMethod(true);
                if (getter != null) accessors.Add(getter);
                if (setter != null) accessors.Add(setter);
            }
            foreach (var ev in iface.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (ev.AddMethod != null) accessors.Add(ev.AddMethod);
                if (ev.RemoveMethod != null) accessors.Add(ev.RemoveMethod);
                if (ev.RaiseMethod != null) accessors.Add(ev.RaiseMethod);
            }
        }

        // Boundary: anything that is not an auto-implementable property or the
        // PropertyChanged event cannot be synthesized, so the destination is
        // unproxyable. Default interface methods (which carry their own body)
        // and static members need no implementation and are exempt.
        foreach (var iface in interfaces)
        {
            foreach (var method in iface.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.IsStatic || accessors.Contains(method)) continue;
                if (HasBody(method)) continue;
                throw new AutoMapperMappingException(
                    $"Cannot create a proxy for {destinationType.Name}: it declares method {method.Name} which cannot be auto-implemented. Map to a concrete type, or supply a construction function for this map.");
            }
            foreach (var ev in iface.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (ev.EventHandlerType == typeof(PropertyChangedEventHandler) && ev.Name == "PropertyChanged") continue;
                throw new AutoMapperMappingException(
                    $"Cannot create a proxy for {destinationType.Name}: it declares event {ev.Name} which cannot be auto-implemented. Map to a concrete type, or supply a construction function for this map.");
            }
        }

        var specs = new List<PropertySpec>();
        foreach (var group in GetDestinationProperties(destinationType).GroupBy(p => p.Name, StringComparer.Ordinal))
        {
            var props = group.ToList();
            var representative = props[0];
            if (props.Any(p => p.PropertyType != representative.PropertyType))
                throw new AutoMapperMappingException(
                    $"Cannot create a proxy for {destinationType.Name}: property {representative.Name} is declared with conflicting types across its interfaces.");
            if (representative.GetIndexParameters().Length > 0)
                throw new AutoMapperMappingException(
                    $"Cannot create a proxy for {destinationType.Name}: indexer property {representative.Name} cannot be auto-implemented. Map to a concrete type, or supply a construction function for this map.");
            var getters = props.Select(p => p.GetGetMethod(true)).Where(m => m != null).Select(m => m!).Distinct().ToList();
            var setters = props.Select(p => p.GetSetMethod(true)).Where(m => m != null).Select(m => m!).Distinct().ToList();
            specs.Add(new PropertySpec
            {
                Name = representative.Name,
                PropertyType = representative.PropertyType,
                Getter = representative.GetGetMethod(true),
                Setter = representative.GetSetMethod(true),
                GetterOverrides = getters,
                SetterOverrides = setters,
                IsAbstractOverride = false,
            });
        }

        var extraInterfaces = new List<Type> { destinationType };
        if (!typeof(INotifyPropertyChanged).IsAssignableFrom(destinationType))
            extraInterfaces.Add(typeof(INotifyPropertyChanged));
        // The proxy always supplies the PropertyChanged event itself: either the
        // destination already requires it (it extends INotifyPropertyChanged) or
        // the proxy adds the interface. Either way the event accessors to
        // implement are INotifyPropertyChanged's own.
        var inpcEvent = typeof(INotifyPropertyChanged).GetEvent("PropertyChanged")!;
        return BuildProxyType(
            destinationType, parent: typeof(object), interfaces: extraInterfaces,
            specs, emitPropertyChanged: true,
            eventAddOverrides: new[] { inpcEvent.GetAddMethod()! }.ToList(),
            eventRemoveOverrides: new[] { inpcEvent.GetRemoveMethod()! }.ToList(),
            baseConstructor: typeof(object).GetConstructor(Type.EmptyTypes)!);
    }

    private static Type EmitAbstractProxy(Type destinationType)
    {
        if (destinationType.IsSealed)
            throw new AutoMapperMappingException(
                $"Cannot create a proxy for {destinationType.Name}: it is a static class and cannot be subclassed.");
        var baseConstructor = destinationType.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (baseConstructor == null)
            throw new AutoMapperMappingException(
                $"Cannot create a proxy for {destinationType.Name}: it has no parameterless constructor for a proxy subclass to call. Map to a concrete type, or supply a construction function for this map.");

        var propertyAccessors = new HashSet<MethodInfo>();
        var eventAccessors = new HashSet<MethodInfo>();
        foreach (var prop in destinationType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (prop.GetGetMethod(true) is { } g) propertyAccessors.Add(g);
            if (prop.GetSetMethod(true) is { } s) propertyAccessors.Add(s);
        }
        foreach (var ev in destinationType.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (ev.AddMethod != null) eventAccessors.Add(ev.AddMethod);
            if (ev.RemoveMethod != null) eventAccessors.Add(ev.RemoveMethod);
            if (ev.RaiseMethod != null) eventAccessors.Add(ev.RaiseMethod);
        }

        // Boundary: abstract members other than properties and the
        // PropertyChanged event cannot be synthesized.
        foreach (var method in destinationType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (!method.IsAbstract || method.IsStatic) continue;
            if (propertyAccessors.Contains(method) || eventAccessors.Contains(method)) continue;
            throw new AutoMapperMappingException(
                $"Cannot create a proxy for {destinationType.Name}: it declares abstract method {method.Name} which cannot be auto-implemented. Map to a concrete type, or supply a construction function for this map.");
        }
        foreach (var ev in destinationType.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var add = ev.GetAddMethod(true);
            if (add == null || !add.IsAbstract) continue;
            if (ev.EventHandlerType == typeof(PropertyChangedEventHandler) && ev.Name == "PropertyChanged") continue;
            throw new AutoMapperMappingException(
                $"Cannot create a proxy for {destinationType.Name}: it declares abstract event {ev.Name} which cannot be auto-implemented. Map to a concrete type, or supply a construction function for this map.");
        }
        // An abstract class may also leave interface methods unimplemented
        // without declaring them abstract; those cannot be synthesized either.
        foreach (var iface in destinationType.GetInterfaces())
        {
            InterfaceMapping map;
            try { map = destinationType.GetInterfaceMap(iface); }
            catch { continue; }
            for (int i = 0; i < map.InterfaceMethods.Length; i++)
            {
                var target = map.TargetMethods[i];
                var ifaceMethod = map.InterfaceMethods[i];
                if (target == null || !target.IsAbstract) continue;
                if (propertyAccessors.Contains(target) || eventAccessors.Contains(target)) continue;
                if (HasBody(ifaceMethod)) continue;
                throw new AutoMapperMappingException(
                    $"Cannot create a proxy for {destinationType.Name}: it leaves interface method {ifaceMethod.Name} unimplemented, which cannot be auto-implemented. Map to a concrete type, or supply a construction function for this map.");
            }
        }

        var specs = new List<PropertySpec>();
        foreach (var prop in destinationType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var getter = prop.GetGetMethod(true);
            var setter = prop.GetSetMethod(true);
            bool getterAbstract = getter != null && getter.IsAbstract;
            bool setterAbstract = setter != null && setter.IsAbstract;
            if (!getterAbstract && !setterAbstract) continue; // Inherited concrete property: used as-is.
            if (prop.GetIndexParameters().Length > 0)
                throw new AutoMapperMappingException(
                    $"Cannot create a proxy for {destinationType.Name}: abstract indexer property {prop.Name} cannot be auto-implemented. Map to a concrete type, or supply a construction function for this map.");
            specs.Add(new PropertySpec
            {
                Name = prop.Name,
                PropertyType = prop.PropertyType,
                Getter = getterAbstract ? getter : null,
                Setter = setterAbstract ? setter : null,
                GetterOverrides = getterAbstract ? new List<MethodInfo> { getter! } : new List<MethodInfo>(),
                SetterOverrides = setterAbstract ? new List<MethodInfo> { setter! } : new List<MethodInfo>(),
                IsAbstractOverride = true,
            });
        }

        // PropertyChanged: emit it unless the base already supplies a concrete
        // implementation (in that case the inherited event is used, and
        // overridden setters cannot raise through the base's private backing
        // field — a documented limitation, not a failure).
        bool baseSuppliesInpc = false;
        MethodInfo? baseEventAdd = null, baseEventRemove = null;
        if (typeof(INotifyPropertyChanged).IsAssignableFrom(destinationType))
        {
            var ownEvent = destinationType.GetEvent("PropertyChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var ownAdd = ownEvent?.GetAddMethod(true);
            if (ownAdd != null && !ownAdd.IsAbstract)
            {
                baseSuppliesInpc = true;
            }
            else if (ownAdd != null)
            {
                baseEventAdd = ownAdd;
                baseEventRemove = ownEvent!.GetRemoveMethod(true);
            }
        }
        bool emitPropertyChanged = !baseSuppliesInpc;
        var extraInterfaces = new List<Type>();
        if (emitPropertyChanged && !typeof(INotifyPropertyChanged).IsAssignableFrom(destinationType))
            extraInterfaces.Add(typeof(INotifyPropertyChanged));
        var inpcEvent = typeof(INotifyPropertyChanged).GetEvent("PropertyChanged")!;
        var addOverrides = new List<MethodInfo>();
        var removeOverrides = new List<MethodInfo>();
        if (emitPropertyChanged)
        {
            if (baseEventAdd != null) addOverrides.Add(baseEventAdd);
            if (baseEventRemove != null) removeOverrides.Add(baseEventRemove);
            // The INotifyPropertyChanged accessors are also satisfied when the
            // base event overrides them; adding both targets is harmless when
            // they differ and required when the base declares no event of its own.
            if (typeof(INotifyPropertyChanged).IsAssignableFrom(destinationType) || extraInterfaces.Count > 0)
            {
                if (!addOverrides.Contains(inpcEvent.GetAddMethod()!)) addOverrides.Add(inpcEvent.GetAddMethod()!);
                if (!removeOverrides.Contains(inpcEvent.GetRemoveMethod()!)) removeOverrides.Add(inpcEvent.GetRemoveMethod()!);
            }
        }
        return BuildProxyType(
            destinationType, parent: destinationType, interfaces: extraInterfaces,
            specs, emitPropertyChanged,
            eventAddOverrides: addOverrides, eventRemoveOverrides: removeOverrides,
            baseConstructor);
    }

    // ---- Emission -------------------------------------------------------

    private static Type BuildProxyType(
        Type destinationType, Type parent, List<Type> interfaces, List<PropertySpec> specs,
        bool emitPropertyChanged, List<MethodInfo> eventAddOverrides, List<MethodInfo> eventRemoveOverrides,
        ConstructorInfo baseConstructor)
    {
        var typeBuilder = Module.DefineType(
            ProxyTypeName(destinationType),
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            parent, interfaces.ToArray());

        FieldBuilder? eventField = null;
        MethodBuilder? notifyMethod = null;
        if (emitPropertyChanged)
        {
            eventField = typeBuilder.DefineField(
                "<PropertyChanged>proxyField", typeof(PropertyChangedEventHandler), FieldAttributes.Private);
            notifyMethod = DefineNotifyMethod(typeBuilder, eventField);
            var addMethod = DefineEventAccessor(typeBuilder, "add_PropertyChanged", eventField, isAdd: true);
            var removeMethod = DefineEventAccessor(typeBuilder, "remove_PropertyChanged", eventField, isAdd: false);
            foreach (var target in eventAddOverrides) typeBuilder.DefineMethodOverride(addMethod, target);
            foreach (var target in eventRemoveOverrides) typeBuilder.DefineMethodOverride(removeMethod, target);
            var eventBuilder = typeBuilder.DefineEvent(
                "PropertyChanged", EventAttributes.None, typeof(PropertyChangedEventHandler));
            eventBuilder.SetAddOnMethod(addMethod);
            eventBuilder.SetRemoveOnMethod(removeMethod);
        }

        foreach (var spec in specs)
        {
            var field = typeBuilder.DefineField("_proxy_" + spec.Name, spec.PropertyType, FieldAttributes.Private);
            var propertyBuilder = typeBuilder.DefineProperty(
                spec.Name, PropertyAttributes.None, spec.PropertyType, Type.EmptyTypes);
            if (spec.Getter != null)
            {
                var getter = typeBuilder.DefineMethod(
                    "get_" + spec.Name,
                    AccessOf(spec.Getter) | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                    spec.PropertyType, Type.EmptyTypes);
                var il = getter.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, field);
                il.Emit(OpCodes.Ret);
                propertyBuilder.SetGetMethod(getter);
                foreach (var target in spec.GetterOverrides) typeBuilder.DefineMethodOverride(getter, target);
            }
            if (spec.Setter != null)
            {
                var setter = typeBuilder.DefineMethod(
                    "set_" + spec.Name,
                    AccessOf(spec.Setter) | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                    null, new[] { spec.PropertyType });
                var il = setter.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stfld, field);
                if (notifyMethod != null)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldstr, spec.Name);
                    il.Emit(OpCodes.Call, notifyMethod);
                }
                il.Emit(OpCodes.Ret);
                propertyBuilder.SetSetMethod(setter);
                foreach (var target in spec.SetterOverrides) typeBuilder.DefineMethodOverride(setter, target);
            }
        }

        var ctor = typeBuilder.DefineMethod(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            null, Type.EmptyTypes);
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, baseConstructor);
        ctorIl.Emit(OpCodes.Ret);

        try
        {
            return typeBuilder.CreateTypeInfo()!.AsType();
        }
        catch (Exception ex) when (ex is TypeLoadException or InvalidOperationException)
        {
            throw new AutoMapperMappingException(
                $"Cannot create a proxy for {destinationType.Name}: the generated proxy type failed to load ({ex.Message}). Map to a concrete type, or supply a construction function for this map.", ex);
        }
    }

    private static MethodBuilder DefineNotifyMethod(TypeBuilder typeBuilder, FieldBuilder eventField)
    {
        var method = typeBuilder.DefineMethod(
            "NotifyPropertyChanged",
            MethodAttributes.Private | MethodAttributes.HideBySig,
            null, new[] { typeof(string) });
        var il = method.GetILGenerator();
        var handler = il.DeclareLocal(typeof(PropertyChangedEventHandler));
        var end = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, eventField);
        il.Emit(OpCodes.Stloc, handler);
        il.Emit(OpCodes.Ldloc, handler);
        il.Emit(OpCodes.Brfalse_S, end);
        il.Emit(OpCodes.Ldloc, handler);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, typeof(PropertyChangedEventArgs).GetConstructor(new[] { typeof(string) })!);
        il.Emit(OpCodes.Callvirt, typeof(PropertyChangedEventHandler).GetMethod("Invoke")!);
        il.MarkLabel(end);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineEventAccessor(TypeBuilder typeBuilder, string name, FieldBuilder eventField, bool isAdd)
    {
        var method = typeBuilder.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
            null, new[] { typeof(PropertyChangedEventHandler) });
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, eventField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, isAdd
            ? typeof(Delegate).GetMethod("Combine", new[] { typeof(Delegate), typeof(Delegate) })!
            : typeof(Delegate).GetMethod("Remove", new[] { typeof(Delegate), typeof(Delegate) })!);
        il.Emit(OpCodes.Castclass, typeof(PropertyChangedEventHandler));
        il.Emit(OpCodes.Stfld, eventField);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodAttributes AccessOf(MethodInfo method) =>
        method.Attributes & MethodAttributes.MemberAccessMask;

    private static bool HasBody(MethodInfo method)
    {
        try { return method.GetMethodBody() != null; }
        catch { return false; }
    }

    private static List<Type> AllInterfaces(Type destinationType)
    {
        var result = new List<Type> { destinationType };
        var seen = new HashSet<Type> { destinationType };
        var queue = new Queue<Type>();
        queue.Enqueue(destinationType);
        while (queue.Count > 0)
        {
            foreach (var parent in queue.Dequeue().GetInterfaces())
                if (seen.Add(parent))
                {
                    result.Add(parent);
                    queue.Enqueue(parent);
                }
        }
        return result;
    }

    private static string ProxyTypeName(Type destinationType)
    {
        string full;
        if (destinationType.IsGenericType)
        {
            var definition = destinationType.GetGenericTypeDefinition();
            full = (definition.FullName ?? definition.Name) + "[" +
                string.Join(",", destinationType.GetGenericArguments().Select(a => a.FullName ?? a.Name)) + "]";
        }
        else
        {
            full = destinationType.FullName ?? destinationType.Name;
        }
        var sanitized = new StringBuilder();
        foreach (var ch in full)
            sanitized.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(destinationType.AssemblyQualifiedName ?? full)))[..12];
        return "Proxy_" + sanitized + "_" + hash;
    }
}

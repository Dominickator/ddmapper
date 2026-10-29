using System.Reflection;

namespace AutoMapper;

/// <summary>Invokes a converter object's <c>Convert</c> method, accepting
/// the shapes a converter may legitimately take, in preference order:
/// <c>Convert(source, destination, context)</c> (the interface shape),
/// <c>Convert(source, destination)</c> (no context), and
/// <c>Convert(source)</c> (source only). A method that exists but throws
/// from user code propagates its own exception; a candidate that simply
/// does not accept the supplied arguments is skipped for the next shape.
/// When no <c>Convert</c> shape accepts the arguments, a clear mapping
/// exception names the converter type instead of surfacing a reflection
/// error.</summary>
internal static class ConverterInvoker
{
    public static object? Invoke(object converter, object? source, object? destination, ResolutionContext context)
    {
        var candidates = converter.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "Convert")
            .OrderByDescending(m => m.GetParameters().Length)
            .ToList();
        foreach (var method in candidates)
        {
            var parameters = method.GetParameters();
            object?[]? args = parameters.Length switch
            {
                3 => new object?[] { source, destination, context },
                2 => new object?[] { source, destination },
                1 => new object?[] { source },
                _ => null,
            };
            if (args == null) continue;
            if (!ArgumentsFit(parameters, args)) continue;
            try
            {
                return method.Invoke(converter, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
            catch (ArgumentException)
            {
                // The shape looked compatible but the runtime types did
                // not fit; try the next Convert shape.
                continue;
            }
        }
        throw new AutoMapperMappingException(
            $"Converter {converter.GetType().Name} does not expose a usable Convert method for a value of type {source?.GetType().Name ?? "null"}. " +
            "Give it a Convert(source, destination, context), Convert(source, destination), or Convert(source) method.");
    }

    private static bool ArgumentsFit(ParameterInfo[] parameters, object?[] args)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            var value = args[i];
            if (value == null)
            {
                if (parameters[i].ParameterType.IsValueType && Nullable.GetUnderlyingType(parameters[i].ParameterType) == null)
                    return false;
                continue;
            }
            if (!parameters[i].ParameterType.IsAssignableFrom(value.GetType())) return false;
        }
        return true;
    }
}

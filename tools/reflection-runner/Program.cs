// Minimal reflection runner: executes every [Fact] method in DDMapper.Tests and
// reports PASS/FAIL. This is NOT `dotnet test` output; it exists because the
// vstest testhost cannot connect in this sandbox (see report). It runs the exact
// same test methods and xUnit asserts as the suite.
using System.Reflection;

var assembly = Assembly.Load("DDMapper.Tests");
int passed = 0, failed = 0;
var failures = new List<string>();
foreach (var type in assembly.GetTypes().OrderBy(t => t.FullName))
{
    if (type.IsAbstract || type.IsInterface) continue;
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(m => m.GetCustomAttributes().Any(a => a.GetType().FullName == "Xunit.FactAttribute"))
        .OrderBy(m => m.Name))
    {
        var name = $"{type.Name}.{method.Name}";
        try
        {
            var instance = Activator.CreateInstance(type)!;
            method.Invoke(instance, null);
            Console.WriteLine($"PASS {name}");
            passed++;
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            Console.WriteLine($"FAIL {name}: {inner.GetType().Name}: {inner.Message.Split('\n')[0]}");
            failures.Add($"{name}: {inner}");
            failed++;
        }
    }
}
Console.WriteLine($"RESULT passed={passed} failed={failed}");
foreach (var f in failures) Console.WriteLine("FAILED DETAIL: " + f.Split('\n')[0]);
return failed == 0 ? 0 : 1;

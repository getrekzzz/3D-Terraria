using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Strata.Core.Commands;

/// <summary>Finds the <see cref="ProbeSourceAttribute"/> classes of an assembly and runs their <c>Register</c> methods.</summary>
public static class ProbeSources
{
    private static readonly ConcurrentDictionary<Assembly, MethodInfo[]> Found = new();

    /// <summary>The probe sources declared in an assembly, in registration order (by full type name).</summary>
    public static IReadOnlyList<Type> In(Assembly assembly) => Array.ConvertAll(Methods(assembly), m => m.DeclaringType!);

    /// <summary>
    /// Registers every probe source of <paramref name="assembly"/> into <paramref name="ctx"/>, in full-type-name order
    /// (later sources see earlier probes; a fixed name registered twice throws). Each <c>Register</c>
    /// parameter receives the context or the first of <paramref name="services"/> of its type; a source asking for
    /// anything else throws, since it would silently lose its probes.
    /// </summary>
    public static void RegisterAll(CommandContext ctx, Assembly assembly, params object[] services)
    {
        foreach (var method in Methods(assembly))
        {
            var parameters = method.GetParameters();
            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                args[i] = Resolve(parameters[i].ParameterType, ctx, services) ?? throw new InvalidOperationException(
                    $"Probe source {method.DeclaringType!.FullName}.Register needs a {parameters[i].ParameterType.Name}, which this command host does not provide.");
            try
            {
                method.Invoke(null, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        }
    }

    /// <summary>The single <c>static void Register(…)</c> of a probe source; throws when the class breaks the contract.</summary>
    internal static MethodInfo RegisterMethod(Type source)
    {
        var methods = source.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == "Register").ToArray();
        bool isStatic = source.IsAbstract && source.IsSealed;
        if (!isStatic || methods.Length != 1 || methods[0].ReturnType != typeof(void) || methods[0].IsGenericMethodDefinition)
            throw new InvalidOperationException($"[ProbeSource] {source.FullName} must be a static class with exactly one static void Register(CommandContext ctx, …) method.");
        return methods[0];
    }

    private static MethodInfo[] Methods(Assembly assembly) => Found.GetOrAdd(assembly, a => a.GetTypes()
        .Where(t => t.IsDefined(typeof(ProbeSourceAttribute), inherit: false))
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .Select(RegisterMethod)
        .ToArray());

    private static object? Resolve(Type wanted, CommandContext ctx, object[] services)
    {
        if (wanted.IsInstanceOfType(ctx)) return ctx;
        foreach (var service in services)
            if (wanted.IsInstanceOfType(service)) return service;
        return null;
    }
}

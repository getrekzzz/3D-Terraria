namespace Strata.Core.Commands;

/// <summary>
/// Marks a static class that adds probes to a <see cref="CommandContext"/>, so a feature brings its own probe file
/// instead of editing <see cref="StandardProbes"/> or the engine command host. The class declares one
/// <c>static void Register(CommandContext ctx, …)</c>; further parameters are filled by type from the services the
/// host passes (<see cref="ProbeSources.RegisterAll"/>). Sources in Strata.Core are registered by every
/// <see cref="CommandContext"/>; sources in the game assembly by the Godot command host.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ProbeSourceAttribute : Attribute
{
}

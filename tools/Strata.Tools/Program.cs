using Strata.Tools.Commands;

namespace Strata.Tools;

/// <summary>Developer CLI entry point: <c>dotnet run --project tools/Strata.Tools -- &lt;command&gt; [options]</c>.</summary>
public static class Program
{
    private static readonly IToolCommand[] Commands =
    {
        new ManifestCommand(),
        new ValidateCommand(),
        new WorldgenReportCommand(),
        new MeshBenchCommand(),
    };

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        var command = Commands.FirstOrDefault(c => c.Name == args[0]);
        if (command == null)
        {
            Console.Error.WriteLine($"Unknown command '{args[0]}'.");
            PrintHelp();
            return 1;
        }

        var options = ToolOptions.Parse(args.Skip(1));
        var root = options.Get("root") ?? Strata.Core.Content.Loading.FileSystemDataSource.FindProjectRoot(Directory.GetCurrentDirectory());
        return command.Run(new ToolContext(root, options));
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Strata developer tools");
        foreach (var c in Commands) Console.WriteLine($"  {c.Name,-18} {c.Description}");
    }
}

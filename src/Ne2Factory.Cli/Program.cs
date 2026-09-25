using Ne2Factory.Cli;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.GapScout;

const string TopUsage = """
    Usage: ne2-factory <backlog|gap-scout> ...

    Subcommands:
      backlog     Work the GitHub issues backlog. See `ne2-factory backlog --help`.
      gap-scout   Scan for architecture gaps and file them as issues. See
                    `ne2-factory gap-scout --help`.
    """;

if (args.Length == 0)
{
    Console.WriteLine(TopUsage);
    return 0;
}

var ctx = ProjectContext.Resolve();
var rest = args.Skip(1).ToArray();

return args[0] switch
{
    "backlog" => BacklogCommand.Run(rest, ctx),
    "gap-scout" => GapScoutCommand.Run(rest, ctx),
    "-h" or "--help" => Print(TopUsage),
    _ => Unknown(args[0])
};

static int Print(string text) { Console.WriteLine(text); return 0; }

static int Unknown(string command)
{
    Console.Error.WriteLine($"Comando desconocido: {command}");
    Console.WriteLine(TopUsage);
    return 1;
}

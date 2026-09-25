using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Ne2Factory.Cli.Configuration;

// Loads a shell-style KEY=VALUE file (`.ne2-factory.env`) into IConfiguration,
// the .NET equivalent of what bash `source .ne2-factory.env` used to do.
internal sealed class EnvFileConfigurationSource(string path) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new EnvFileConfigurationProvider(path);
}

internal sealed class EnvFileConfigurationProvider(string path) : ConfigurationProvider
{
    public override void Load()
    {
        if (!File.Exists(path)) return;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var match = Regex.Match(line, @"^([A-Za-z_][A-Za-z0-9_]*)=(.*)$");
            if (!match.Success) continue;

            var key = match.Groups[1].Value;
            var value = match.Groups[2].Value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1];
            else if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
                value = value[1..^1];

            Data[key] = value;
        }
    }
}

internal static class EnvFileConfigurationExtensions
{
    public static IConfigurationBuilder AddEnvFile(this IConfigurationBuilder builder, string path) =>
        builder.Add(new EnvFileConfigurationSource(path));
}

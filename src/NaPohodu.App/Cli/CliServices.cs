using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NaPohodu.Core.Configuration;
using NaPohodu.Core.Pohoda;

namespace NaPohodu.App.Cli;

/// <summary>Závislosti příkazů CLI; v testech se nahrazují (konfigurace, falešný runner).</summary>
internal sealed class CliServices
{
    public required IConfiguration Configuration { get; init; }

    public required Func<PohodaOptions, ILoggerFactory, IPohodaXmlRunner> RunnerFactory { get; init; }

    public static CliServices CreateDefault() =>
        new()
        {
            Configuration = AppConfiguration.Build(),
            RunnerFactory = (options, loggerFactory) => new PohodaXmlRunner(options, loggerFactory.CreateLogger<PohodaXmlRunner>()),
        };
}

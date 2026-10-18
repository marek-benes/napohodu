using System.CommandLine;

namespace NaPohodu.App.Cli;

/// <summary>Kořenový příkaz <c>napohodu</c> se všemi podpříkazy.</summary>
internal static class NapohoduCli
{
    public static RootCommand Create(CliServices services)
    {
        var root = new RootCommand("NaPohodu: doplňková aplikace k účetnímu systému POHODA.")
        {
            CliOptions.Verbose,
        };

        root.Subcommands.Add(CustomersCommand.Create(services));
        root.Subcommands.Add(InvoicesCommand.Create(services));
        root.Subcommands.Add(FilingCommand.Create(services));
        root.Subcommands.Add(DocDirsCommand.Create(services));
        root.Subcommands.Add(AuditExportCommand.Create(services));
        root.Subcommands.Add(PodkladyCommand.Create(services));
        root.Subcommands.Add(ServeCommand.Create(services));
        root.Subcommands.Add(ConfigCommand.Create(services));
        return root;
    }
}

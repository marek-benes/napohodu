using System.Text;
using NaPohodu.App.Cli;

// Čeština v konzoli Windows (výchozí OEM 852) i při přesměrování do souboru.
Console.OutputEncoding = Encoding.UTF8;

var root = NapohoduCli.Create(CliServices.CreateDefault());
return await root.Parse(args).InvokeAsync();

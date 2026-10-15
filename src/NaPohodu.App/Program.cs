using System.CommandLine;

var root = new RootCommand("NaPohodu: doplňková aplikace k účetnímu systému POHODA.");

// Podpříkazy (customers, invoices, serve, …) přibudou v dalších krocích.

return await root.Parse(args).InvokeAsync();

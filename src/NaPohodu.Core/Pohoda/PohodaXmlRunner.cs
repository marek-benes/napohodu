using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using NaPohodu.Core.Configuration;
using NaPohodu.Core.Text;

namespace NaPohodu.Core.Pohoda;

/// <summary>
/// Spouští <c>Pohoda.exe /XML &lt;user&gt; &lt;password&gt; &lt;ini&gt;</c> nad dočasnými soubory.
/// Každé volání = start Pohody, přihlášení a otevření jednotky (sekundy). Vždy s timeoutem:
/// Pohoda může zobrazit modální dialog a viset, proces se pak ukončí.
/// Heslo je jen v argumentech procesu; nikdy se neloguje ani nedostane do výjimky.
/// </summary>
public sealed partial class PohodaXmlRunner(PohodaOptions options, ILogger<PohodaXmlRunner> logger) : IPohodaXmlRunner
{
    private const string InputFileName = "input.xml";
    private const string ResponseFileName = "response.xml";
    private const string IniFileName = "pohoda.ini";

    public async Task<PohodaXmlResponse> RunAsync(XDocument request, string? database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(options.ExePath))
        {
            throw new PohodaException($"Pohoda.exe nebyla nalezena na cestě: {options.ExePath}");
        }

        // Unikátní adresář pro každé volání (souběh více volání nesmí kolidovat).
        var workDir = Directory.CreateTempSubdirectory("napohodu-");
        try
        {
            var inputPath = Path.Combine(workDir.FullName, InputFileName);
            var responsePath = Path.Combine(workDir.FullName, ResponseFileName);
            var iniPath = Path.Combine(workDir.FullName, IniFileName);

            await WriteRequestAsync(request, inputPath, cancellationToken).ConfigureAwait(false);
            // Pohoda je ANSI aplikace; INI zapisujeme v jejím kódování kvůli cestám a názvům s diakritikou.
            await File.WriteAllTextAsync(iniPath, PohodaIniFile.Build(inputPath, responsePath, database), PohodaEncoding.Windows1250, cancellationToken)
                .ConfigureAwait(false);

            LogStarting(database ?? "(seznam jednotek)");
            var stopwatch = Stopwatch.StartNew();
            var exitCode = await RunProcessAsync(iniPath, cancellationToken).ConfigureAwait(false);
            LogFinished(exitCode, stopwatch.ElapsedMilliseconds);

            byte[]? bytes = File.Exists(responsePath)
                ? await File.ReadAllBytesAsync(responsePath, cancellationToken).ConfigureAwait(false)
                : null;

            if (PohodaExitCodes.IsError(exitCode))
            {
                throw new PohodaException(PohodaExitCodes.Describe(exitCode, TryReadNote(bytes))) { ExitCode = exitCode };
            }

            if (PohodaExitCodes.IsWarning(exitCode))
            {
                LogWarningExitCode(exitCode);
            }

            if (bytes is null)
            {
                throw new PohodaException("Pohoda nevytvořila soubor s odpovědí. Zkontrolujte přihlašovací údaje a cestu k Pohoda.exe.")
                {
                    ExitCode = exitCode,
                };
            }

            LogResponseSize(bytes.Length);
            return new PohodaXmlResponse(ParseResponse(bytes), bytes);
        }
        finally
        {
            DeleteWorkDir(workDir);
        }
    }

    private async Task<int> RunProcessAsync(string iniPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(options.ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(options.ExePath) ?? string.Empty,
        };
        startInfo.ArgumentList.Add("/XML");
        startInfo.ArgumentList.Add(options.Username);
        startInfo.ArgumentList.Add(options.Password);
        startInfo.ArgumentList.Add(iniPath);

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new PohodaException("Pohoda.exe se nepodařilo spustit.");
        }
        catch (Win32Exception ex)
        {
            // Zpráva Win32Exception argumenty (heslo) neobsahuje.
            throw new PohodaException($"Pohoda.exe se nepodařilo spustit: {ex.Message}", ex);
        }

        using (process)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.Timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                cancellationToken.ThrowIfCancellationRequested();
                throw new PohodaTimeoutException(
                    $"Pohoda neodpověděla do {options.Timeout.TotalSeconds:0} s a byla ukončena. " +
                    "Možná čeká na potvrzení dialogu (přihlášení, konverze dat, otevřená jednotka).");
            }

            return process.ExitCode;
        }
    }

    private void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Proces mezitím skončil.
        }
        catch (Win32Exception ex)
        {
            LogKillFailed(ex);
        }
    }

    private static async Task WriteRequestAsync(XDocument request, string path, CancellationToken cancellationToken)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            Async = true,
        };
        var stream = File.Create(path);
        await using (stream.ConfigureAwait(false))
        {
            var writer = XmlWriter.Create(stream, settings);
            await using (writer.ConfigureAwait(false))
            {
                await request.SaveAsync(writer, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal static XDocument ParseResponse(byte[] bytes)
    {
        try
        {
            return XDocument.Parse(PohodaXmlDecoding.Decode(bytes));
        }
        catch (XmlException ex)
        {
            throw new PohodaException($"Odpověď Pohody není platné XML: {ex.Message}", ex);
        }
    }

    private static string? TryReadNote(byte[]? bytes)
    {
        if (bytes is null)
        {
            return null;
        }

        try
        {
            return XDocument.Parse(PohodaXmlDecoding.Decode(bytes)).Root?.Attribute("note")?.Value;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private void DeleteWorkDir(DirectoryInfo workDir)
    {
        try
        {
            workDir.Delete(recursive: true);
        }
        catch (IOException ex)
        {
            LogCleanupFailed(workDir.FullName, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogCleanupFailed(workDir.FullName, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Pohoda XML API: spouštím Pohoda.exe /XML pro {Database}")]
    private partial void LogStarting(string database);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pohoda.exe skončila s kódem {ExitCode} za {ElapsedMs} ms")]
    private partial void LogFinished(int exitCode, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pohoda.exe skončila s varováním (kód {ExitCode})")]
    private partial void LogWarningExitCode(int exitCode);

    // Obsah odpovědi se neloguje (osobní údaje, velikost); jen velikost.
    [LoggerMessage(Level = LogLevel.Debug, Message = "Pohoda XML API: odpověď {Length} B")]
    private partial void LogResponseSize(int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nepodařilo se ukončit proces Pohoda.exe")]
    private partial void LogKillFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nepodařilo se smazat dočasný adresář {Path}")]
    private partial void LogCleanupFailed(string path, Exception exception);
}

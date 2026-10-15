namespace NaPohodu.Core.Pohoda;

/// <summary>
/// Chyba komunikace s Pohodou: spuštění, timeout, chybový kód nebo chybová odpověď.
/// Zpráva je určená pro uživatele (česky) a nikdy neobsahuje heslo.
/// </summary>
public class PohodaException : Exception
{
    public PohodaException()
    {
    }

    public PohodaException(string message)
        : base(message)
    {
    }

    public PohodaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Návratový kód <c>Pohoda.exe</c>, pokud je znám.</summary>
    public int? ExitCode { get; init; }
}

/// <summary>Pohoda nedoběhla v nastaveném čase a proces byl ukončen.</summary>
public class PohodaTimeoutException : PohodaException
{
    public PohodaTimeoutException()
    {
    }

    public PohodaTimeoutException(string message)
        : base(message)
    {
    }

    public PohodaTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

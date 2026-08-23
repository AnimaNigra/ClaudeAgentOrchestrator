namespace ClaudeOrchestrator.Services;

public enum PathError
{
    None,
    Invalid,
    Traversal,
    UnsupportedExtension,
    NotFound,
    TooLarge,
}

public readonly record struct LocalFilePathResult(string FullPath, PathError Error, string? Message)
{
    public bool Ok => Error == PathError.None;
}

/// <summary>
/// Jediná validace cesty k lokálnímu souboru pro celý backend. Používá ji
/// Reader (obsah + raw) i prohlížeč e-mailů; duplikovat ji je chyba.
/// Whitelist adresářů se zavádí záměrně nikde — orchestrátor už dnes čte
/// libovolné cesty a spouští agenty kdekoli na disku.
/// </summary>
public static class LocalFilePath
{
    public const long MaxFileBytes = 104_857_600; // 100 MB

    private static readonly char[] Separators =
        { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    public static LocalFilePathResult Validate(
        string? input,
        IReadOnlyCollection<string> allowedExtensions,
        long maxBytes = MaxFileBytes)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new LocalFilePathResult("", PathError.Invalid, "Cesta je povinná.");

        var trimmed = input.Trim();
        // Windows "Kopírovat jako cestu" obaluje hodnotu uvozovkami.
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            trimmed = trimmed[1..^1].Trim();

        if (trimmed.Length == 0)
            return new LocalFilePathResult("", PathError.Invalid, "Cesta je povinná.");

        // GetFullPath `..` sám sesbírá, takže se traversal musí odmítnout
        // z původního vstupu, ne z výsledku.
        foreach (var segment in trimmed.Split(Separators))
            if (segment == "..")
                return new LocalFilePathResult("", PathError.Traversal,
                    "Cesta nesmí obsahovat segment '..'.");

        string full;
        try { full = Path.GetFullPath(trimmed); }
        catch (Exception ex)
        {
            return new LocalFilePathResult("", PathError.Invalid, $"Neplatná cesta: {ex.Message}");
        }

        var ext = Path.GetExtension(full);
        if (!allowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return new LocalFilePathResult(full, PathError.UnsupportedExtension,
                $"Nepodporovaná přípona souboru. Očekává se {string.Join(" nebo ", allowedExtensions)}.");

        if (!File.Exists(full))
            return new LocalFilePathResult(full, PathError.NotFound, $"Soubor nenalezen: {full}");

        if (new FileInfo(full).Length > maxBytes)
            return new LocalFilePathResult(full, PathError.TooLarge,
                $"Soubor je příliš velký (max {maxBytes / 1024 / 1024} MB).");

        return new LocalFilePathResult(full, PathError.None, null);
    }
}

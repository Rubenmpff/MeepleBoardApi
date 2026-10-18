using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MeepleBoard.Services.Calculators;

/// <summary>
/// First protection for text other people can see (docs/proposta-bloqueio-comunidade.md in the app, section 5): a short
/// list of whole words and expressions in Portuguese and English, matched after lower-casing and removing accents, never
/// inside other words («computador», «disputa», «caralhota», «Scunthorpe», «classic» pass). It does not replace reports,
/// human review or handling of harassment and threats. Never applied to private notes, URLs or report texts.
/// </summary>
public static class ContentFilter
{
    public const string Message = "Este texto tem termos que não são permitidos. Revê-o antes de guardar.";

    // Whole words or expressions (accents removed, lower case). Inflections are listed explicitly instead of matching prefixes.
    // Left out on purpose because games use them innocently: «cabra» (goat), «fag», «rape», «go die», «vou-te matar», «I will kill you».
    // Leaving them out does not make threats acceptable: threats stay forbidden by the rules and are handled by reports and
    // moderation. This list is a first, incomplete protection; passing it does not mean a text is acceptable.
    static readonly string[] Terms =
    [
        // Portuguese
        "caralho", "caralhos", "foda-se", "fodase", "fodasse", "foder", "fodido", "fodida", "puta", "putas", "filho da puta", "filha da puta",
        "cabrao", "cabroes", "paneleiro", "paneleiros", "panasca", "maricas", "rabeta", "cona", "conas", "piroca",
        "cu do caralho", "vai-te foder", "vai te foder", "mata-te", "mata te", "preto de merda", "monhe", "cigano de merda",
        "atrasado mental", "mongoloide",
        // English
        "fuck", "fucking", "fucker", "motherfucker", "fuck you", "shithead", "bitch", "bitches", "cunt", "cunts", "whore", "slut",
        "nigger", "niggers", "nigga", "faggot", "faggots", "retard", "retarded", "kys", "kill yourself", "rapist",
    ];

    // A term is a run of letters/digits on both sides bounded by anything that is not a letter or digit.
    static readonly Regex Pattern = new(
        @"(?<![\p{L}\p{N}])(?:" + string.Join("|", Terms.OrderByDescending(t => t.Length).Select(Expression)) + @")(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Words of an expression may be joined by spaces, hyphens, dots or underscores, or written together («foda-se», «fodase»).
    static string Expression(string term) => string.Join(@"[\s\-_.]*", Regex.Split(term, "[ -]").Select(Regex.Escape));

    /// <summary>Lower case, accents removed, so «Cabrão» and «cabrao» are the same word.</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) plain.Append(c);
        return plain.ToString().Normalize(NormalizationForm.FormC);
    }

    public static bool IsAllowed(string? text) => string.IsNullOrWhiteSpace(text) || !Pattern.IsMatch(Normalize(text));

    /// <summary>Throws ArgumentException (400) with a neutral message when any text has a forbidden term.</summary>
    public static void Require(params string?[] texts)
    {
        if (texts.Any(t => !IsAllowed(t))) throw new ArgumentException(Message);
    }
}

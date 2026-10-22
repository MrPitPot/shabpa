using System.Text;

namespace Shabpa.Core;

/// <summary>One word of a typed command: <see cref="Raw"/> as the person wrote it (for titles shown back to them) and
/// <see cref="Norm"/>, the form every match runs on.</summary>
public sealed record CommandToken(string Raw, string Norm);

/// <summary>Turns free text in Persian, English or a mix of both into a list of comparable words. Pure logic.
/// Arabic ي/ك become Persian ی/ک, the zero-width non-joiner and diacritics go, Persian and Arabic digits become Latin,
/// letters typed three times in a row («خاموووش», "sleeeep") are squeezed, and «می خوام» / «میخوام» / «می‌خوام» all end
/// up as the same word.</summary>
public static class CommandText
{
    public const int MaxLength = 300;

    private const char Zwnj = '‌';

    /// <summary>The character-level clean-up, with the word breaks kept (ZWNJ becomes nothing, so «می‌خوام» is one word).</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.Length > MaxLength) text = text[..MaxLength];
        var sb = new StringBuilder(text.Length);
        foreach (var raw in text)
        {
            var c = raw switch
            {
                'ي' or 'ى' or 'ئ' => 'ی',
                'ك' => 'ک',
                'ة' => 'ه',
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ؤ' => 'و',
                'ۀ' => 'ه',
                >= '۰' and <= '۹' => (char)('0' + (raw - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (raw - '٠')),
                '٫' => '.',
                '٬' => ',',
                '٪' => '%',
                '،' or '؛' => ',',
                '؟' => '?',
                _ => char.ToLowerInvariant(raw),
            };
            // diacritics, tatweel, the joiners and other invisible marks
            if (c is >= 'ً' and <= 'ٟ' or 'ٰ' or 'ـ' or Zwnj or '‍' or '‎' or '‏' or '﻿') continue;
            if (char.IsControl(c)) c = ' ';
            sb.Append(c);
        }
        return Squeeze(sb.ToString());
    }

    /// <summary>Three or more of the same letter in a row: one for Persian letters (they are never doubled in writing),
    /// two for Latin ("sleeeep" → "sleep", "soooon" → "soon").</summary>
    private static string Squeeze(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var run = 1;
            while (i + run < text.Length && text[i + run] == c) run++;
            if (run >= 3 && char.IsLetter(c))
            {
                sb.Append(c);
                if (c < 0x0600) sb.Append(c);
            }
            else sb.Append(c, run);
            i += run - 1;
        }
        return sb.ToString();
    }

    private static bool IsBreak(char c) => char.IsWhiteSpace(c) || c is ',' or '،' or '؟' or '؛' or '!' or '?' or ';' or '"' or '(' or ')' or '«' or '»' or '“' or '”' or '[' or ']' or '…';

    /// <summary>Splits the text into words. A dot or colon only stays inside a word when digits surround it
    /// ("2:30", "1.5"); everywhere else it ends the word.</summary>
    public static List<CommandToken> Tokenize(string? text)
    {
        var tokens = new List<CommandToken>();
        if (string.IsNullOrWhiteSpace(text)) return tokens;
        if (text.Length > MaxLength) text = text[..MaxLength];
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var end = i == text.Length || IsBreak(text[i]) || (text[i] is '.' or ':' or '-' or '/' && !BetweenDigits(text, i));
            if (!end)
            {
                if (start < 0) start = i;
                continue;
            }
            if (start >= 0) Add(tokens, text[start..i]);
            start = -1;
        }
        return Join(tokens);
    }

    private static bool BetweenDigits(string text, int i) =>
        i > 0 && i < text.Length - 1 && IsDigit(text[i - 1]) && IsDigit(text[i + 1]);

    private static bool IsDigit(char c) => c is >= '0' and <= '9' or >= '۰' and <= '۹' or >= '٠' and <= '٩';

    private static void Add(List<CommandToken> tokens, string raw)
    {
        var norm = Normalize(raw).Trim();
        if (norm.Length == 0) return;
        // "2pm", "30min", "۲۰دقیقه": a number glued to a word is two words
        var split = 0;
        while (split < norm.Length && (char.IsAsciiDigit(norm[split]) || (split > 0 && norm[split] is '.' or ':' && split + 1 < norm.Length && char.IsAsciiDigit(norm[split + 1])))) split++;
        if (split > 0 && split < norm.Length && norm[split..].All(char.IsLetter))
        {
            tokens.Add(new CommandToken(raw, norm[..split]));
            tokens.Add(new CommandToken("", norm[split..]));
            return;
        }
        // «یکساعت»، «نیم‌ساعت»، «یه‌ربع»: a number word glued to a unit
        foreach (var unit in GluedUnits)
        {
            if (norm.Length <= unit.Length || !norm.EndsWith(unit, StringComparison.Ordinal)) continue;
            var head = norm[..^unit.Length];
            if (!GluedNumbers.Contains(head)) continue;
            tokens.Add(new CommandToken(raw, head));
            tokens.Add(new CommandToken("", unit));
            return;
        }
        tokens.Add(new CommandToken(raw, norm));
    }

    private static readonly string[] GluedUnits = ["ساعت", "دقیقه", "ربع"];
    private static readonly HashSet<string> GluedNumbers = ["یک", "یه", "نیم", "دو", "سه", "چهار", "پنج", "شش", "ده", "بیست", "سی"];

    /// <summary>Spoken Persian splits verbs that are written as one word: «می خوام» → «میخوام», «نمی شه» → «نمیشه»,
    /// and two-part weekdays «سه شنبه» → «سهشنبه».</summary>
    private static List<CommandToken> Join(List<CommandToken> tokens)
    {
        var joined = new List<CommandToken>(tokens.Count);
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (i + 1 < tokens.Count)
            {
                var next = tokens[i + 1];
                var glue = t.Norm is "می" or "نمی" && IsPersianWord(next.Norm)
                           || t.Norm is "یک" or "سه" or "پنج" && next.Norm == "شنبه"
                           || t.Norm is "پس" && next.Norm is "فردا"
                           || t.Norm is "بعد" && next.Norm is "ازظهر"
                           || t.Norm is "بعداز" && next.Norm is "ظهر";
                if (glue)
                {
                    joined.Add(new CommandToken(t.Raw + Zwnj + next.Raw, t.Norm + next.Norm));
                    i++;
                    continue;
                }
            }
            joined.Add(t);
        }
        return joined;
    }

    private static bool IsPersianWord(string s) => s.Length > 0 && s[0] is >= '؀' and <= 'ۿ';

    /// <summary>Damerau–Levenshtein distance, stopping early once it passes <paramref name="max"/>.</summary>
    public static int Distance(string a, string b, int max = 2)
    {
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var best = int.MaxValue;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, d[i - 2, j - 2] + 1);
                d[i, j] = v;
                best = Math.Min(best, v);
            }
            if (best > max) return max + 1;
        }
        return d[a.Length, b.Length];
    }

    /// <summary>How far a word may be from a keyword and still count as a typo of it: none for short words
    /// (a wrong letter in «قفل» is another word), one letter from five letters on (six in Persian), two from nine (ten).</summary>
    public static int Tolerance(string keyword)
    {
        // Persian words change meaning with one letter at the end («خوابم» is not «خوابش»), so they need a longer word
        var persian = keyword.Length > 0 && keyword[0] is >= '\u0600' and <= '\u06FF';
        var from = persian ? 6 : 5;
        return keyword.Length >= from + 4 ? 2 : keyword.Length >= from ? 1 : 0;
    }
}

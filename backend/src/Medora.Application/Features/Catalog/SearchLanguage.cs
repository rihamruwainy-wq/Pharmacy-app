using System.Globalization;
using System.Text;

namespace Medora.Application.Features.Catalog;

/// <summary>A parsed search: what to match in product text, and which categories a symptom implies.</summary>
public sealed record SearchPlan(IReadOnlyList<IReadOnlyList<string>> TermGroups, IReadOnlyList<string> Categories)
{
    /// <summary>Each group is OR-ed (synonyms of one word); groups are AND-ed (every word must match).</summary>
    public bool IsEmpty => TermGroups.Count == 0 && Categories.Count == 0;
}

/// <summary>
/// Pure query understanding for pharmacy search, in English and Arabic:
/// normalisation (case, diacritics, Arabic letter variants), brand/generic synonyms
/// (Tylenol = Panadol = acetaminophen = paracetamol = باراسيتامول), symptom-to-category mapping
/// ("headache", "صداع" -> pain relief) and a typo corrector over the catalogue's own vocabulary.
/// Data lives in two dictionaries below so the client can extend them without touching the logic.
/// </summary>
public static class SearchLanguage
{
    /// <summary>Any of these words -> the canonical term(s) actually present in product data.</summary>
    private static readonly Dictionary<string, string[]> Synonyms = Build(new()
    {
        ["paracetamol"] = ["acetaminophen", "tylenol", "panadol", "باراسيتامول", "بارسيتامول", "بنادول", "تايلينول", "اسيتامينوفين"],
        ["ibuprofen"] = ["advil", "motrin", "brufen", "ايبوبروفين", "ابيوبروفين", "بروفين", "ادفيل"],
        ["aspirin"] = ["asa", "acetylsalicylic", "اسبرين", "أسبرين", "اسبيرين"],
        ["naproxen"] = ["aleve", "نابروكسين", "اليف"],
        ["tramadol"] = ["ترامادول"],
        ["pseudoephedrine"] = ["sudafed", "سودافيد", "سودوايفيدرين"],
        ["robitussin"] = ["dextromethorphan", "guaifenesin", "روبيتوسين"],
        ["cetirizine"] = ["zyrtec", "سيتريزين", "زيرتك"],
        ["loratadine"] = ["claritin", "لوراتادين", "كلاريتين"],
        ["omeprazole"] = ["prilosec", "losec", "اوميبرازول", "أوميبرازول", "بريلوسك"],
        ["loperamide"] = ["imodium", "لوبيراميد", "ايموديوم"],
        ["vitamin"] = ["vitamins", "فيتامين", "فيتامينات"],
        ["multivitamin"] = ["centrum", "سنتروم"],
        ["ferrous"] = ["iron", "حديد"],
        ["atorvastatin"] = ["lipitor", "اتورفاستاتين", "ليبيتور"],
        ["simvastatin"] = ["zocor", "سيمفاستاتين"],
        ["lisinopril"] = ["zestril", "ليسينوبريل"],
        ["amlodipine"] = ["norvasc", "املوديبين"],
        ["warfarin"] = ["coumadin", "وارفارين"],
        ["clopidogrel"] = ["plavix", "كلوبيدوقريل", "بلافيكس"],
        ["metformin"] = ["glucophage", "ميتفورمين", "جلوكوفاج"],
        ["amoxicillin"] = ["amoxil", "amoxycillin", "اموكسيسيلين", "اموكسيل"],
        ["clarithromycin"] = ["biaxin", "klacid", "كلاريثروميسين", "كلاسيد"],
        ["ciprofloxacin"] = ["cipro", "سيبروفلوكساسين", "سيبرو"],
        ["sertraline"] = ["zoloft", "سيرترالين", "زولوفت"],
        ["salbutamol"] = ["albuterol", "ventolin", "سالبوتامول", "فنتولين", "بخاخ"],
        ["hydrocortisone"] = ["cortizone", "هيدروكورتيزون"],
        ["clotrimazole"] = ["lotrimin", "canesten", "كلوتريمازول"],
        ["pedialyte"] = ["ors", "rehydration", "محلول", "جفاف"],
    });

    /// <summary>Symptom / need words -> category ids (customers describe problems, not molecules).</summary>
    private static readonly Dictionary<string, string[]> SymptomCategories = Build(new()
    {
        ["pain-relief"] = ["pain", "headache", "migraine", "ache", "fever", "temperature", "toothache", "backache", "painkiller", "ألم", "الم", "صداع", "حرارة", "حراره", "حمى", "مسكن", "مسكنات", "وجع"],
        ["cold-flu"] = ["cold", "flu", "cough", "congestion", "sore", "throat", "blocked", "زكام", "برد", "انفلونزا", "سعال", "كحة", "كحه", "احتقان"],
        ["allergy"] = ["allergy", "allergic", "hayfever", "sneezing", "antihistamine", "حساسية", "حساسيه", "عطس"],
        ["digestive"] = ["heartburn", "reflux", "acid", "indigestion", "stomach", "diarrhea", "diarrhoea", "حموضة", "حموضه", "معدة", "معده", "اسهال", "إسهال", "هضم"],
        ["vitamins"] = ["supplement", "supplements", "immunity", "anemia", "anaemia", "مكمل", "مكملات", "مناعة", "انيميا", "فقر"],
        ["heart"] = ["heart", "pressure", "bp", "hypertension", "cholesterol", "blood", "قلب", "ضغط", "كوليسترول", "دم"],
        ["diabetes"] = ["diabetes", "diabetic", "sugar", "glucose", "سكر", "سكري", "السكري"],
        ["antibiotics"] = ["antibiotic", "antibiotics", "infection", "مضاد", "حيوي", "التهاب", "عدوى"],
        ["mental-health"] = ["depression", "anxiety", "mood", "اكتئاب", "قلق", "نفسية"],
        ["respiratory"] = ["asthma", "breathing", "wheeze", "inhaler", "ربو", "تنفس", "ضيق"],
        ["skin"] = ["skin", "rash", "itch", "itchy", "itching", "eczema", "fungal", "athlete", "جلد", "طفح", "حكة", "حكه", "اكزيما", "فطريات"],
        ["baby"] = ["baby", "babies", "child", "children", "kids", "infant", "طفل", "اطفال", "أطفال", "رضيع", "رضع", "بالطفل", "للطفل", "للاطفال"],
    });

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "for", "the", "a", "an", "and", "of", "with", "to", "my", "me", "i", "have", "medicine", "medicines", "tablets", "tablet",
        "relief", "remedy", "care", "في", "من", "على", "عن", "دواء", "ادوية", "أدوية", "علاج", "حبوب", "لل", "ل", "و", "عندي", "عنايه", "العنايه",
    };

    /// <summary>Lower-case, strip Latin/Arabic diacritics and tatweel, and unify Arabic letter variants.</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormKD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark || ch == 'ـ') continue; // harakat, accents, tatweel
            sb.Append(ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ة' => 'ه',
                'ى' => 'ي',
                'ؤ' => 'و',
                'ئ' => 'ي',
                _ => char.ToLowerInvariant(ch),
            });
        }
        return sb.ToString();
    }

    public static IReadOnlyList<string> Words(string text) =>
        Normalize(text).Split((char[])[' ', '\t', ',', '.', '-', '/', '،', '(', ')', '+', '\'', '"'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.StartsWith("ال", StringComparison.Ordinal) && w.Length > 3 && !Synonyms.ContainsKey(w) && !SymptomCategories.ContainsKey(w) ? w[2..] : w) // Arabic definite article
            .Where(w => w.Length >= 2 && !StopWords.Contains(w))
            .Take(6)
            .ToList();

    public static SearchPlan Plan(string query)
    {
        var groups = new List<IReadOnlyList<string>>();
        var categories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in Words(query))
        {
            var matchedSymptom = SymptomCategories.TryGetValue(word, out var cats);
            if (matchedSymptom) categories.UnionWith(cats!);
            var terms = Synonyms.TryGetValue(word, out var canon) ? canon.Append(word).Distinct(StringComparer.Ordinal).ToList() : [word];
            // A pure symptom word ("headache") filters by category instead of requiring the word in product text.
            if (!matchedSymptom || Synonyms.ContainsKey(word)) groups.Add(terms);
        }
        return new SearchPlan(groups, [.. categories]);
    }

    /// <summary>
    /// Typo correction against the catalogue's vocabulary (plus all known synonyms): each unknown word is
    /// replaced by the closest known word within an edit distance that scales with word length.
    /// Returns null when nothing needed (or could be) corrected.
    /// </summary>
    public static string? Correct(string query, IReadOnlyCollection<string> vocabulary)
    {
        var known = new HashSet<string>(vocabulary.Select(Normalize), StringComparer.Ordinal);
        known.UnionWith(Synonyms.Keys);
        known.UnionWith(SymptomCategories.Keys);
        var changed = false;
        var words = Words(query).Select(w =>
        {
            if (known.Contains(w) || w.Length < 4) return w;
            var max = w.Length <= 5 ? 1 : 2;
            var best = known.Where(k => Math.Abs(k.Length - w.Length) <= max)
                .Select(k => (Word: k, Distance: Distance(w, k)))
                .Where(x => x.Distance <= max)
                .OrderBy(x => x.Distance).ThenBy(x => x.Word, StringComparer.Ordinal)
                .FirstOrDefault();
            if (best.Word is null) return w;
            changed = true;
            return best.Word;
        }).ToList();
        return changed ? string.Join(' ', words) : null;
    }

    /// <summary>Damerau-Levenshtein (optimal string alignment): transpositions like "ibuporfen" count as one edit.</summary>
    internal static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }
        return d[a.Length, b.Length];
    }

    /// <summary>Inverts "canonical -> aliases" into "any word -> canonical terms", normalising every key.</summary>
    private static Dictionary<string, string[]> Build(Dictionary<string, string[]> source)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (canonical, aliases) in source)
        {
            foreach (var word in aliases.Append(canonical).Select(Normalize))
            {
                if (!map.TryGetValue(word, out var set)) map[word] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(Normalize(canonical));
            }
        }
        return map.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal);
    }
}

namespace Medora.Domain.Knowledge;

/// <summary>
/// One section of a drug monograph (indications, dosing, interactions…). This is the RAG corpus
/// for the pharmacist copilot: each section is a retrievable, citable chunk.
/// </summary>
public sealed class MonographSection
{
    private MonographSection() { }

    public MonographSection(string id, string genericName, string drugTitle, string section, string content, string source)
    {
        Id = id; GenericName = genericName; DrugTitle = drugTitle; Section = section; Content = content; Source = source;
    }

    public string Id { get; private set; } = default!;
    public string GenericName { get; private set; } = default!;
    public string DrugTitle { get; private set; } = default!;
    public string Section { get; private set; } = default!;
    public string Content { get; private set; } = default!;
    /// <summary>Provenance shown with every citation (e.g. "Medora demo formulary v1 - verify against local SmPC").</summary>
    public string Source { get; private set; } = default!;

    public string Title => $"{DrugTitle} - {Section}";
}

public enum InteractionSeverity
{
    Minor = 0,
    Moderate = 1,
    Major = 2,
    Contraindicated = 3,
}

/// <summary>
/// Curated drug-drug interaction pair. Interaction screening is DETERMINISTIC (a table lookup), never
/// left to a language model: the model may explain an alert, but cannot create or suppress one.
/// Pairs are stored with the two generic names in ordinal order (A &lt; B) so each pair exists once.
/// </summary>
public sealed class DrugInteraction
{
    private DrugInteraction() { }

    public DrugInteraction(string drugA, string drugB, InteractionSeverity severity, string effect, string management)
    {
        var (a, b) = Order(drugA, drugB);
        DrugA = a; DrugB = b; Severity = severity; Effect = effect; Management = management;
    }

    public string DrugA { get; private set; } = default!;
    public string DrugB { get; private set; } = default!;
    public InteractionSeverity Severity { get; private set; }
    public string Effect { get; private set; } = default!;
    public string Management { get; private set; } = default!;

    public static (string A, string B) Order(string x, string y)
    {
        var a = x.Trim().ToLowerInvariant();
        var b = y.Trim().ToLowerInvariant();
        return string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
    }
}

/// <summary>Pure interaction screen over a set of generic names (order lines + the patient's recent medicines).</summary>
public static class InteractionScreen
{
    public static IReadOnlyList<DrugInteraction> Find(IEnumerable<string> genericNames, IEnumerable<DrugInteraction> table) =>
        Find(genericNames, [], table);

    /// <summary>
    /// Pairs among <paramref name="orderDrugs"/>, and between an order drug and a <paramref name="currentMedicines"/> drug.
    /// History-vs-history pairs are deliberately NOT reported: the patient is already on both, and repeating
    /// them on every order trains pharmacists to ignore alerts (alert fatigue).
    /// </summary>
    public static IReadOnlyList<DrugInteraction> Find(IEnumerable<string> orderDrugs, IEnumerable<string> currentMedicines, IEnumerable<DrugInteraction> table)
    {
        var order = Normalize(orderDrugs);
        var all = order.Concat(Normalize(currentMedicines)).Distinct(StringComparer.Ordinal).ToArray();
        var inOrder = new HashSet<string>(order, StringComparer.Ordinal);
        var pairs = new HashSet<(string, string)>();
        for (var i = 0; i < all.Length; i++)
        {
            for (var j = i + 1; j < all.Length; j++)
            {
                if (inOrder.Contains(all[i]) || inOrder.Contains(all[j])) pairs.Add(DrugInteraction.Order(all[i], all[j]));
            }
        }
        return table.Where(t => pairs.Contains((t.DrugA, t.DrugB))).OrderByDescending(t => t.Severity).ThenBy(t => t.DrugA, StringComparer.Ordinal).ToList();
    }

    private static string[] Normalize(IEnumerable<string> names) =>
        names.Select(n => n.Trim().ToLowerInvariant()).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
}

public sealed record AllergyAlert(string DeclaredAllergy, string DrugClass, string GenericName);

/// <summary>
/// Deterministic allergy screen: maps the customer's free-text declared allergies to drug classes and
/// flags order items in those classes (e.g. "Penicillin (rash)" vs amoxicillin). Cross-sensitivity
/// judgements (e.g. cephalosporins) stay with the pharmacist.
/// </summary>
public static class AllergyScreen
{
    private static readonly Dictionary<string, (string Class, string[] Keywords, string[] Members)> Classes = new(StringComparer.Ordinal)
    {
        ["penicillin"] = ("penicillins", ["penicillin", "amoxicillin", "amoxycillin", "ampicillin", "augmentin", "flucloxacillin"], ["amoxicillin", "ampicillin", "flucloxacillin", "phenoxymethylpenicillin", "co-amoxiclav"]),
        ["nsaid"] = ("NSAIDs", ["nsaid", "ibuprofen", "naproxen", "aspirin", "diclofenac", "anti-inflammator"], ["ibuprofen", "naproxen", "aspirin", "diclofenac"]),
        ["macrolide"] = ("macrolides", ["macrolide", "erythromycin", "clarithromycin", "azithromycin"], ["clarithromycin", "erythromycin", "azithromycin"]),
        ["fluoroquinolone"] = ("fluoroquinolones", ["quinolone", "ciprofloxacin", "levofloxacin"], ["ciprofloxacin", "levofloxacin"]),
        ["sulfonamide"] = ("sulfonamides", ["sulfa", "sulpha", "sulfonamide", "co-trimoxazole"], ["sulfamethoxazole", "co-trimoxazole"]),
        ["statin"] = ("statins", ["statin"], ["atorvastatin", "simvastatin", "rosuvastatin", "pravastatin"]),
        ["ace"] = ("ACE inhibitors", ["ace inhibitor", "lisinopril", "ramipril", "enalapril"], ["lisinopril", "ramipril", "enalapril"]),
        ["opioid"] = ("opioids", ["opioid", "codeine", "morphine", "tramadol"], ["tramadol", "codeine", "morphine"]),
    };

    public static IReadOnlyList<AllergyAlert> Find(string? declaredAllergies, IEnumerable<string> orderDrugs)
    {
        if (string.IsNullOrWhiteSpace(declaredAllergies)) return [];
        var text = declaredAllergies.ToLowerInvariant();
        var drugs = orderDrugs.Select(d => d.Trim().ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var alerts = new List<AllergyAlert>();
        foreach (var (_, (cls, keywords, members)) in Classes)
        {
            if (!keywords.Any(k => text.Contains(k, StringComparison.Ordinal))) continue;
            alerts.AddRange(members.Where(drugs.Contains).Select(m => new AllergyAlert(declaredAllergies.Trim(), cls, m)));
        }
        return alerts.DistinctBy(a => a.GenericName).ToList();
    }
}

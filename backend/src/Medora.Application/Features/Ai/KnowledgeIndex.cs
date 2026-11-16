using System.Globalization;
using System.Text;

namespace Medora.Application.Features.Ai;

public sealed record KnowledgeChunk(string Id, string GenericName, string Title, string Content, string Provenance);

/// <summary>
/// In-memory Okapi BM25 index over the drug-monograph sections (the RAG retriever).
/// Why lexical BM25 rather than embeddings here: pharmacist questions are dense with exact drug names,
/// doses and terms ("warfarin", "QT", "eGFR") where lexical matching is precise; the corpus is small
/// (thousands of sections) so the whole index fits in memory and queries take microseconds; and it adds
/// no second model/provider to operate. Retrieval is also BOOSTED toward drugs present in the order under
/// review, so the copilot grounds itself in the case first. Swap in hybrid (BM25 + vectors) behind the
/// same <see cref="Search"/> signature when the corpus grows beyond curated monographs.
/// Immutable after construction: safe to share across threads.
/// </summary>
public sealed class KnowledgeIndex
{
    private const double K1 = 1.2;
    private const double B = 0.75;
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a","an","and","are","as","at","be","by","can","do","does","for","from","how","i","if","in","is","it","its","of","on","or",
        "should","that","the","this","to","was","what","when","which","who","why","with","you","your","patient","take","taking",
    };

    private readonly KnowledgeChunk[] _chunks;
    private readonly Dictionary<string, int>[] _termFreqs;
    private readonly int[] _lengths;
    private readonly Dictionary<string, double> _idf;
    private readonly double _avgLength;

    public KnowledgeIndex(IEnumerable<KnowledgeChunk> chunks)
    {
        _chunks = chunks.ToArray();
        _termFreqs = new Dictionary<string, int>[_chunks.Length];
        _lengths = new int[_chunks.Length];
        var docFreq = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < _chunks.Length; i++)
        {
            // Title + generic name are indexed with the body so "warfarin interactions" hits the right section.
            var tokens = Tokenize($"{_chunks[i].Title} {_chunks[i].GenericName} {_chunks[i].Content}");
            _lengths[i] = tokens.Count;
            var tf = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var t in tokens) tf[t] = tf.GetValueOrDefault(t) + 1;
            _termFreqs[i] = tf;
            foreach (var t in tf.Keys) docFreq[t] = docFreq.GetValueOrDefault(t) + 1;
        }
        _avgLength = _chunks.Length == 0 ? 0 : _lengths.Average();
        _idf = docFreq.ToDictionary(kv => kv.Key, kv => Math.Log(1 + ((_chunks.Length - kv.Value + 0.5) / (kv.Value + 0.5))), StringComparer.Ordinal);
    }

    public int Count => _chunks.Length;

    /// <param name="query">Free-text question.</param>
    /// <param name="caseDrugs">Generic names in the case under review; their sections get a score boost.</param>
    /// <param name="take">Max chunks to return.</param>
    public IReadOnlyList<KnowledgeChunk> Search(string query, IReadOnlyCollection<string> caseDrugs, int take = 6)
    {
        var terms = Tokenize(query).Distinct(StringComparer.Ordinal).ToList();
        var boost = new HashSet<string>(caseDrugs.Select(d => d.ToLowerInvariant()), StringComparer.Ordinal);
        var scored = new List<(int Index, double Score)>();
        for (var i = 0; i < _chunks.Length; i++)
        {
            double score = 0;
            foreach (var term in terms)
            {
                if (!_termFreqs[i].TryGetValue(term, out var f) || !_idf.TryGetValue(term, out var idf)) continue;
                score += idf * (f * (K1 + 1)) / (f + (K1 * (1 - B + (B * _lengths[i] / _avgLength))));
            }
            if (boost.Contains(_chunks[i].GenericName)) score = (score * 1.5) + 0.5;
            if (score > 0) scored.Add((i, score));
        }
        return scored.OrderByDescending(s => s.Score).ThenBy(s => _chunks[s.Index].Id, StringComparer.Ordinal).Take(Math.Clamp(take, 1, 12)).Select(s => _chunks[s.Index]).ToList();
    }

    internal static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        foreach (var raw in text.Normalize(NormalizationForm.FormKD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark) continue;
            var c = char.ToLowerInvariant(raw);
            if (char.IsLetterOrDigit(c)) { sb.Append(c); continue; }
            Flush();
        }
        Flush();
        return tokens;

        void Flush()
        {
            if (sb.Length == 0) return;
            var t = sb.ToString();
            sb.Clear();
            if (t.Length < 2 || StopWords.Contains(t)) return;
            // Light stemming: plural "s" (interactions -> interaction), keeps "dose"/"doses" together.
            if (t.Length > 4 && t.EndsWith('s') && !t.EndsWith("ss", StringComparison.Ordinal)) t = t[..^1];
            tokens.Add(t);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentRagMcpServer.Interfaces;
using DocumentRagMcpServer.Models;

namespace DocumentRagMcpServer.Services
{
    /// <summary>
    /// BM25 search service with layered scoring:
    ///   1. BM25 on section body (TF * IDF, length-normalised)
    ///   2. Title match bonus
    ///   3. Exact phrase bonus
    ///   4. Keyword list match bonus
    /// </summary>
    public class SearchService : ISearchService
    {
        private const double K1 = 1.5;
        private const double B = 0.75;

        private static readonly Regex _tokeniseRegex = new(@"\W+", RegexOptions.Compiled);
        private static readonly Regex _controlCharsRegex = new(@"[\x00-\x08\x0B\x0C\x0E-\x1F]", RegexOptions.Compiled);

        private readonly IDocumentRepository _repository;
        private readonly Dictionary<string, List<SearchResult>> _cache = new();
        private const int CACHE_SIZE = 50;

        public SearchService(IDocumentRepository repository) => _repository = repository;

        public List<SearchResult> Search(string query, DocumentIndex index, string? documentId = null, int limit = 5)
        {
            limit = Math.Clamp(limit, 1, 20);
            var cacheKey = (string.IsNullOrEmpty(documentId) ? query : $"{documentId}:{query}").ToLowerInvariant() + $":{limit}";
            if (_cache.TryGetValue(cacheKey, out var cached))
                return cached;

            var terms = Tokenise(query);
            if (terms.Count == 0) return new List<SearchResult>();

            var allSections = _repository.GetAllSectionsWithIds(index);

            // Scope to document if specified; IDF always computed from full corpus
            var scoringSections = string.IsNullOrEmpty(documentId)
                ? allSections
                : allSections.Where(s => s.section.DocumentId == documentId).ToList();

            if (scoringSections.Count == 0) return new List<SearchResult>();

            // Build a map of section IDs to track adjacency and hierarchy
            var sectionMap = new Dictionary<string, (int index, string docId)>();
            for (int i = 0; i < scoringSections.Count; i++)
                sectionMap[scoringSections[i].id] = (i, scoringSections[i].section.DocumentId ?? "");

            // Build lowercase content map once — avoids per-term per-section allocations
            var lowerContentMap = new Dictionary<string, string>(allSections.Count);
            for (int i = 0; i < allSections.Count; i++)
                lowerContentMap[allSections[i].id] = allSections[i].section.Content.ToLowerInvariant();

            var idf = terms.ToDictionary(t => t, t => ComputeIdf(t, allSections, lowerContentMap));
            var avgLen = scoringSections.Average(s => (double)s.section.Content.Length);

            var results = new List<SearchResult>();
            foreach (var (section, id, type) in scoringSections)
            {
                var contentLower = lowerContentMap[id];
                var score = ScoreSection(section, contentLower, terms, idf, avgLen);
                if (score <= 0) continue;

                var currentIndex = sectionMap[id].index;
                var prevId = currentIndex > 0 ? scoringSections[currentIndex - 1].id : null;
                var nextId = currentIndex < scoringSections.Count - 1 ? scoringSections[currentIndex + 1].id : null;

                results.Add(new SearchResult
                {
                    Id = id,
                    Title = section.Title,
                    Type = type,
                    DocumentId = section.DocumentId,
                    RelevanceScore = (int)(score * 100),
                    Preview = GetContextPreview(section.Content, contentLower, terms),
                    MatchCount = CountTermHits(contentLower, terms),
                    Summary = section.Summary,
                    HeadingLevel = section.HeadingLevel,
                    ParentTitle = section.ParentTitle,
                    PreviousSectionId = prevId,
                    NextSectionId = nextId,
                    HierarchyPath = BuildHierarchyPath(section)
                });
            }

            results.Sort((a, b) => b.RelevanceScore.CompareTo(a.RelevanceScore));

            var top = results.Take(limit).ToList();
            _cache[cacheKey] = top;
            EvictCache();
            return top;
        }

        public void ClearCache() => _cache.Clear();

        // --- Hierarchy ---

        private string BuildHierarchyPath(DocumentSection section)
        {
            if (section.HeadingLevel == 0)
                return "Document Content";

            var path = section.ParentTitle ?? "Root";
            if (!string.IsNullOrEmpty(section.Title) && section.Title != section.ParentTitle)
                path += " > " + section.Title;

            return path;
        }

        // --- Scoring ---

        private double ScoreSection(
            DocumentSection section,
            string contentLower,
            List<string> terms,
            Dictionary<string, double> idf,
            double avgLen)
        {
            var titleLower = section.Title.ToLowerInvariant();
            double score = 0;

            foreach (var term in terms)
            {
                var tf = CountOccurrences(contentLower, term);
                if (tf == 0) continue;
                var normalised = (tf * (K1 + 1)) / (tf + K1 * (1 - B + B * section.Content.Length / avgLen));
                score += idf[term] * normalised;
            }

            foreach (var term in terms)
                if (titleLower.Contains(term))
                    score += idf[term] * 3.0;

            score += ComputePhraseBonus(contentLower, terms) * 2.0;

            if (section.Keywords?.Count > 0)
                foreach (var term in terms)
                    if (section.Keywords.Any(k => k.Contains(term, StringComparison.OrdinalIgnoreCase)))
                        score += idf.TryGetValue(term, out var w) ? w * 0.5 : 0.5;

            return score;
        }

        private double ComputeIdf(string term, List<(DocumentSection section, string id, string type)> sections, Dictionary<string, string> lowerContentMap)
        {
            double N = sections.Count;
            double n = sections.Count(s => lowerContentMap[s.id].Contains(term, StringComparison.Ordinal));
            if (n == 0) return 0;
            return Math.Log((N - n + 0.5) / (n + 0.5) + 1.0);
        }

        private int CountOccurrences(string text, string term)
        {
            if (string.IsNullOrEmpty(term)) return 0;
            int count = 0, idx = 0;
            while ((idx = text.IndexOf(term, idx, StringComparison.Ordinal)) >= 0) { count++; idx += term.Length; }
            return count;
        }

        private double ComputePhraseBonus(string contentLower, List<string> terms) =>
            terms.Count < 2 ? 0 : (contentLower.Contains(string.Join(" ", terms), StringComparison.Ordinal) ? 1.0 : 0);

        private int CountTermHits(string contentLower, List<string> terms) =>
            terms.Sum(t => contentLower.Contains(t) ? 1 : 0);

        private string GetContextPreview(string content, string contentLower, List<string> terms, int window = 100)
        {
            int firstMatch = -1;
            foreach (var term in terms)
            {
                var pos = contentLower.IndexOf(term, StringComparison.Ordinal);
                if (pos >= 0 && (firstMatch < 0 || pos < firstMatch)) firstMatch = pos;
            }
            if (firstMatch < 0)
                return SanitizePreview(content.Length > 200 ? content[..197] + "..." : content);
            var start = Math.Max(0, firstMatch - window);
            var end = Math.Min(content.Length, firstMatch + window);
            var preview = (start > 0 ? "..." : "") + content[start..end].Trim() + (end < content.Length ? "..." : "");
            return SanitizePreview(preview);
        }

        /// <summary>
        /// Remove control characters from preview text to prevent JSON serialization issues.
        /// Strips ASCII 0-31 (excluding tab/newline/carriage return) and other problematic control chars.
        /// </summary>
        private static string SanitizePreview(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            return _controlCharsRegex.Replace(text, "");
        }

        private static readonly HashSet<string> MinorWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "the","a","an","and","or","in","on","at","to","for","of","with","by","is","it","its"
        };

        private List<string> Tokenise(string query) =>
            _tokeniseRegex.Split(query.ToLowerInvariant())
                 .Where(t => t.Length > 1 && !MinorWords.Contains(t))
                 .Distinct()
                 .ToList();

        private void EvictCache()
        {
            if (_cache.Count > CACHE_SIZE)
            {
                var toRemove = _cache.Keys.Take(_cache.Count - CACHE_SIZE / 2).ToList();
                foreach (var k in toRemove) _cache.Remove(k);
            }
        }
    }
}

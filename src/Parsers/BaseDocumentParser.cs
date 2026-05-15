using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DocumentRagMcpServer.Models;

namespace DocumentRagMcpServer.Parsers
{
    /// <summary>
    /// Base implementation of document parser.
    /// Uses ONLY custom regex patterns from config — no fallback heuristics.
    /// </summary>
    public abstract class BaseDocumentParser : IDocumentParser
    {
        private static readonly Regex _multiSpaceRegex = new(@" +", RegexOptions.Compiled);

        public abstract DocumentType DocumentType { get; }

        public virtual bool CanParse(DocumentConfig config) =>
            config.Type?.Equals(DocumentType.ToString(), StringComparison.OrdinalIgnoreCase) == true;

        public abstract Task<DocumentIndex> ParseAsync(DocumentConfig config);

        protected static async Task<string> ReadFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");
            return await File.ReadAllTextAsync(filePath, System.Text.Encoding.UTF8);
        }

        public virtual List<DocumentSection> ExtractSections(string content, DocumentConfig? config = null)
        {
            var maxSize = config?.MaxSectionSize ?? 1500;
            var deduplicateSections = config?.DeduplicateSections ?? true;

            var sections = new List<DocumentSection>();

            // 1. Try Bold Extraction
            if (config?.ExtractByBold == true)
            {
                var boldStrategy = new BoldExtractionStrategy();
                sections = boldStrategy.Extract(content, config);
            }

            // 2. Try Pattern Extraction (if Bold produced nothing or wasn't enabled)
            if (sections.Count == 0 && config?.ExtractByPatterns == true && config?.CustomHeadingPatterns?.Count > 0)
            {
                var patternStrategy = new PatternExtractionStrategy();
                sections = patternStrategy.Extract(content, config);
            }

            // 3. Fallback to FixedSize if neither produced sections
            if (sections.Count == 0)
            {
                var fixedSizeStrategy = new FixedSizeExtractionStrategy();
                sections = fixedSizeStrategy.Extract(content, config);
            }

            // Post-processing
            sections = EnforceMaxSectionSize(sections, maxSize);

            if (deduplicateSections)
                sections = DeduplicateSections(sections);

            sections = PostProcessSections(sections);

            return sections;
        }

        /// <summary>
        /// Normalize content before indexing: replace all newlines/carriage returns with single space.
        /// </summary>
        private static string NormalizeContentStatic(string content)
        {
            // Replace all newline and carriage return variations with single space
            content = content.Replace("\r\n", " ");   // CRLF -> space
            content = content.Replace("\r", " ");      // CR -> space
            content = content.Replace("\n", " ");      // LF -> space
            content = content.Replace("\\r\\n", " "); // Escaped CRLF -> space
            content = content.Replace("\\r", " ");     // Escaped CR -> space
            content = content.Replace("\\n", " ");     // Escaped LF -> space

            // Collapse multiple spaces to single space
            content = _multiSpaceRegex.Replace(content, " ");

            return content.Trim();
        }

        private List<DocumentSection> EnforceMaxSectionSize(List<DocumentSection> sections, int maxSize)
        {
            var result = new List<DocumentSection>();
            foreach (var sec in sections)
            {
                if (sec.Content.Length <= maxSize)
                {
                    result.Add(sec);
                    continue;
                }

                // Section exceeds maxSize -> split at sentence boundaries near the limit
                var content = sec.Content;
                int partIndex = 1;

                while (content.Length > maxSize)
                {
                    // Find a split point near maxSize using LastIndexOf for efficiency.
                    int splitIndex = -1;
                    int searchFrom = maxSize - 1;
                    while (searchFrom > 0)
                    {
                        int dotIdx = content.LastIndexOf('.', searchFrom);
                        if (dotIdx < 0) break;
                        if (dotIdx + 1 < content.Length && char.IsWhiteSpace(content[dotIdx + 1]))
                        {
                            splitIndex = dotIdx + 1;
                            break;
                        }
                        searchFrom = dotIdx - 1;
                    }

                    // Fallback to hard split if no sentence boundary found
                    if (splitIndex == -1)
                        splitIndex = maxSize;

                    var chunk = content[..splitIndex].Trim();
                    content = content[splitIndex..].TrimStart();

                    if (chunk.Length > 0)
                    {
                        result.Add(new DocumentSection
                        {
                            Title = $"{sec.Title}.{partIndex}",
                            Content = chunk,
                            Keywords = new List<string>(),
                            Summary = "",
                            HeadingLevel = sec.HeadingLevel + 1,
                            ParentTitle = sec.Title,
                            CharCount = chunk.Length,
                            PageNumber = sec.PageNumber
                        });
                        partIndex++;
                    }
                }

                // Add remaining content
                if (content.Length > 0)
                {
                    result.Add(new DocumentSection
                    {
                        Title = $"{sec.Title}.{partIndex}",
                        Content = content,
                        Keywords = new List<string>(),
                        Summary = "",
                        HeadingLevel = sec.HeadingLevel + 1,
                        ParentTitle = sec.Title,
                        CharCount = content.Length,
                        PageNumber = sec.PageNumber
                    });
                }
            }
            return result;
        }

        private List<DocumentSection> DeduplicateSections(List<DocumentSection> sections)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<DocumentSection>(sections.Count);
            foreach (var sec in sections)
            {
                if (seen.Add(sec.Title))
                    result.Add(sec);
            }
            return result;
        }

        private List<DocumentSection> PostProcessSections(List<DocumentSection> sections)
        {
            sections = sections.Where(s => s.Content.Length >= 30).ToList();

            for (int i = 0; i < sections.Count; i++)
            {
                if (string.IsNullOrEmpty(sections[i].Id))
                    sections[i].Id = $"section-{i + 1}";

                // PDF PAGE markers already stripped in ExtractionHelper.BuildSection
                // Page numbers already set during extraction
                // Just normalize content one final time (idempotent operation)
                sections[i].Content = NormalizeContentStatic(sections[i].Content);
                sections[i].CharCount = sections[i].Content.Length;
            }

            return sections;
        }

        protected DocumentIndex CreateDocumentIndex(List<DocumentSection> sections, DocumentSection? frontMatter = null)
        {
            return new DocumentIndex
            {
                Metadata = new DocumentMetadata
                {
                    ParsedAt = DateTime.UtcNow.ToString("O"),
                    TotalPages = (int)Math.Ceiling(sections.Sum(s => s.Content.Length) / 2500.0),
                    TotalCharacters = sections.Sum(s => s.Content.Length),
                    TotalSections = sections.Count,
                    FrontMatter = frontMatter,
                    Sections = sections
                }
            };
        }
    }
}

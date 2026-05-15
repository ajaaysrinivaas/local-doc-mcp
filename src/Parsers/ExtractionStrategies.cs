using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentRagMcpServer.Models;

namespace DocumentRagMcpServer.Parsers
{
    /// <summary>Interface for document extraction strategies.</summary>
    public interface IExtractionStrategy
    {
        List<DocumentSection> Extract(string content, DocumentConfig? config);
    }

    /// <summary>Shared utilities for extraction strategies.</summary>
    public static class ExtractionHelper
    {
        private const int MinSectionLength = 30;
        private const int MaxTitleLength = 120;

        private static readonly Regex _multiSpaceRegex = new(@" +", RegexOptions.Compiled);
        private static readonly Regex _hashHeaderRegex = new(@"^#+\s*", RegexOptions.Compiled);
        private static readonly Regex _numberingRegex = new(@"^\d+(\.\d+)*\.?\s*", RegexOptions.Compiled);
        private static readonly Regex _pdfPageMarkerRegex = new(
            @"={40,}\s*\r?\n\s*PDF PAGE\s+(\d+)\s*\r?\n\s*={40,}\s*(\r?\n)?",
            RegexOptions.Compiled);

        public static DocumentSection BuildSection(string title, string content)
        {
            // CRITICAL: Extract page number from markers BEFORE stripping them
            int pageNumber = ExtractPageNumberFromMarkers(content);

            // Strip PDF PAGE markers BEFORE normalizing newlines
            // This prevents markers from being mangled when newlines → spaces
            content = StripPdfPageMarkers(content);

            var normalized = NormalizeContent(content);
            return new DocumentSection
            {
                Title = SanitizeTitle(title),
                Content = normalized,
                Keywords = new List<string>(),
                Summary = "",
                PageNumber = pageNumber
            };
        }

        public static string NormalizeContent(string content)
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

            // Remove trailing and leading whitespace
            return content.Trim();
        }

        public static string SanitizeTitle(string raw)
        {
            var s = _hashHeaderRegex.Replace(raw, "").Trim();      // Remove markdown headers
            s = _numberingRegex.Replace(s, "").Trim();             // Remove numbering
            if (s.Length > MaxTitleLength)
                return s[..(MaxTitleLength - 3)] + "...";
            return s;
        }

        public static bool IsValidSection(string content) => content.Trim().Length >= MinSectionLength;

        /// <summary>
        /// Strips all PDF PAGE marker blocks before newline normalization.
        /// Regex: =====+ / PDF PAGE N / =====+ with optional whitespace and newlines.
        /// Must run BEFORE NormalizeContent to match markers with intact newlines.
        /// </summary>
        private static string StripPdfPageMarkers(string content)
        {
            // Match markers with original newlines intact: ====\nPDF PAGE N\n====
            return _pdfPageMarkerRegex.Replace(content, "").Trim();
        }

        /// <summary>
        /// Extracts the first page number from PDF PAGE markers in content.
        /// Returns 0 if no marker is found.
        /// Must run BEFORE NormalizeContent to match markers with intact newlines.
        /// </summary>
        private static int ExtractPageNumberFromMarkers(string content)
        {
            var match = _pdfPageMarkerRegex.Match(content);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int pageNum))
                return pageNum;

            return 0;
        }
    }

    /// <summary>Pattern-based extraction using regex patterns from config. Falls back to plain keyword match.</summary>
    public class PatternExtractionStrategy : IExtractionStrategy
    {
        public List<DocumentSection> Extract(string content, DocumentConfig? config)
        {
            var patterns = config?.CustomHeadingPatterns;
            if (patterns == null || patterns.Count == 0)
                return new List<DocumentSection>();

            var sections = new List<DocumentSection>();
            var matches = new List<(int pos, string title, int level)>();

            for (int i = 0; i < patterns.Count; i++)
            {
                var pattern = patterns[i];
                var level = i == 0 ? 1 : 2; // First pattern = H1, rest = H2

                try
                {
                    // Try as Regex first
                    var rx = new Regex(pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    foreach (Match m in rx.Matches(content))
                    {
                        matches.Add((m.Index, m.Value.Trim(), level));
                    }
                }
                catch
                {
                    // Fallback to substring matching if it's not a valid regex
                    int pos = 0;
                    while ((pos = content.IndexOf(pattern, pos, StringComparison.OrdinalIgnoreCase)) != -1)
                    {
                        // Find the start of the line for the title
                        int lineStart = content.LastIndexOf('\n', pos) + 1;
                        if (lineStart < 0) lineStart = 0;
                        int lineEnd = content.IndexOf('\n', pos);
                        if (lineEnd < 0) lineEnd = content.Length;

                        var title = content[lineStart..lineEnd].Trim();
                        matches.Add((lineStart, title, level));
                        pos = lineEnd + 1;
                    }
                }
            }

            if (matches.Count < 1)
                return new List<DocumentSection>();

            // Distinct and sort matches by position
            matches = matches
                .GroupBy(m => m.pos)
                .Select(g => g.First())
                .OrderBy(m => m.pos)
                .ToList();

            string? parentTitle = null;

            for (int i = 0; i < matches.Count; i++)
            {
                var (pos, title, level) = matches[i];
                var bodyStart = pos + content[pos..].IndexOf('\n') + 1;
                if (bodyStart >= content.Length) bodyStart = pos + title.Length + 1;

                var bodyEnd = i + 1 < matches.Count ? matches[i + 1].pos : content.Length;
                var body = content[bodyStart..bodyEnd].Trim();

                if (!ExtractionHelper.IsValidSection(body))
                    continue;

                if (level == 1)
                    parentTitle = title;

                var section = ExtractionHelper.BuildSection(title, body);
                section.HeadingLevel = level;
                if (level > 1 && parentTitle != null)
                    section.ParentTitle = parentTitle;

                sections.Add(section);
            }

            return sections;
        }
    }

    /// <summary>Bold-based extraction: lines starting with **bold** text define sections.</summary>
    public class BoldExtractionStrategy : IExtractionStrategy
    {
        private static readonly Regex _boldRegex = new(@"^\*\*([^*]+)\*\*", RegexOptions.Compiled);

        public List<DocumentSection> Extract(string content, DocumentConfig? config)
        {
            var lines = content.Split('\n');
            var sections = new List<DocumentSection>();
            var current = (title: "", body: "");

            foreach (var line in lines)
            {
                var boldMatch = _boldRegex.Match(line);

                if (boldMatch.Success)
                {
                    // Save previous section
                    if (ExtractionHelper.IsValidSection(current.body))
                        sections.Add(ExtractionHelper.BuildSection(current.title, current.body));

                    current = (boldMatch.Groups[1].Value.Trim(), "");
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    current.body += (current.body.Length > 0 ? "\n" : "") + line;
                }
            }

            // Save last section
            if (ExtractionHelper.IsValidSection(current.body))
                sections.Add(ExtractionHelper.BuildSection(current.title, current.body));

            return sections;
        }
    }

    /// <summary>Fixed-size extraction: break content into ~charsPerSection chunks at paragraph boundaries.</summary>
    public class FixedSizeExtractionStrategy : IExtractionStrategy
    {
        public List<DocumentSection> Extract(string content, DocumentConfig? config)
        {
            // FixedSize is now the fallback and uses MaxSectionSize as its target chunk size.
            var charsPerSection = config?.MaxSectionSize ?? 1500;
            var paragraphs = content
                .Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.None)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .ToList();

            if (paragraphs.Count == 0)
                return new List<DocumentSection>();

            var sections = new List<DocumentSection>();
            var currentContent = "";
            int sectionIndex = 1;

            foreach (var para in paragraphs)
            {
                // If adding this paragraph exceeds target, save current and start new
                if (currentContent.Length > 0 && currentContent.Length + para.Length > charsPerSection)
                {
                    if (ExtractionHelper.IsValidSection(currentContent))
                    {
                        var title = $"Section {sectionIndex}";
                        var section = ExtractionHelper.BuildSection(title, currentContent);
                        section.HeadingLevel = 0;
                        sections.Add(section);
                        sectionIndex++;
                    }
                    currentContent = para;
                }
                else
                {
                    currentContent += (currentContent.Length > 0 ? "\n\n" : "") + para;
                }
            }

            // Save final section
            if (ExtractionHelper.IsValidSection(currentContent))
            {
                var title = $"Section {sectionIndex}";
                var section = ExtractionHelper.BuildSection(title, currentContent);
                section.HeadingLevel = 0;
                sections.Add(section);
            }

            return sections;
        }
    }
}

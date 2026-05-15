using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DocumentRagMcpServer.Models
{
    public enum DocumentType { Generic, Book, Manual }

    /// <summary>Page selection for book/long-form documents.</summary>
    public class PageRange
    {
        [JsonPropertyName("start")]
        public int Start { get; set; } = 1;

        [JsonPropertyName("end")]
        public int? End { get; set; }

        /// <summary>Specific page numbers to include (overrides start/end).</summary>
        [JsonPropertyName("only")]
        public List<int>? Only { get; set; }

        [JsonPropertyName("exclude")]
        public List<int>? Exclude { get; set; }
    }

    /// <summary>
    /// Preprocessing configuration (markdown and line-break handling).
    /// Applied during single-pass PDF → txt → normalization pipeline.
    /// Defaults: cleanEncoding=true, preserveMarkdown=true (markdown preserved by default).
    /// </summary>
    public class PreprocessingConfig
    {
        /// <summary>
        /// Clean encoding artifacts and invalid Unicode sequences (e.g., mojibake, \r, control chars).
        /// Default: true. This runs on all files.
        /// </summary>
        [JsonPropertyName("cleanEncoding")]
        public bool CleanEncoding { get; set; } = true;

        /// <summary>
        /// Preserve markdown formatting (**bold**, _italic_, # headers, etc.).
        /// Default: true. When true, overrides all strip* options.
        /// </summary>
        [JsonPropertyName("preserveMarkdown")]
        public bool PreserveMarkdown { get; set; } = true;

        /// <summary>Strip markdown bold (**text** → text). Ignored if preserveMarkdown=true.</summary>
        [JsonPropertyName("stripBold")]
        public bool StripBold { get; set; } = false;

        /// <summary>Strip markdown italic (_text_ → text). Ignored if preserveMarkdown=true.</summary>
        [JsonPropertyName("stripItalic")]
        public bool StripItalic { get; set; } = false;

        /// <summary>Strip markdown headers (# Header → Header). Ignored if preserveMarkdown=true.</summary>
        [JsonPropertyName("stripHeaders")]
        public bool StripHeaders { get; set; } = false;

        /// <summary>Remove PDF PAGE N markers and separator lines (default false).</summary>
        [JsonPropertyName("removePageMarkers")]
        public bool RemovePageMarkers { get; set; } = false;

        /// <summary>Normalize line breaks intelligently (sentence-end preservation, mid-sentence joining).</summary>
        [JsonPropertyName("normalizeLineBreaks")]
        public bool NormalizeLineBreaks { get; set; } = false;

        /// <summary>Join short lines with next line (useful for poorly formatted PDFs).</summary>
        [JsonPropertyName("joinShortLines")]
        public bool JoinShortLines { get; set; } = false;
    }

    /// <summary>Per-document configuration. Stored in config/{filename}.json with only pageRange, meta, and parsing options.</summary>
    public class DocumentConfig
    {
        /// <summary>Document ID (auto-generated from filename, not serialized).</summary>
        [JsonIgnore]
        public string Id { get; set; } = "";

        /// <summary>Filename relative to /documents/ (not serialized, set at runtime).</summary>
        [JsonIgnore]
        public string File { get; set; } = "";

        /// <summary>Resolved absolute file path (not serialized, set at runtime).</summary>
        [JsonIgnore]
        public string FilePath { get; set; } = "";

        /// <summary>Document type (not serialized, auto-detected at runtime).</summary>
        [JsonIgnore]
        public string? Type { get; set; }

        /// <summary>Page range to index. Omit to index all pages.</summary>
        [JsonPropertyName("pageRange")]
        public PageRange? PageRange { get; set; }

        /// <summary>Extra metadata.</summary>
        [JsonPropertyName("meta")]
        public Dictionary<string, string>? Meta { get; set; }

        /// <summary>Preprocessing configuration (markdown/line-break handling).</summary>
        [JsonPropertyName("preprocessing")]
        public PreprocessingConfig Preprocessing { get; set; } = new();

        // ── Extraction Strategies ───────────────────────────────────────────

        /// <summary>Extract sections by bold markers (**text**) — headings in books/manuals (default false).</summary>
        [JsonPropertyName("extractByBold")]
        public bool ExtractByBold { get; set; } = false;

        /// <summary>
        /// Extract sections by custom patterns from customHeadingPatterns.
        /// Each entry is matched as a regex OR a plain keyword — whichever parses successfully as regex.
        /// (default false)
        /// </summary>
        [JsonPropertyName("extractByPatterns")]
        public bool ExtractByPatterns { get; set; } = false;

        /// <summary>Keywords or regex patterns for section boundaries (used when extractByPatterns = true).</summary>
        [JsonPropertyName("customHeadingPatterns")]
        public List<string>? CustomHeadingPatterns { get; set; }

        // ── Post-Processing ────────────────────────────────────────────

        /// <summary>Maximum section size in characters (default 1500). Larger sections are split intelligently at sentence boundaries.</summary>
        [JsonPropertyName("maxSectionSize")]
        public int MaxSectionSize { get; set; } = 1500;

        /// <summary>Remove duplicate sections by title (default true).</summary>
        [JsonPropertyName("deduplicateSections")]
        public bool DeduplicateSections { get; set; } = true;
    }

    /// <summary>
    /// Global configuration (config/rag-config.json).
    /// Controls the document processing pipeline and indexing strategy.
    /// </summary>
    public class RagConfig
    {
        /// <summary>
        /// Master toggle for single-pass unified pipeline: PDF → txt → normalization.
        /// When true (default):
        ///   - PDF parsing and preprocessing happen in ONE pass
        ///   - Per-document preprocessing config is applied automatically
        ///   - Default: cleanEncoding=true, preserveMarkdown=true for all files
        /// When false: PDF and TXT are converted to /documents/, then indexing happens separately.
        /// </summary>
        [JsonPropertyName("enableUnifiedPipeline")]
        public bool EnableUnifiedPipeline { get; set; } = true;

        /// <summary>
        /// Legacy toggle: Enable document preprocessor.
        /// Only used if EnableUnifiedPipeline=false. Default: true.
        /// When EnableUnifiedPipeline=true, this is ignored (preprocessing always runs).
        /// </summary>
        [JsonPropertyName("enablePreprocessing")]
        public bool EnablePreprocessing { get; set; } = true;

        /// <summary>Path to preprocessing config file (optional).</summary>
        [JsonPropertyName("preprocessingConfigPath")]
        public string? PreprocessingConfigPath { get; set; }
    }
}

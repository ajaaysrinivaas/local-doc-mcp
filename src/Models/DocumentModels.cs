using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DocumentRagMcpServer.Models
{
    /// <summary>Represents a section of a document.</summary>
    public class DocumentSection
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";

        [JsonPropertyName("keywords")]
        public List<string> Keywords { get; set; } = new();

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("documentId")]
        public string? DocumentId { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        /// <summary>Heading level (0=no heading, 1=h1, 2=h2, etc.) for hierarchy tracking.</summary>
        [JsonPropertyName("headingLevel")]
        public int HeadingLevel { get; set; } = 0;

        /// <summary>Parent section title for maintaining document structure.</summary>
        [JsonPropertyName("parentTitle")]
        public string? ParentTitle { get; set; }

        /// <summary>Character count of content for size tracking.</summary>
        [JsonPropertyName("charCount")]
        public int CharCount { get; set; }

        /// <summary>Page number (for PDF extracts) where this section originates. 0 = unknown.</summary>
        [JsonPropertyName("pageNumber")]
        public int PageNumber { get; set; } = 0;
    }

    /// <summary>Metadata about the entire document.</summary>
    public class DocumentMetadata
    {
        [JsonPropertyName("parsedAt")]
        public string ParsedAt { get; set; } = "";

        [JsonPropertyName("totalPages")]
        public int TotalPages { get; set; }

        [JsonPropertyName("totalCharacters")]
        public int TotalCharacters { get; set; }

        [JsonPropertyName("totalSections")]
        public int TotalSections { get; set; }

        [JsonPropertyName("frontMatter")]
        public DocumentSection? FrontMatter { get; set; }

        [JsonPropertyName("sections")]
        public List<DocumentSection> Sections { get; set; } = new();
    }

    /// <summary>Root document index structure.</summary>
    public class DocumentIndex
    {
        [JsonPropertyName("metadata")]
        public DocumentMetadata Metadata { get; set; } = new();

        [JsonPropertyName("files")]
        public List<DocumentFileInfo> Files { get; set; } = new();
    }

    /// <summary>Information about a document file in the index.</summary>
    public class DocumentFileInfo
    {
        [JsonPropertyName("documentId")]
        public string DocumentId { get; set; } = "";

        [JsonPropertyName("filePath")]
        public string FilePath { get; set; } = "";

        [JsonPropertyName("documentType")]
        public string DocumentType { get; set; } = "";

        [JsonPropertyName("sectionCount")]
        public int SectionCount { get; set; }

        [JsonPropertyName("totalCharacters")]
        public int TotalCharacters { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }
    }

    /// <summary>A search result matching a query.</summary>
    public class SearchResult
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("relevanceScore")]
        public int RelevanceScore { get; set; }

        [JsonPropertyName("preview")]
        public string Preview { get; set; } = "";

        [JsonPropertyName("matchCount")]
        public int MatchCount { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("documentId")]
        public string? DocumentId { get; set; }

        /// <summary>Full section content (populated when explicitly requested).</summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        /// <summary>Heading level for hierarchical awareness.</summary>
        [JsonPropertyName("headingLevel")]
        public int HeadingLevel { get; set; }

        /// <summary>Parent section title (for hierarchy context).</summary>
        [JsonPropertyName("parentTitle")]
        public string? ParentTitle { get; set; }

        /// <summary>ID of previous section in document order.</summary>
        [JsonPropertyName("previousSectionId")]
        public string? PreviousSectionId { get; set; }

        /// <summary>ID of next section in document order.</summary>
        [JsonPropertyName("nextSectionId")]
        public string? NextSectionId { get; set; }

        /// <summary>Hierarchy path (e.g., "Chapter 2 > Section 2.3").</summary>
        [JsonPropertyName("hierarchyPath")]
        public string? HierarchyPath { get; set; }
    }

}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DocumentRagMcpServer.Config;
using DocumentRagMcpServer.Interfaces;
using DocumentRagMcpServer.Models;
using DocumentRagMcpServer.Parsers;

namespace DocumentRagMcpServer.Repositories
{
    /// <summary>
    /// Manages the document index lifecycle:
    ///   • Converts raw files from /raw/ into /documents/ (parse + preprocess in one pass)
    ///   • Auto-discovers all .txt files in /documents/
    ///   • Rebuilds the index only when files have changed (or --force-rebuild)
    ///   • Stores the cached index in .index/document_index.json
    /// </summary>
    public class DocumentRepository : IDocumentRepository, IDisposable
    {
        private DocumentIndex? _cachedIndex;
        private readonly string _rawPath;
        private readonly string _documentsPath;
        private readonly string _configPath;
        private readonly string _indexPath;
        private readonly ParserFactory _parserFactory;
        private readonly ParsingConfigLoader _configLoader;
        private readonly bool _forceRebuild;
        private bool _disposed = false;

        private static readonly Regex _multiSpaceRegex = new(@" +", RegexOptions.Compiled);
        private static readonly Regex _docIdRegex = new(@"[^a-z0-9]+", RegexOptions.Compiled);

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public DocumentRepository(string dataPath, bool forceRebuild = false)
        {
            try
            {
                _rawPath = Path.Combine(dataPath, "raw");
                _documentsPath = Path.Combine(dataPath, "documents");
                _configPath = Path.Combine(dataPath, "config");
                _indexPath = Path.Combine(dataPath, ".index", "document_index.json");
                _parserFactory = new ParserFactory();
                _configLoader = new ParsingConfigLoader(_configPath);
                _forceRebuild = forceRebuild;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [DocumentRepository] Error during initialization: {ex.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _cachedIndex = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        // ── Public API ────────────────────────────────────────────────────────

        public async Task<DocumentIndex> LoadIndexAsync()
        {
            if (_disposed) throw new ObjectDisposedException("DocumentRepository");
            if (_cachedIndex != null) return _cachedIndex;

            await ConvertRawDocumentsAsync();

            if (!_forceRebuild && File.Exists(_indexPath) && !NeedsRebuild())
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_indexPath, System.Text.Encoding.UTF8);
                    _cachedIndex = JsonSerializer.Deserialize<DocumentIndex>(json)
                        ?? throw new InvalidOperationException("Invalid index file");

                    // CRITICAL: Normalize all section content when loading from cache
                    // This removes any \r\n artifacts that may have been serialized
                    _cachedIndex = NormalizeIndexContent(_cachedIndex);

                    Console.Error.WriteLine($"  Loaded {_cachedIndex.Files.Count} document(s) from cache ({_cachedIndex.Metadata.TotalSections} sections)");
                    return _cachedIndex;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Cache invalid ({ex.Message}), rebuilding...");
                }
            }

            _cachedIndex = await BuildIndexAsync();
            await SaveIndexAsync(_cachedIndex);
            return _cachedIndex;
        }

        public async Task<DocumentIndex> RebuildIndexAsync()
        {
            _cachedIndex = null;
            _configLoader.Invalidate();

            await ConvertRawDocumentsAsync();

            _cachedIndex = await BuildIndexAsync();
            await SaveIndexAsync(_cachedIndex);

            return _cachedIndex;
        }

        // ── Index lifecycle ───────────────────────────────────────────────────

        private bool NeedsRebuild()
        {
            if (!File.Exists(_indexPath)) return true;
            var indexTime = File.GetLastWriteTimeUtc(_indexPath);

            foreach (var dir in new[] { _documentsPath, _rawPath })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir))
                    if (File.GetLastWriteTimeUtc(f) > indexTime) return true;
            }

            // Check if any config files have changed
            if (Directory.Exists(_configPath))
            {
                foreach (var f in Directory.GetFiles(_configPath, "*.json"))
                    if (File.GetLastWriteTimeUtc(f) > indexTime) return true;
            }

            return false;
        }

        private async Task ConvertRawDocumentsAsync()
        {
            if (!Directory.Exists(_rawPath)) return;

            var config = _configLoader.LoadConfig();

            // Use unified pipeline: PDF → txt → normalization in single pass
            var pipeline = new Services.UnifiedPreprocessingPipeline(Path.GetDirectoryName(_rawPath) ?? ".");
            var (filesProcessed, filesPreprocessed, errors) = await pipeline.ExecuteAsync(config, _configLoader);

            if (errors.Count > 0)
            {
                foreach (var (file, error) in errors)
                    Console.Error.WriteLine($"  ⚠ {file}: {error}");
            }

            if (filesProcessed == 0 && filesPreprocessed == 0)
            {
                Console.Error.WriteLine("  No files to process");
            }
        }

        private async Task<DocumentIndex> BuildIndexAsync()
        {
            var configs = BuildDocumentConfigs();
            var allSections = new List<DocumentSection>();
            var fileInfos = new List<DocumentFileInfo>();

            Console.Error.WriteLine($"  Indexing {configs.Count} document(s) from /documents/...");

            foreach (var docConfig in configs)
            {
                try
                {
                    var parser = _parserFactory.GetParser(docConfig);
                    var docIndex = await parser.ParseAsync(docConfig);

                    // Apply filters (page range, keywords)
                    var sections = ApplyFilters(docIndex.Metadata.Sections, docConfig);

                    foreach (var s in sections)
                        s.DocumentId = docConfig.Id;

                    if (docIndex.Metadata.FrontMatter != null)
                        docIndex.Metadata.FrontMatter.DocumentId = docConfig.Id;

                    allSections.AddRange(sections);

                    fileInfos.Add(new DocumentFileInfo
                    {
                        DocumentId = docConfig.Id,
                        FilePath = Path.GetFileName(docConfig.FilePath),
                        DocumentType = docConfig.Type ?? DocumentTypeDetector.Detect(docConfig.FilePath).ToString(),
                        SectionCount = sections.Count,
                        TotalCharacters = sections.Sum(s => s.Content.Length),
                        Summary = docIndex.Metadata.FrontMatter?.Summary
                    });

                    Console.Error.WriteLine($"    [{docConfig.Id}] {sections.Count} section(s)" +
                        (docConfig.PageRange != null ? " (page range limited)" : ""));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Failed to parse {docConfig.Id}: {ex.Message}");
                }
            }

            return new DocumentIndex
            {
                Metadata = new DocumentMetadata
                {
                    ParsedAt = DateTime.UtcNow.ToString("O"),
                    TotalPages = (int)Math.Ceiling(allSections.Sum(s => s.Content.Length) / 2500.0),
                    TotalCharacters = allSections.Sum(s => s.Content.Length),
                    TotalSections = allSections.Count,
                    Sections = allSections
                },
                Files = fileInfos
            };
        }

        private List<DocumentConfig> BuildDocumentConfigs()
        {
            Directory.CreateDirectory(_documentsPath);
            var configs = new List<DocumentConfig>();

            foreach (var file in Directory.GetFiles(_documentsPath, "*.txt").OrderBy(f => f))
            {
                var filename = Path.GetFileName(file);
                var id = _docIdRegex.Replace(
                    Path.GetFileNameWithoutExtension(file).ToLowerInvariant(),
                    "-").Trim('-');

                var docConfig = _configLoader.LoadDocumentConfig(filename) ?? new DocumentConfig();
                docConfig.Id = id;
                docConfig.File = filename;
                docConfig.FilePath = file;

                configs.Add(docConfig);
            }

            return configs;
        }

        /// <summary>
        /// Applies page range filter to sections based on config.
        /// Supports both continuous ranges (Start/End) and specific pages (Only).
        /// Uses precomputed cumulative offsets to avoid O(n²) scanning.
        /// </summary>
        private List<DocumentSection> ApplyFilters(List<DocumentSection> sections, DocumentConfig config)
        {
            if (config.PageRange == null) return sections;

            const int pageSize = 2500;

            // Precompute start char offset for each section — O(n) once
            var offsets = new int[sections.Count];
            for (int i = 1; i < sections.Count; i++)
                offsets[i] = offsets[i - 1] + sections[i - 1].Content.Length;

            if (config.PageRange.Only?.Count > 0)
            {
                var allowedPages = new HashSet<int>(config.PageRange.Only);
                var filtered = new List<DocumentSection>(sections.Count);
                for (int i = 0; i < sections.Count; i++)
                {
                    var startPage = (offsets[i] / pageSize) + 1;
                    var endPage = (int)Math.Ceiling((double)(offsets[i] + sections[i].Content.Length) / pageSize);
                    for (int p = startPage; p <= endPage; p++)
                    {
                        if (allowedPages.Contains(p)) { filtered.Add(sections[i]); break; }
                    }
                }
                return filtered;
            }
            else
            {
                var startChar = (config.PageRange.Start - 1) * pageSize;
                var endChar = config.PageRange.End.HasValue ? config.PageRange.End.Value * pageSize : int.MaxValue;
                var filtered = new List<DocumentSection>(sections.Count);
                for (int i = 0; i < sections.Count; i++)
                {
                    if (offsets[i] < endChar && offsets[i] + sections[i].Content.Length > startChar)
                        filtered.Add(sections[i]);
                }
                return filtered;
            }
        }

        private async Task SaveIndexAsync(DocumentIndex index)
        {
            try
            {
                // CRITICAL: Normalize all content before serializing to JSON
                // This removes any \r\n artifacts from extraction/processing
                index = NormalizeIndexContent(index);

                Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);
                await File.WriteAllTextAsync(_indexPath, JsonSerializer.Serialize(index, _jsonOptions), System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Failed to save index: {ex.Message}");
            }
        }

        /// <summary>
        /// Normalize all section content in an index to remove carriage returns and escaped sequences.
        /// </summary>
        private DocumentIndex NormalizeIndexContent(DocumentIndex? index)
        {
            if (index == null)
                throw new InvalidOperationException("Index cannot be null");

            if (index.Metadata?.Sections != null)
            {
                foreach (var section in index.Metadata.Sections)
                {
                    section.Content = NormalizeNewlines(section.Content);
                }
            }

            if (index.Metadata?.FrontMatter != null)
            {
                index.Metadata.FrontMatter.Content = NormalizeNewlines(index.Metadata.FrontMatter.Content);
            }

            return index;
        }

        /// <summary>
        /// Comprehensive newline/carriage-return normalization: replace all with single space.
        /// </summary>
        private static string NormalizeNewlines(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;

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

        // ── IDocumentRepository ───────────────────────────────────────────────

        public List<(DocumentSection section, string id, string type)> GetAllSectionsWithIds(DocumentIndex index)
        {
            var result = new List<(DocumentSection, string, string)>();
            for (int i = 0; i < index.Metadata.Sections.Count; i++)
            {
                var s = index.Metadata.Sections[i];
                var id = !string.IsNullOrEmpty(s.Id) ? s.Id : $"section-{i + 1}";
                result.Add((s, id, "section"));
            }
            return result;
        }

        /// <summary>
        /// Get page content directly from source documents (primary source).
        /// Extracts content between PDF PAGE markers.
        /// </summary>
        public async Task<List<(string content, string title, string documentId)>> GetPageFromSourceAsync(int pageNumber, string? fileId = null)
        {
            var results = new List<(string content, string title, string documentId)>();

            if (!Directory.Exists(_documentsPath))
                return results;

            var documentFiles = Directory.GetFiles(_documentsPath, "*.txt");

            foreach (var filePath in documentFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var documentId = fileId ?? fileName;

                // Skip if fileId filter specified and doesn't match
                if (!string.IsNullOrEmpty(fileId) && !fileName.Equals(fileId, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var content = await File.ReadAllTextAsync(filePath);
                    var pageMarker = $"PDF PAGE {pageNumber}";

                    // Find page start
                    var pageStartIdx = content.IndexOf(pageMarker, StringComparison.Ordinal);
                    if (pageStartIdx == -1)
                        continue; // Page not found in this file

                    // Find page end (next page marker or end of file)
                    var nextPageMarker = $"PDF PAGE {pageNumber + 1}";
                    var pageEndIdx = content.IndexOf(nextPageMarker, pageStartIdx + pageMarker.Length, StringComparison.Ordinal);

                    if (pageEndIdx == -1)
                        pageEndIdx = content.Length;

                    // Extract page content (skip the page marker line)
                    var pageContentStart = pageStartIdx + pageMarker.Length;
                    var pageContent = content[pageContentStart..pageEndIdx].Trim();

                    if (!string.IsNullOrEmpty(pageContent))
                    {
                        results.Add((pageContent, $"Page {pageNumber}", documentId));
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error reading source document {filePath}: {ex.Message}");
                }
            }

            return results;
        }


    }
}



using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DocumentRagMcpServer.Interfaces;
using DocumentRagMcpServer.Models;

namespace DocumentRagMcpServer.MCP
{
    public class McpMessageHandler : IMcpMessageHandler
    {
        private readonly IDocumentRepository _repo;
        private readonly ISearchService _search;
        private DocumentIndex? _index;

        public McpMessageHandler(IDocumentRepository repo, ISearchService search)
        {
            _repo = repo;
            _search = search;
        }

        public async Task InitializeAsync()
        {
            _index = await _repo.LoadIndexAsync();
        }

        public async Task<McpMessage> HandleMessageAsync(McpMessage message)
        {
            switch (message.Method)
            {
                case "initialize": return HandleInitialize(message);
                case "tools/list": return HandleListTools(message);
                case "tools/call": return await HandleCallToolAsync(message);
                case "ping": return new McpMessage { Id = message.Id, Result = new { } };
                default: return new McpMessage { Id = message.Id, Error = new ErrorObject { Code = -32601, Message = "Method not found" } };
            }
        }

        private McpMessage HandleInitialize(McpMessage message)
        {
            var result = new InitializeResult
            {
                ServerInfo = new Dictionary<string, string> { ["name"] = "document-rag", ["version"] = "3.0.0" },
                Capabilities = new Dictionary<string, object> { ["tools"] = new { } }
            };
            return new McpMessage { Id = message.Id, Result = result };
        }

        private McpMessage HandleListTools(McpMessage message)
        {
            var tools = new List<Tool>
            {
                MakeTool("search",
                    "Search documents by keywords. Optionally filter to a specific file using file_id. If no file_id is provided, searches all files.",
                    new { type="object", properties=new{
                        query   = new{type="string",description="Keywords or phrase to search for."},
                        file_id = new{type="string",description="Optional. File ID from list_files to restrict search to one file."},
                        limit   = new{type="integer",description="Max results 1-20 (default 5)."}
                    }, required=new[]{"query"}}),

                MakeTool("get_section",
                    "Retrieve the full content of a specific section by ID. Includes full text, hierarchy info, and optionally adjacent sections for context.",
                    new { type="object", properties=new{
                        section_id = new{type="string",description="The section ID from search results."},
                        include_context = new{type="boolean",description="Optional. Include previous and next sections for context (default false)."}
                    }, required=new[]{"section_id"}}),

                MakeTool("get_page",
                    "Get content from a specific page number. Optionally filter to a specific file using file_id. If no file_id is provided, searches all files.",
                    new { type="object", properties=new{
                        page_number = new{type="integer",description="Page number to retrieve."},
                        file_id     = new{type="string",description="Optional. File ID from list_files to restrict to one file."}
                    }, required=new[]{"page_number"}}),

                MakeTool("list_files",
                    "List all available documents with their IDs, types, and metadata.",
                    new { type="object", properties=new{}, required=new string[]{} }),
            };
            return new McpMessage { Id = message.Id, Result = new ListToolsResult { Tools = tools } };
        }

        private async Task<McpMessage> HandleCallToolAsync(McpMessage message)
        {
            if (_index == null)
                return Err(message.Id, -32603, "Index not initialized");

            CallToolParams? p;
            try
            {
                p = JsonSerializer.Deserialize<CallToolParams>(
                    ((JsonElement)message.Params!).GetRawText(),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
            catch { return Err(message.Id, -32602, "Invalid parameters"); }

            if (p == null) return Err(message.Id, -32602, "Invalid parameters");

            try
            {
                Task<object> taskResult = p.Name switch
                {
                    "search"      => Task.FromResult(HandleSearch(p)),
                    "get_section" => Task.FromResult(HandleGetSection(p)),
                    "list_files"  => Task.FromResult(HandleListFiles()),
                    "get_page"    => HandleGetPageAsync(p),
                    _             => throw new InvalidOperationException($"Unknown tool: {p.Name}")
                };
                return new McpMessage { Id = message.Id, Result = await taskResult };
            }
            catch (Exception ex)
            {
                return Err(message.Id, -32603, ex.Message);
            }
        }

        // ── Tool Handlers ─────────────────────────────────────────────────────

        private object HandleSearch(CallToolParams p)
        {
            var query = GetStr(p, "query") ?? throw new ArgumentException("Missing query");
            var fileId = GetStr(p, "file_id");
            var limit = GetInt(p, "limit", 5);
            return Wrap(_search.Search(query, _index!, fileId, limit));
        }

        private object HandleGetSection(CallToolParams p)
        {
            var sectionId = GetStr(p, "section_id") ?? throw new ArgumentException("Missing section_id");
            var includeContext = GetBool(p, "include_context", false);

            var allSections = _repo.GetAllSectionsWithIds(_index!);
            var target = allSections.FirstOrDefault(s => s.id == sectionId);
            if (target.section == null)
                throw new InvalidOperationException($"Section '{sectionId}' not found");

            var result = new
            {
                id = sectionId,
                title = target.section.Title,
                content = target.section.Content,
                documentId = target.section.DocumentId,
                headingLevel = target.section.HeadingLevel,
                parentTitle = target.section.ParentTitle,
                keywords = target.section.Keywords,
                summary = target.section.Summary,
                charCount = target.section.CharCount
            };

            // If context requested, find adjacent sections
            if (includeContext)
            {
                var siblingsByDoc = allSections
                    .Where(s => s.section.DocumentId == target.section.DocumentId)
                    .Select((s, i) => (s, index: i))
                    .ToList();

                var targetIndex = siblingsByDoc.FindIndex(x => x.s.id == sectionId);
                var context = new
                {
                    current = result,
                    previous = targetIndex > 0 ? new
                    {
                        id = siblingsByDoc[targetIndex - 1].s.id,
                        title = siblingsByDoc[targetIndex - 1].s.section.Title,
                        content = siblingsByDoc[targetIndex - 1].s.section.Content,
                        summary = siblingsByDoc[targetIndex - 1].s.section.Summary
                    } : null,
                    next = targetIndex < siblingsByDoc.Count - 1 ? new
                    {
                        id = siblingsByDoc[targetIndex + 1].s.id,
                        title = siblingsByDoc[targetIndex + 1].s.section.Title,
                        content = siblingsByDoc[targetIndex + 1].s.section.Content,
                        summary = siblingsByDoc[targetIndex + 1].s.section.Summary
                    } : null
                };
                return Wrap(context);
            }

            return Wrap(result);
        }

        private async Task<object> HandleGetPageAsync(CallToolParams p)
        {
            var pageNum = GetInt(p, "page_number", 0);
            if (pageNum <= 0) throw new ArgumentException("Missing or invalid page_number");
            var fileId = GetStr(p, "file_id");

            // PRIMARY: Try source documents first
            var sourceResults = await _repo.GetPageFromSourceAsync(pageNum, fileId);
            if (sourceResults.Count > 0)
            {
                return Wrap(sourceResults.Select(s => new
                {
                    documentId = s.documentId,
                    title = s.title,
                    content = s.content,
                    pageNumber = pageNum,
                    source = "source_document",
                    keywords = new List<string>()
                }).ToList());
            }

            // FALLBACK: Use index if source not found
            var matches = _repo.GetAllSectionsWithIds(_index!)
                .Where(s => s.section.PageNumber == pageNum)
                .Where(s => string.IsNullOrEmpty(fileId) || s.section.DocumentId == fileId)
                .Select(s => new
                {
                    documentId = s.section.DocumentId,
                    title = s.section.Title,
                    content = s.section.Content,
                    pageNumber = s.section.PageNumber,
                    source = "index",
                    keywords = s.section.Keywords
                })
                .ToList();

            if (matches.Count == 0)
                throw new InvalidOperationException(
                    $"Page {pageNum} not found{(fileId != null ? $" in file '{fileId}'" : "")} (checked both source documents and index)");

            return (object)Wrap(matches);
        }

        private object HandleListFiles()
        {
            return Wrap(_index!.Files.Select(f => new
            {
                file_id = f.DocumentId,
                file = f.FilePath,
                documentType = f.DocumentType,
                totalCharacters = f.TotalCharacters
            }));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static readonly JsonSerializerOptions _wrapOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static object Wrap(object data)
        {
            var json = JsonSerializer.Serialize(data, _wrapOptions);
            // CRITICAL: Clean all carriage returns and escaped sequences to prevent double-escaping
            // The JsonSerializer escapes \r and \n to \\r and \\n.
            // If we don't remove them here, the outer WriteResponse will escape them again to \\\\r and \\\\n.
            // This multi-pass cleanup ensures clean JSON output:
            // 1. Remove escaped carriage returns (\r) - both literal and escaped forms
            // 2. Normalize line endings to single \n
            json = json
                .Replace("\\r\\n", "\n")  // Escaped CRLF → LF
                .Replace("\\r", "")        // Escaped CR → remove
                .Replace("\r\n", "\n")     // Literal CRLF → LF
                .Replace("\r", "");        // Literal CR → remove
            return new { content = new[] { new { type = "text", text = json } } };
        }

        private static McpMessage Err(object? id, int code, string msg) =>
            new McpMessage { Id = id, Error = new ErrorObject { Code = code, Message = msg } };

        private static Tool MakeTool(string name, string desc, object schema) =>
            new Tool { Name = name, Description = desc, InputSchema = schema };

        private static string? GetStr(CallToolParams p, string key)
        {
            if (p.Arguments?.TryGetValue(key, out var v) == true)
                return v is JsonElement je ? je.GetString() : v?.ToString();
            return null;
        }

        private static int GetInt(CallToolParams p, string key, int def)
        {
            if (p.Arguments?.TryGetValue(key, out var v) == true)
            {
                var raw = v is JsonElement je ? je.GetRawText() : v?.ToString();
                if (int.TryParse(raw, out var n)) return n;
            }
            return def;
        }

        private static bool GetBool(CallToolParams p, string key, bool def)
        {
            if (p.Arguments?.TryGetValue(key, out var v) == true)
            {
                if (v is JsonElement je)
                    return je.GetBoolean();
                if (bool.TryParse(v?.ToString(), out var b))
                    return b;
            }
            return def;
        }
    }
}

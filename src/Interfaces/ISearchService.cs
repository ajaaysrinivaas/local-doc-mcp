using System.Collections.Generic;
using DocumentRagMcpServer.Models;

namespace DocumentRagMcpServer.Interfaces
{
    /// <summary>
    /// Service for searching documents by keyword.
    /// Handles relevance scoring and result caching.
    /// </summary>
    public interface ISearchService
    {
        /// <summary>Search documents for matching sections. Optionally scope to a single document and cap results.</summary>
        List<SearchResult> Search(string query, DocumentIndex index, string? documentId = null, int limit = 5);

        /// <summary>Clear the search result cache.</summary>
        void ClearCache();
    }
}

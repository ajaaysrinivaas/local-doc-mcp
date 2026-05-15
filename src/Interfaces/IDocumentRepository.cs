using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DocumentRagMcpServer.Models;

namespace DocumentRagMcpServer.Interfaces
{
    public interface IDocumentRepository : IDisposable
    {
        Task<DocumentIndex> LoadIndexAsync();
        Task<DocumentIndex> RebuildIndexAsync();
        List<(DocumentSection section, string id, string type)> GetAllSectionsWithIds(DocumentIndex index);

        /// <summary>
        /// Get page content directly from source document files.
        /// Returns list of sections found on the requested page, or empty list if page not found.
        /// </summary>
        Task<List<(string content, string title, string documentId)>> GetPageFromSourceAsync(int pageNumber, string? fileId = null);
    }
}

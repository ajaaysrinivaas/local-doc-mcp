using System.Threading.Tasks;
using DocumentRagMcpServer.Models;

namespace DocumentRagMcpServer.Parsers
{
    public interface IDocumentParser
    {
        DocumentType DocumentType { get; }
        bool CanParse(DocumentConfig config);
        Task<DocumentIndex> ParseAsync(DocumentConfig config);
    }
}

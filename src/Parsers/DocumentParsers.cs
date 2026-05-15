using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DocumentRagMcpServer.Models;

namespace DocumentRagMcpServer.Parsers
{
    /// <summary>
    /// Generic parser for all text documents.
    /// Uses linear extraction fallback logic: Bold -> Pattern -> FixedSize.
    /// </summary>
    public class GenericDocumentParser : BaseDocumentParser
    {
        public override DocumentType DocumentType => DocumentType.Generic;

        public override async Task<DocumentIndex> ParseAsync(DocumentConfig config)
        {
            var content = await ReadFileAsync(config.FilePath);
            var sections = ExtractSections(content, config);
            var frontMatter = BuildFrontMatter(config, content);
            return CreateDocumentIndex(sections, frontMatter);
        }

        protected DocumentSection BuildFrontMatter(DocumentConfig config, string content)
        {
            var title = config.Meta?.GetValueOrDefault("title") ?? Path.GetFileNameWithoutExtension(config.FilePath);
            return new DocumentSection
            {
                Title = title,
                Content = "",
                Keywords = new List<string>(),
                Summary = ""
            };
        }
    }

    /// <summary>
    /// Fallback document type detector.
    /// Since we only have a Generic parser now, this always returns Generic.
    /// </summary>
    public static class DocumentTypeDetector
    {
        public static DocumentType Detect(string filePath)
        {
            return DocumentType.Generic;
        }
    }
}

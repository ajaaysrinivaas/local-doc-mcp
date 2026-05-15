using System;
using System.Collections.Generic;
using DocumentRagMcpServer.Models;

namespace DocumentRagMcpServer.Parsers
{
    public class ParserFactory
    {
        private readonly IDocumentParser _genericParser;

        public ParserFactory()
        {
            _genericParser = new GenericDocumentParser();
        }

        public IDocumentParser GetParser(DocumentConfig config)
        {
            return _genericParser;
        }
    }
}

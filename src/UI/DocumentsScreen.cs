using System;
using System.IO;
using System.Linq;
using DocumentRagMcpServer.Config;

namespace DocumentRagMcpServer.UI
{
    internal class DocumentsScreen
    {
        private readonly string _rootPath;
        private readonly ParsingConfigLoader _configLoader;

        internal DocumentsScreen(string rootPath, ParsingConfigLoader configLoader)
        {
            _rootPath = rootPath;
            _configLoader = configLoader;
        }

        internal void Show()
        {
            MenuHelpers.SafeClear();
            Console.WriteLine("═ Documents ═\n");

            var docsPath = Path.Combine(_rootPath, "documents");
            var rawPath = Path.Combine(_rootPath, "raw");

            var txtFiles = Directory.Exists(docsPath)
                ? Directory.GetFiles(docsPath, "*.txt").OrderBy(f => f).ToArray()
                : Array.Empty<string>();

            var pdfFiles = Directory.Exists(rawPath)
                ? Directory.GetFiles(rawPath, "*.pdf").OrderBy(f => f).ToArray()
                : Array.Empty<string>();

            if (txtFiles.Length == 0 && pdfFiles.Length == 0)
            {
                Console.WriteLine("No documents found.");
                Console.WriteLine($"\n  Place .txt files in:  {docsPath}");
                Console.WriteLine($"  Place PDF files in:   {rawPath}");
                return;
            }

            if (txtFiles.Length > 0)
            {
                Console.WriteLine($"Indexed ({txtFiles.Length}):");
                for (int i = 0; i < txtFiles.Length; i++)
                {
                    var name = Path.GetFileName(txtFiles[i]);
                    var size = MenuHelpers.FormatBytes(new FileInfo(txtFiles[i]).Length);
                    var config = _configLoader.LoadDocumentConfig(name);
                    var pages = MenuHelpers.FormatPageStatus(config?.PageRange);
                    Console.WriteLine($"  {i + 1}. {name} ({size}) {pages}");
                }
                Console.WriteLine();
            }

            if (pdfFiles.Length > 0)
            {
                Console.WriteLine($"PDFs in /raw ({pdfFiles.Length}):");
                for (int i = 0; i < pdfFiles.Length; i++)
                {
                    var name = Path.GetFileName(pdfFiles[i]);
                    var size = MenuHelpers.FormatBytes(new FileInfo(pdfFiles[i]).Length);
                    var converted = File.Exists(Path.Combine(docsPath,
                        Path.GetFileNameWithoutExtension(name) + ".txt"));
                    Console.WriteLine($"  {i + 1}. {name} ({size}) [{(converted ? "✓ converted" : "pending")}]");
                }
            }
        }
    }
}

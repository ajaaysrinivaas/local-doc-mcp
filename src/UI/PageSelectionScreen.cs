using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentRagMcpServer.Config;
using DocumentRagMcpServer.Models;

namespace DocumentRagMcpServer.UI
{
    internal class PageSelectionScreen
    {
        private readonly string _rootPath;
        private readonly ParsingConfigLoader _configLoader;

        internal PageSelectionScreen(string rootPath, ParsingConfigLoader configLoader)
        {
            _rootPath = rootPath;
            _configLoader = configLoader;
        }

        internal async Task ShowAsync()
        {
            MenuHelpers.SafeClear();
            Console.WriteLine("═ Page Selection ═\n");

            var docsPath = Path.Combine(_rootPath, "documents");
            var files = Directory.Exists(docsPath)
                ? Directory.GetFiles(docsPath, "*.txt").OrderBy(f => f).ToArray()
                : Array.Empty<string>();

            if (files.Length == 0)
            {
                Console.WriteLine("No documents in /documents.");
                return;
            }

            for (int i = 0; i < files.Length; i++)
            {
                var name = Path.GetFileName(files[i]);
                var config = _configLoader.LoadDocumentConfig(name);
                var pages = MenuHelpers.FormatPageStatus(config?.PageRange);
                Console.WriteLine($"  {i + 1}. {name} {pages}");
            }

            var choice = MenuHelpers.Prompt("\nDocument number (0 to cancel): ");
            if (!int.TryParse(choice, out int idx) || idx < 1 || idx > files.Length) return;

            await ConfigureDocumentAsync(Path.GetFileName(files[idx - 1]));
        }

        private async Task ConfigureDocumentAsync(string fileName)
        {
            var existing = _configLoader.LoadDocumentConfig(fileName);
            var currentPages = existing?.PageRange != null
                ? MenuHelpers.FormatPageStatus(existing.PageRange)
                : "(all pages)";

            Console.WriteLine($"\n{fileName}");
            Console.WriteLine($"  Current: {currentPages}");
            Console.WriteLine("\n  Format:  12, 15-19, 25-27, 32   or   leave blank for all pages");

            var input = MenuHelpers.Prompt("Pages: ");

            var docConfig = existing ?? new DocumentConfig();

            if (string.IsNullOrWhiteSpace(input))
            {
                docConfig.PageRange = null;
                _configLoader.SaveDocumentConfig(fileName, docConfig);
                Console.WriteLine("✓ Cleared — will index all pages.");
            }
            else
            {
                var parsed = MenuHelpers.ParsePageSelection(input);
                if (parsed == null)
                {
                    Console.WriteLine("✗ Invalid format.");
                    return;
                }
                docConfig.PageRange = parsed;
                _configLoader.SaveDocumentConfig(fileName, docConfig);
                Console.WriteLine($"✓ Saved: {MenuHelpers.FormatPageStatus(parsed)}");
            }

            await Task.CompletedTask;
        }
    }
}

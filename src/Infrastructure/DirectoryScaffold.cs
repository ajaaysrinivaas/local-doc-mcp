using System;
using System.IO;
using System.Threading.Tasks;

namespace DocumentRagMcpServer.Infrastructure
{
    /// <summary>
    /// Handles automatic directory structure creation and validation.
    /// Ensures the following structure exists:
    ///   /documents  - Parsed .txt files
    ///   /raw        - PDF files (for PDF parser)
    ///   /config     - Per-document configuration files
    ///   /.index     - Index cache
    /// </summary>
    public class DirectoryScaffold
    {
        public string RootPath { get; }
        public string DocumentsPath { get; }
        public string RawPath { get; }
        public string ConfigPath { get; }
        public string IndexPath { get; }

        public DirectoryScaffold(string rootPath)
        {
            RootPath = Path.GetFullPath(rootPath);
            DocumentsPath = Path.Combine(RootPath, "documents");
            RawPath = Path.Combine(RootPath, "raw");
            ConfigPath = Path.Combine(RootPath, "config");
            IndexPath = Path.Combine(RootPath, ".index");
        }

        /// <summary>
        /// Creates all necessary directories if they don't exist.
        /// </summary>
        public Task<bool> ScaffoldAsync()
        {
            try
            {
                Console.Error.WriteLine("  [DirectoryScaffold] Creating directories...");
                Directory.CreateDirectory(DocumentsPath);
                Console.Error.WriteLine($"    ✓ Created: {DocumentsPath}");

                Directory.CreateDirectory(RawPath);
                Console.Error.WriteLine($"    ✓ Created: {RawPath}");

                Directory.CreateDirectory(ConfigPath);
                Console.Error.WriteLine($"    ✓ Created: {ConfigPath}");

                Directory.CreateDirectory(IndexPath);
                Console.Error.WriteLine($"    ✓ Created: {IndexPath}");

                Console.Error.WriteLine("✓ Directory structure created/validated:");
                Console.Error.WriteLine($"  Root:      {RootPath}");
                Console.Error.WriteLine($"  Documents: {DocumentsPath}");
                Console.Error.WriteLine($"  Raw:       {RawPath}");
                Console.Error.WriteLine($"  Config:    {ConfigPath}");
                Console.Error.WriteLine($"  Index:     {IndexPath}");
                Console.Error.WriteLine();

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"✗ Failed to create directories: {ex.Message}");
                Console.Error.WriteLine($"  Exception type: {ex.GetType().Name}");
                return Task.FromResult(false);
            }
        }

    }
}

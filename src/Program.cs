using System;
using System.IO;
using System.Threading.Tasks;
using DocumentRagMcpServer.Config;
using DocumentRagMcpServer.Infrastructure;
using DocumentRagMcpServer.Interfaces;
using DocumentRagMcpServer.MCP;
using DocumentRagMcpServer.Repositories;
using DocumentRagMcpServer.Services;
using DocumentRagMcpServer.UI;

namespace DocumentRagMcpServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                var (rootPath, skipSetup, forceRebuild) = ParseArgs(args);

                Console.Error.WriteLine("Document RAG MCP Server");
                Console.Error.WriteLine($"Working directory: {rootPath}");
                Console.Error.WriteLine("PDF requirements: pip install -r requirements.txt");
                Console.Error.WriteLine();

                // Initialize directory structure
                var scaffold = new DirectoryScaffold(rootPath);
                await scaffold.ScaffoldAsync();

                // Extract embedded assets to /raw/ (alongside pdf_parser.py)
                var rawPath = Path.Combine(rootPath, "raw");
                var documentsPath = Path.Combine(rootPath, "documents");
                await ResourceExtractor.ExtractPdfParserAsync(rawPath);
                await ResourceExtractor.ExtractPreprocessorAsync(rawPath);

                // Load/create config
                var configLoader = new ParsingConfigLoader(Path.Combine(rootPath, "config"));
                var config = configLoader.LoadConfig();

                // Check if setup is needed (no documents and not skipping setup)
                var hasDocuments = Directory.Exists(documentsPath) &&
                                   Directory.GetFiles(documentsPath, "*.txt").Length > 0;

                // Detect if we have an interactive console available
                var hasConsole = IsConsoleAvailable();

                // Only show setup UI if explicitly requested AND console is available
                var shouldShowSetup = !skipSetup && hasConsole;

                // Run setup/control panel if needed
                if (shouldShowSetup)
                {
                    Console.Error.WriteLine("Initializing document repository...");
                    Console.Error.WriteLine();
                    try
                    {
                        using (IDocumentRepository repository = new DocumentRepository(rootPath, forceRebuild))
                        {
                            var controlPanel = new ControlPanel(rootPath, configLoader, repository);
                            await controlPanel.RunAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error initializing repository: {ex.Message}");
                        Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                        throw;
                    }
                    Console.Error.WriteLine();
                }
                else if (!hasConsole && !skipSetup)
                {
                    Console.Error.WriteLine("(Skipping interactive setup: no console available - running in server mode)");
                    Console.Error.WriteLine();
                }

                // Load repository and start MCP server
                Console.Error.WriteLine("Loading index from disk...");
                IDocumentRepository mcpRepository = new DocumentRepository(rootPath, forceRebuild);
                ISearchService searchService = new SearchService(mcpRepository);
                var messageHandler = new McpMessageHandler(mcpRepository, searchService);

                await messageHandler.InitializeAsync();
                Console.Error.WriteLine("Ready. Listening on stdin/stdout.");
                Console.Error.WriteLine();

                IMcpServer server = new McpServer(messageHandler);
                await server.RunAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.Error.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                Environment.Exit(1);
            }
        }

        private static (string rootPath, bool skipSetup, bool forceRebuild) ParseArgs(string[] args)
        {
            string rootPath = Environment.CurrentDirectory;
            bool skipSetup = false;
            bool forceRebuild = false;

            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "--root" || args[i] == "--data-path" || args[i] == "--config-path") && i + 1 < args.Length)
                    rootPath = args[++i];
                else if (args[i] == "--server" || args[i] == "--no-setup")
                    skipSetup = true;
                else if (args[i] == "--force-rebuild" || args[i] == "--rebuild")
                    forceRebuild = true;
            }

            return (rootPath, skipSetup, forceRebuild);
        }

        /// <summary>
        /// Detects whether an interactive console is available.
        /// Returns false when running as MCP server or with redirected I/O.
        /// </summary>
        private static bool IsConsoleAvailable()
        {
            try
            {
                // Try to access the console window handle
                // This will throw if running without an interactive console
                int height = Console.WindowHeight;
                int width = Console.WindowWidth;

                // If we got here, console is available
                return true;
            }
            catch (InvalidOperationException)
            {
                // No console available (typical for MCP server mode)
                return false;
            }
            catch
            {
                // Unknown error - assume console is not reliable
                return false;
            }
        }
    }
}

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DocumentRagMcpServer.Infrastructure
{
    /// <summary>
    /// Extracts embedded resources from the assembly to disk.
    /// </summary>
    public class ResourceExtractor
    {
        public static Task<string?> ExtractPdfParserAsync(string outputDir) =>
            ExtractAsync("DocumentRagMcpServer.Assets.pdf_parser.py", outputDir, "pdf_parser.py");

        public static Task<string?> ExtractPreprocessorAsync(string outputDir) =>
            ExtractAsync("DocumentRagMcpServer.Assets.preprocess_documents.py", outputDir, "preprocess_documents.py");

        private static async Task<string?> ExtractAsync(string resourceName, string outputDir, string fileName)
        {
            try
            {
                Directory.CreateDirectory(outputDir);
                var outputPath = Path.Combine(outputDir, fileName);

                if (File.Exists(outputPath))
                    return outputPath;

                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    Console.Error.WriteLine($"Warning: Could not find embedded resource: {resourceName}");
                    return null;
                }

                using var fileStream = File.Create(outputPath);
                await stream.CopyToAsync(fileStream);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    try
                    {
                        File.SetAttributes(outputPath, FileAttributes.Normal);
                        var psi = new System.Diagnostics.ProcessStartInfo("chmod", $"+x {outputPath}")
                        {
                            UseShellExecute = false
                        };
                        using var proc = System.Diagnostics.Process.Start(psi);
                        proc?.WaitForExit();
                    }
                    catch { }
                }

                Console.Error.WriteLine($"✓ Extracted {fileName} to: {outputPath}");
                return outputPath;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Failed to extract {fileName}: {ex.Message}");
                return null;
            }
        }
    }
}


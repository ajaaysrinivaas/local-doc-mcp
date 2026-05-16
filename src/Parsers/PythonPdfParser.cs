using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DocumentRagMcpServer.Parsers
{
    /// <summary>
    /// Wrapper for calling external Python PDF parser script.
    /// Converts PDF files from /raw/ to .txt files in /documents/.
    /// </summary>
    public class PythonPdfParser
    {
        private readonly string _pythonScriptPath;
        private readonly string _rawPath;
        private readonly string _documentsPath;

        public PythonPdfParser(string pythonScriptPath, string rawPath, string documentsPath)
        {
            _pythonScriptPath = pythonScriptPath;
            _rawPath = rawPath;
            _documentsPath = documentsPath;
        }

        /// <summary>
        /// Converts a PDF file using the Python parser script.
        /// Returns the output text, or null if conversion failed.
        /// </summary>
        public async Task<string?> ParsePdfAsync(string pdfFilePath)
        {
            try
            {
                var scriptPath = Path.GetFullPath(_pythonScriptPath);
                if (!File.Exists(scriptPath))
                {
                    Console.Error.WriteLine($"  Warning: Python parser script not found at {scriptPath}");
                    return null;
                }

                var fullPdfPath = Path.GetFullPath(pdfFilePath);
                if (!File.Exists(fullPdfPath))
                {
                    Console.Error.WriteLine($"  Warning: PDF file not found: {fullPdfPath}");
                    return null;
                }

                var pythonExe = GetPythonExecutable();
                if (string.IsNullOrWhiteSpace(pythonExe))
                {
                    Console.Error.WriteLine("  Warning: Python not found in PATH. Please install Python 3.");
                    return null;
                }

                var processInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{scriptPath}\" \"{fullPdfPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    Console.Error.WriteLine("  Failed to start Python parser process");
                    return null;
                }

                // Start both reads concurrently before waiting — prevents OS buffer deadlock
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                var output = await outputTask;
                var error = await errorTask;

                if (process.ExitCode != 0)
                {
                    Console.Error.WriteLine($"  Python parser failed (exit code {process.ExitCode}): {error}");
                    return null;
                }

                return string.IsNullOrWhiteSpace(output) ? null : output;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  PDF parsing error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Converts all PDF files in /raw/ to /documents/.
        /// Reports progress and errors for each file.
        /// </summary>
        public async Task<(int successCount, int failureCount, List<(string file, string error)> errors)> ConvertAllPdfsAsync()
        {
            var errors = new List<(string, string)>();
            int successCount = 0;
            int failureCount = 0;

            if (!Directory.Exists(_rawPath))
                return (successCount, failureCount, errors);

            Directory.CreateDirectory(_documentsPath);

            var pdfFiles = Directory.GetFiles(_rawPath, "*.pdf");
            if (pdfFiles.Length == 0)
                return (successCount, failureCount, errors);

            Console.Error.WriteLine("Converting PDF files to text...");

            foreach (var pdfFile in pdfFiles)
            {
                var fileName = Path.GetFileName(pdfFile);
                var outputPath = Path.Combine(_documentsPath, Path.GetFileNameWithoutExtension(pdfFile) + ".txt");

                // Skip if already converted and PDF hasn't changed
                if (File.Exists(outputPath) &&
                    File.GetLastWriteTimeUtc(outputPath) >= File.GetLastWriteTimeUtc(pdfFile))
                {
                    Console.Error.WriteLine($"  [{fileName}] Already converted (using cache)");
                    successCount++;
                    continue;
                }

                Console.Error.WriteLine($"  [{fileName}] Converting...");

                try
                {
                    var content = await ParsePdfAsync(pdfFile);
                    if (content != null)
                    {
                        await File.WriteAllTextAsync(outputPath, content, System.Text.Encoding.UTF8);
                        Console.Error.WriteLine($"  [{fileName}] ✓ Success");
                        successCount++;
                    }
                    else
                    {
                        var errorMsg = "No output generated";
                        Console.Error.WriteLine($"  [{fileName}] ✗ Failed: {errorMsg}");
                        errors.Add((fileName, errorMsg));
                        failureCount++;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  [{fileName}] ✗ Failed: {ex.Message}");
                    errors.Add((fileName, ex.Message));
                    failureCount++;
                }
            }

            return (successCount, failureCount, errors);
        }

        private static string? GetPythonExecutable()
        {
            // Try each candidate by running it directly — works in venv, PowerShell, CMD, and any shell
            // Avoids 'where'/'which' which are unreliable (PowerShell aliases, venv, etc.)
            foreach (var cmd in new[] { "python3", "python", "py" })
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc?.WaitForExit(3000) == true && proc.ExitCode == 0) return cmd;
                }
                catch { }
            }
            return null;
        }

    }
}

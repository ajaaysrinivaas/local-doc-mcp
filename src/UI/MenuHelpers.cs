using System;
using System.IO;
using DocumentRagMcpServer.Models;

namespace DocumentRagMcpServer.UI
{
    internal static class MenuHelpers
    {
        internal static string Prompt(string text)
        {
            Console.Write(text);
            return (Console.ReadLine() ?? "").Trim();
        }

        internal static void PressAnyKey()
        {
            Console.WriteLine("\nPress any key to continue...");
            try
            {
                Console.ReadKey(true);
            }
            catch (InvalidOperationException)
            {
                // No console available - just wait a bit
                System.Threading.Thread.Sleep(500);
            }
            SafeClear();
        }

        /// <summary>
        /// Safely clears the console if available.
        /// When running as MCP server without a console handle, this gracefully does nothing.
        /// </summary>
        internal static void SafeClear()
        {
            try
            {
                Console.Clear();
            }
            catch (InvalidOperationException)
            {
                // Console not available (e.g., when running as MCP server with redirected stdout)
                // Write a visual separator instead
                Console.Error.WriteLine("\n" + new string('─', 40) + "\n");
            }
        }

        internal static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
            return $"{len:0.##} {sizes[order]}";
        }

        internal static string FormatPageStatus(PageRange? pageRange)
        {
            if (pageRange == null) return "";

            if (pageRange.Only?.Count > 0)
            {
                var pageList = string.Join(", ", System.Linq.Enumerable.Take(
                    System.Linq.Enumerable.OrderBy(pageRange.Only, p => p), 5));
                if (pageRange.Only.Count > 5)
                    pageList += $", +{pageRange.Only.Count - 5}";
                return $"[pages: {pageList}]";
            }

            if (pageRange.Start > 1 || pageRange.End.HasValue)
            {
                var end = pageRange.End.HasValue ? pageRange.End.ToString() : "end";
                return $"[pages {pageRange.Start}-{end}]";
            }

            return "";
        }

        /// <summary>
        /// Parses user input like "12, 15-19, 25-27, 32" into a PageRange.
        /// Returns null if format is invalid.
        /// </summary>
        internal static PageRange? ParsePageSelection(string input)
        {
            var pageSet = new System.Collections.Generic.SortedSet<int>();

            foreach (var part in input.Split(','))
            {
                var trimmed = part.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                if (trimmed.Contains('-'))
                {
                    var sides = trimmed.Split('-');
                    if (sides.Length != 2) return null;
                    if (!int.TryParse(sides[0].Trim(), out int s) || s < 1) return null;
                    if (!int.TryParse(sides[1].Trim(), out int e) || e < s) return null;
                    for (int i = s; i <= e; i++) pageSet.Add(i);
                }
                else
                {
                    if (!int.TryParse(trimmed, out int p) || p < 1) return null;
                    pageSet.Add(p);
                }
            }

            return pageSet.Count == 0 ? null : new PageRange { Only = new System.Collections.Generic.List<int>(pageSet) };
        }
    }
}


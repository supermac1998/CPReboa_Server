using CPReboaReporting;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;



namespace CPReboaReporting
{
    internal static class Program
    {
        static async Task<int> Main(string[] args)
        {

            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.EnableDebugging = true;

            string debugDir = @"C:\temp";
            string debugPath = Path.Combine(debugDir, "cpreboa_debug.txt");

            Directory.CreateDirectory(debugDir);

            string? casePath = null;
            string? username = null;

            try
            {
                await File.AppendAllTextAsync(debugPath,
                    $"STARTED at {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                    $"ARGS: {string.Join(" | ", args)}{Environment.NewLine}");

                var parsed = ParseArguments(args);

                parsed.TryGetValue("--case", out casePath);
                parsed.TryGetValue("--user", out username);

                await File.AppendAllTextAsync(debugPath,
                    $"Parsed casePath = {casePath}{Environment.NewLine}" +
                    $"Parsed username = {username}{Environment.NewLine}");

                if (string.IsNullOrWhiteSpace(casePath))
                {
                    await File.AppendAllTextAsync(debugPath, "Missing required argument: --case" + Environment.NewLine);
                    return 1;
                }

                if (string.IsNullOrWhiteSpace(username))
                {
                    await File.AppendAllTextAsync(debugPath, "Missing required argument: --user" + Environment.NewLine);
                    return 1;
                }

                if (!Directory.Exists(casePath))
                {
                    await File.AppendAllTextAsync(debugPath, $"Case directory not found: {casePath}{Environment.NewLine}");
                    return 1;
                }

                string redcapCsvPath = Path.Combine(casePath, "redcap.csv");
                await File.AppendAllTextAsync(debugPath, $"Looking for redcap.csv at: {redcapCsvPath}{Environment.NewLine}");

                if (!File.Exists(redcapCsvPath))
                {
                    await File.AppendAllTextAsync(debugPath, $"redcap.csv not found: {redcapCsvPath}{Environment.NewLine}");
                    return 1;
                }

                var result = await RedcapMonitorReportGenerator.GenerateFromRedcapCsvAsync(redcapCsvPath, username);

                await File.AppendAllTextAsync(debugPath,
                    $"Generator returned CSV path: {result.MonitorTableCsvPath}{Environment.NewLine}" +
                    $"Generator returned PDF path: {result.PdfPath}{Environment.NewLine}");

                if (!File.Exists(result.PdfPath))
                {
                    await File.AppendAllTextAsync(debugPath,
                        $"PDF was not generated at expected path: {result.PdfPath}{Environment.NewLine}");
                    return 1;
                }

                await File.AppendAllTextAsync(debugPath,
                    $"SUCCESS: PDF generated at {result.PdfPath}{Environment.NewLine}");

                return 0;
            }
            catch (Exception ex)
            {
                await File.AppendAllTextAsync(debugPath,
                    $"EXCEPTION at {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{ex}{Environment.NewLine}");

                if (!string.IsNullOrWhiteSpace(casePath) && Directory.Exists(casePath))
                {
                    string caseLogPath = Path.Combine(casePath, "report_generation.log");
                    await File.AppendAllTextAsync(caseLogPath,
                        $"EXCEPTION at {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{ex}{Environment.NewLine}");
                }

                return 2;
            }
        }

        private static Dictionary<string, string> ParseArguments(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--"))
                    continue;

                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    result[args[i]] = args[i + 1];
                    i++;
                }
                else
                {
                    result[args[i]] = string.Empty;
                }
            }

            return result;
        }
    }
}


/*using QuestPDF.Infrastructure;
using CPReboaReporting;
using System;
using System.IO;
using System.Threading.Tasks;

QuestPDF.Settings.License = LicenseType.Community;
QuestPDF.Settings.EnableDebugging = true;


namespace CPReboaReporting
{
    internal static class Program
    {
        static async Task<int> Main(string[] args)
        {
            try
            {
                var parsed = ParseArguments(args);

                if (!parsed.TryGetValue("--case", out var casePath) || string.IsNullOrWhiteSpace(casePath))
                {
                    Console.Error.WriteLine("Missing required argument: --case");
                    return 1;
                }

                if (!parsed.TryGetValue("--user", out var username) || string.IsNullOrWhiteSpace(username))
                {
                    Console.Error.WriteLine("Missing required argument: --user");
                    return 1;
                }

                if (!Directory.Exists(casePath))
                {
                    Console.Error.WriteLine($"Case directory not found: {casePath}");
                    return 1;
                }

                string redcapCsvPath = Path.Combine(casePath, "redcap.csv");

                if (!File.Exists(redcapCsvPath))
                {
                    Console.Error.WriteLine($"redcap.csv not found: {redcapCsvPath}");
                    return 1;
                }

                Console.WriteLine($"Starting report generation for case: {casePath}");
                Console.WriteLine($"User: {username}");

                var result = await RedcapMonitorReportGenerator.GenerateFromRedcapCsvAsync(redcapCsvPath, username);

                Console.WriteLine("Report generation completed successfully.");
                Console.WriteLine($"Monitor CSV: {result.MonitorTableCsvPath}");
                Console.WriteLine($"PDF: {result.PdfPath}");

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Fatal error:");
                Console.Error.WriteLine(ex.ToString());
                return 2;
            }
        }

        private static System.Collections.Generic.Dictionary<string, string> ParseArguments(string[] args)
        {
            var result = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < args.Length; i++)
            {
                string current = args[i];

                if (!current.StartsWith("--"))
                    continue;

                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    result[current] = args[i + 1];
                    i++;
                }
                else
                {
                    result[current] = string.Empty;
                }
            }

            return result;
        }
    }
}*/


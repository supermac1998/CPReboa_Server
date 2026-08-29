using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CPReboaReporting
{

    // DOCUMENT ID
    // [STUDY_ACRONYM]-[DOC_TYPE]-[ID]-[VERSION]
    public static class DocumentIdGenerator
    {
        public static string Generate(
            string studyAcronym,
            string docType,
            string caseId,
            int version = 1,
            DateTime? date = null)
        {
            var safeStudy = Normalize(studyAcronym);
            var safeDocType = Normalize(docType);
            var safeCaseId = Normalize(caseId);

            var dt = date ?? DateTime.Now;
            var datePart = dt.ToString("yyyyMMdd");

            var versionPart = $"V{version:00}";

            return $"{safeStudy}-{safeDocType}-{safeCaseId}-{datePart}-{versionPart}";
        }

        private static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "UNKNOWN";

            return input.Trim().ToUpper().Replace(" ", "");
        }
    }

    public sealed class ChoiceOption
    {
        public string Value { get; set; } = "";
        public string Label { get; set; } = "";
    };

    

    public sealed class MonitorTimepointRow
    {
        public string Timepoint { get; set; } = "";
        public string? Timestamp { get; set; }

        public string? SchleuseBpSyst { get; set; }
        public string? SchleuseBpMean { get; set; }
        public string? SchleuseBpDiast { get; set; }

        public string? ReboaBpSyst { get; set; }
        public string? ReboaBpMean { get; set; }
        public string? ReboaBpDiast { get; set; }

        public string? NirsLeft { get; set; }
        public string? NirsRight { get; set; }

        public string? EtCo2 { get; set; }
    }

    public sealed class BloodDrawTimeRow
    {
        public string Timepoint { get; set; } = "";
        public string? BloodDrawTime { get; set; }
    }

    public static class TimepointMapping
    {
        public static readonly (string Label, string Suffix)[] Timepoints =
        {
            ("-10", "10"),
            ("T0", "t0"),
            ("T1", "t1"),
            ("T2", "t2"),
            ("T3", "t3"),
            ("T4", "t4"),
            ("T5", "t5"),
            ("TI", "ti")
        };

        public static string? GetTimestamp(Dictionary<string, string> lookup, string suffix)
        {
            if (lookup.TryGetValue($"dt_{suffix}", out var value))
                return FormatTimestamp(value);

            if (lookup.TryGetValue($"pc_timestamp_{suffix}", out var pc))
                return FormatTimestamp(pc);

            return null;
        }

        private static string FormatTimestamp(string value)
        {
            if (DateTime.TryParseExact(
                    value,
                    "d/M/yyyy HH:mm:ss",   
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var dt))
            {
                return dt.ToString("dd-MM-yyyy HH:mm:ss");
            }

            return value;
        }
    }

    public static class RepeatTimepointMapping
    {
        public static readonly (string Label, string Suffix)[] Timepoints =
        {
        ("T1", "t1_2"),
        ("T2", "t2_2"),
        ("T3", "t3_2"),
        ("T4", "t4_2"),
        ("T5", "t5_2")
        };

        public static string? GetTimestamp(Dictionary<string, string> lookup, string suffix)
        {
            if (lookup.TryGetValue($"dt_{suffix}", out var value))
                return FormatTimestamp(value);

            if (lookup.TryGetValue($"pc_timestamp_{suffix}", out var pc))
                return FormatTimestamp(pc);

            return null;
        }

        private static string FormatTimestamp(string value)
        {
            if (DateTime.TryParseExact(
                    value,
                    "d/M/yyyy HH:mm:ss", 
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var dt))
            {
                return dt.ToString("dd-MM-yyyy HH:mm:ss");
            }

            return value;
        }
    }

    public static class Repeat3TimepointMapping
    {
        public static readonly (string Label, string Suffix)[] Timepoints =
        {
        ("T1", "t1_3"),
        ("T2", "t2_3"),
        ("T3", "t3_3"),
        ("T4", "t4_3"),
        ("T5", "t5_3")
    };

        public static string? GetTimestamp(Dictionary<string, string> lookup, string suffix)
        {
            if (lookup.TryGetValue($"dt_{suffix}", out var value))
                return FormatTimestamp(value);

            if (lookup.TryGetValue($"pc_timestamp_{suffix}", out var pc))
                return FormatTimestamp(pc);

            return null;
        }

        private static string FormatTimestamp(string value)
        {
            if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt))
            {
                return dt.ToLocalTime().ToString("dd-MM-yyyy HH:mm:ss");
            }

            return value;
        }
    }

    public static class MonitorTimepointTableGenerator
    {
        private static string? FormatTimestamp(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (DateTime.TryParseExact(
                    value,
                    "d/M/yyyy HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var dt))
            {
                return dt.ToString("dd-MM-yyyy HH:mm:ss");
            }

            if (DateTime.TryParse(value, out dt))
                return dt.ToString("dd-MM-yyyy HH:mm:ss");

            return value;
        }

        public static async Task<string> GenerateMonitorTableCsvAsync(string caseDir, Dictionary<string, string> lookup)
        {
            if (string.IsNullOrWhiteSpace(caseDir))
                throw new ArgumentException("Case directory is required.", nameof(caseDir));

            Directory.CreateDirectory(caseDir);

            var rows = BuildRows(lookup);
            var outputPath = Path.Combine(caseDir, "monitor_table.csv");

            var lines = new List<string>
            {
                string.Join(",",
                    EscapeCsv("TP"),
                    EscapeCsv("Timestamp"),
                    EscapeCsv("Schleuse BP syst"),
                    EscapeCsv("Schleuse BP mean"),
                    EscapeCsv("Schleuse BP diast"),
                    EscapeCsv("REBOA BP syst"),
                    EscapeCsv("REBOA BP mean"),
                    EscapeCsv("REBOA BP diast"),
                    EscapeCsv("NIRS Left"),
                    EscapeCsv("NIRS Right"),
                    EscapeCsv("etCO2"))
            };

            foreach (var row in rows)
            {
                lines.Add(string.Join(",",
                    EscapeCsv(row.Timepoint),
                    EscapeCsv(row.Timestamp ?? ""),
                    EscapeCsv(row.SchleuseBpSyst ?? ""),
                    EscapeCsv(row.SchleuseBpMean ?? ""),
                    EscapeCsv(row.SchleuseBpDiast ?? ""),
                    EscapeCsv(row.ReboaBpSyst ?? ""),
                    EscapeCsv(row.ReboaBpMean ?? ""),
                    EscapeCsv(row.ReboaBpDiast ?? ""),
                    EscapeCsv(row.NirsLeft ?? ""),
                    EscapeCsv(row.NirsRight ?? ""),
                    EscapeCsv(row.EtCo2 ?? "")
                ));
            }

            await File.WriteAllLinesAsync(outputPath, lines, Encoding.UTF8);
            return outputPath;
        }

        public static List<MonitorTimepointRow> BuildRows(Dictionary<string, string> lookup)
        {
            var rows = new List<MonitorTimepointRow>();

            foreach (var (label, suffix) in TimepointMapping.Timepoints)
            {
                rows.Add(new MonitorTimepointRow
                {
                    Timepoint = label,
                    Timestamp = TimepointMapping.GetTimestamp(lookup, suffix),

                    SchleuseBpSyst = GetValue(lookup, $"r_bp_syst_{suffix}"),
                    SchleuseBpMean = GetValue(lookup, $"r_bp_m_{suffix}"),
                    SchleuseBpDiast = GetValue(lookup, $"r_bp_diast_{suffix}"),

                    ReboaBpSyst = GetValue(lookup, $"s_bp_syst_{suffix}"),
                    ReboaBpMean = GetValue(lookup, $"s_bp_m_{suffix}"),
                    ReboaBpDiast = GetValue(lookup, $"s_bp_diast_{suffix}"),

                    NirsLeft = GetValue(lookup, $"nirs_l_{suffix}"),
                    NirsRight = GetValue(lookup, $"nirs_r_{suffix}"),
                    EtCo2 = GetValue(lookup, $"etco2_{suffix}")
                });
            }

            return rows;
        }

        public static List<MonitorTimepointRow> BuildRepeatRows(Dictionary<string, string> lookup)
        {
            var rows = new List<MonitorTimepointRow>();

            foreach (var (label, suffix) in RepeatTimepointMapping.Timepoints)
            {
                rows.Add(new MonitorTimepointRow
                {
                    Timepoint = label,
                    Timestamp = RepeatTimepointMapping.GetTimestamp(lookup, suffix),

                    SchleuseBpSyst = GetValue(lookup, $"r_bp_syst_{suffix}"),
                    SchleuseBpMean = GetValue(lookup, $"r_bp_m_{suffix}"),
                    SchleuseBpDiast = GetValue(lookup, $"r_bp_diast_{suffix}"),

                    ReboaBpSyst = GetValue(lookup, $"s_bp_syst_{suffix}"),
                    ReboaBpMean = GetValue(lookup, $"s_bp_m_{suffix}"),
                    ReboaBpDiast = GetValue(lookup, $"s_bp_diast_{suffix}"),

                    NirsLeft = GetValue(lookup, $"nirs_l_{suffix}"),
                    NirsRight = GetValue(lookup, $"nirs_r_{suffix}"),
                    EtCo2 = GetValue(lookup, $"etco2_{suffix}")
                });
            }

            return rows;
        }

        public static List<MonitorTimepointRow> BuildRepeat3Rows(Dictionary<string, string> lookup)
        {
            var rows = new List<MonitorTimepointRow>();

            foreach (var (label, suffix) in Repeat3TimepointMapping.Timepoints)
            {
                rows.Add(new MonitorTimepointRow
                {
                    Timepoint = label,
                    Timestamp = Repeat3TimepointMapping.GetTimestamp(lookup, suffix),

                    SchleuseBpSyst = GetValue(lookup, $"r_bp_syst_{suffix}"),
                    SchleuseBpMean = GetValue(lookup, $"r_bp_m_{suffix}"),
                    SchleuseBpDiast = GetValue(lookup, $"r_bp_diast_{suffix}"),

                    ReboaBpSyst = GetValue(lookup, $"s_bp_syst_{suffix}"),
                    ReboaBpMean = GetValue(lookup, $"s_bp_m_{suffix}"),
                    ReboaBpDiast = GetValue(lookup, $"s_bp_diast_{suffix}"),

                    NirsLeft = GetValue(lookup, $"nirs_l_{suffix}"),
                    NirsRight = GetValue(lookup, $"nirs_r_{suffix}"),
                    EtCo2 = GetValue(lookup, $"etco2_{suffix}")
                });
            }

            return rows;
        }

        public static List<BloodDrawTimeRow> BuildBloodDrawRows(Dictionary<string, string> lookup)
        {
            return new List<BloodDrawTimeRow>
            {
                new BloodDrawTimeRow
                {
                    Timepoint = "T0",
                    BloodDrawTime = FormatTimestamp(GetValue(lookup, "dt_bs_t0"))
                },
                new BloodDrawTimeRow
                {
                    Timepoint = "T4",
                    BloodDrawTime = FormatTimestamp(GetValue(lookup, "dt_bs_t4"))
                },
                new BloodDrawTimeRow
                {
                    Timepoint = "T5",
                    BloodDrawTime = FormatTimestamp(GetValue(lookup, "dt_bs_t5"))
                },
                new BloodDrawTimeRow
                {
                    Timepoint = "TI",
                    BloodDrawTime = FormatTimestamp(GetValue(lookup, "dt_bs_ti"))
                }
            };
        }

        private static string? GetValue(Dictionary<string, string> lookup, string key)
        {
            return lookup.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }

        private static string EscapeCsv(string value)
        {
            value ??= string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }

    public static class RedcapMonitorReportGenerator
    {
        public static async Task<(string MonitorTableCsvPath, string PdfPath)> GenerateFromRedcapCsvAsync(string redcapCsvPath, string exportedByUser)
        {
            if (string.IsNullOrWhiteSpace(redcapCsvPath))
                throw new ArgumentException("Path is required.", nameof(redcapCsvPath));

            if (!File.Exists(redcapCsvPath))
                throw new FileNotFoundException("redcap.csv not found.", redcapCsvPath);

            var caseDir = Path.GetDirectoryName(redcapCsvPath)
                         ?? throw new InvalidOperationException("Could not determine folder for redcap.csv.");

            var caseId = new DirectoryInfo(caseDir).Name;

            var physicianSignaturePath = Directory
                .GetFiles(caseDir, "signature_physician_*.png")
                .FirstOrDefault() ?? "";

            var nurseSignaturePath = Directory
                .GetFiles(caseDir, "signature_study_nurse_*.png")
                .FirstOrDefault() ?? "";


            var lookup = ReadRedcapCsvToLookup(redcapCsvPath);
            var recordId = lookup.TryGetValue("record_id", out var rid) && !string.IsNullOrWhiteSpace(rid)
                ? rid.Trim()
                : caseId;
            var documentId = DocumentIdGenerator.Generate(
                "CPREBOA",
                "REP",
                recordId,
                version: 1);
            var rows = MonitorTimepointTableGenerator.BuildRows(lookup);
            var repeatRows = MonitorTimepointTableGenerator.BuildRepeatRows(lookup);
            var repeat3Rows = MonitorTimepointTableGenerator.BuildRepeat3Rows(lookup);
            var bloodDrawRows = MonitorTimepointTableGenerator.BuildBloodDrawRows(lookup);

            var monitorTableCsvPath = await MonitorTimepointTableGenerator.GenerateMonitorTableCsvAsync(caseDir, lookup);

            var pdfPath = Path.Combine(caseDir, $"study_report.pdf");
            var logoSvgPath = Path.Combine("C:\\Users\\Alex\\Documents\\CPReboa\\Logo_UKN.svg");
            var document = new MonitorReportDocument(caseId, documentId, lookup, rows, repeatRows, repeat3Rows, bloodDrawRows, logoSvgPath, physicianSignaturePath, nurseSignaturePath, exportedByUser);


            QuestPDF.Settings.EnableDebugging = true;

            try
            {
                document.GeneratePdf(pdfPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("MESSAGE:");
                Console.WriteLine(ex.Message);
                Console.WriteLine();

                Console.WriteLine("FULL:");
                Console.WriteLine(ex.ToString());
                Console.WriteLine();

                var inner = ex.InnerException;
                while (inner != null)
                {
                    Console.WriteLine("INNER:");
                    Console.WriteLine(inner.ToString());
                    Console.WriteLine();
                    inner = inner.InnerException;
                }

                throw;
            }

            return (monitorTableCsvPath, pdfPath);
        }

        public static Dictionary<string, string> ReadRedcapCsvToLookup(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var lines = File.ReadAllLines(path);
            if (lines.Length == 0)
                return result;

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = SplitCsvLine(line);
                if (parts.Count < 2)
                    continue;

                var key = parts[0]?.Trim();
                var value = parts[1]?.Trim() ?? "";

                if (!string.IsNullOrWhiteSpace(key))
                    result[key] = value;
            }

            return result;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            result.Add(sb.ToString());
            return result;
        }
    }

    public sealed class MonitorReportDocument : IDocument
    {
        private readonly string _caseId;
        private readonly string _documentId;
        private readonly IReadOnlyDictionary<string, string> _lookup;
        private readonly IReadOnlyList<MonitorTimepointRow> _rows;
        private readonly IReadOnlyList<MonitorTimepointRow> _repeatRows;
        private readonly IReadOnlyList<MonitorTimepointRow> _repeat3Rows;
        private readonly IReadOnlyList<BloodDrawTimeRow> _bloodDrawRows;
        private readonly string _logoSvgPath;
        private readonly string _physicianSignaturePath;
        private readonly string _nurseSignaturePath;
        private readonly string _exportedByUser;

        public MonitorReportDocument(string caseId, string documentId, IReadOnlyDictionary<string, string> lookup, IReadOnlyList<MonitorTimepointRow> rows, IReadOnlyList<MonitorTimepointRow> repeatRows, IReadOnlyList<MonitorTimepointRow> repeat3Rows, IReadOnlyList<BloodDrawTimeRow> bloodDrawRows, string logoSvgPath, string physicianSignaturePath, string nurseSignaturePath, string exportedByUser)
        {
            _caseId = caseId;
            _documentId = documentId;
            _lookup = lookup;
            _rows = rows;
            _repeatRows = repeatRows;
            _repeat3Rows = repeat3Rows;
            _bloodDrawRows = bloodDrawRows;
            _logoSvgPath = logoSvgPath;
            _physicianSignaturePath = physicianSignaturePath;
            _nurseSignaturePath = nurseSignaturePath;
            _exportedByUser = exportedByUser;
        }

       

        private static string? FormatDateTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt))
            {
                return dt.ToLocalTime().ToString("dd-MM-yyyy HH:mm:ss");
            }

            return value; // fallback if parsing fails
        }

        private static bool IsSelected(string? selectedValue, string optionValue)
        {
            return string.Equals(
                selectedValue?.Trim(),
                optionValue?.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private void ComposeChoiceField(
            IContainer container,
            string fieldLabel,
            string? selectedValue,
            IReadOnlyList<ChoiceOption> options,
            int labelWidth = 180,
            int optionCircleWidth = 18,
            int optionLabelWidth = 110)
        {
            container.ShowEntire().Row(row =>
            {
                row.Spacing(8);

                row.ConstantItem(labelWidth)
                    .AlignTop()
                    .Text(fieldLabel)
                    .SemiBold();

                row.RelativeItem().Column(col =>
                {
                    col.Spacing(4);

                    foreach (var option in options)
                    {
                        col.Item().Row(optionRow =>
                        {
                            optionRow.Spacing(4);

                            optionRow.ConstantItem(optionCircleWidth)
                                        .AlignMiddle()
                                        .Text(IsSelected(selectedValue, option.Value) ? "●" : "○");

                            optionRow.ConstantItem(optionLabelWidth)
                                        .AlignMiddle()
                                        .Text(option.Label);
                        });
                    }
                });
            });
        }

        private static bool IsEnabled(string? value) => value == "1";

        private string? GetLookupValue(string key)
        {
            return _lookup.TryGetValue(key, out var value) ? value : null;
        }

        private static bool IsYes(string? value) => value == "1";
        private static bool IsNo(string? value) => value == "0";

        private void ComposeInflationRow(IContainer container, string label, string? value)
        {
            ComposeChoiceField(
                container,
                label,
                value,
                new List<ChoiceOption>
                {
            new ChoiceOption { Value = "1", Label = "Yes" },
            new ChoiceOption { Value = "0", Label = "No" }
                },
                labelWidth: 110,
                optionCircleWidth: 20,
                optionLabelWidth: 40);
        }

        private void ComposeValueField(
            IContainer container,
            string fieldLabel,
            string? value,
            string? unit = null,
            int labelWidth = 180)
        {
            container.Row(row =>
            {
                row.Spacing(8);

                row.ConstantItem(labelWidth)
                   .Text(fieldLabel)
                   .SemiBold();

                row.AutoItem()
                   .MinWidth(60)
                   .Border(1)
                   .Padding(4)
                   .Text(string.IsNullOrWhiteSpace(value) ? "" : value);

                if (!string.IsNullOrWhiteSpace(unit))
                {
                    row.ConstantItem(60)
                       .AlignMiddle()
                       .Text(unit);
                }
            });
        }

        private void ComposeSignatureBlock(IContainer container)
        {
            const float signingAreaHeight = 60;
            const float signatureImageHeight = 32;

            container.Column(col =>
            {
                col.Spacing(8);

                col.Item().Text(
                    "By signing below, the physician confirms that the data presented in this document are identical to the data recorded in the study-specific data capture application."
                );

                col.Item().PaddingTop(10).Row(row =>
                {
                    row.Spacing(20);

                    // DATE
                    row.RelativeItem().Column(c =>
                    {
                        var created = File.Exists(_physicianSignaturePath)
                            ? File.GetCreationTime(_physicianSignaturePath).ToString("dd-MM-yyyy")
                            : "";

                        c.Item().Text("Date");

                        c.Item()
                            .Height(signingAreaHeight)
                            .AlignBottom()
                            .Column(inner =>
                            {
                                inner.Item()
                                     .PaddingBottom(2)
                                     .Text(created);

                                inner.Item()
                                     .LineHorizontal(1);
                            });
                    });

                    // PHYSICIAN SIGNATURE
                    row.RelativeItem().Column(c =>
                    {
                        string physicianName = "";

                        if (File.Exists(_physicianSignaturePath))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(_physicianSignaturePath);

                            if (fileName.StartsWith("signature_physician_", StringComparison.OrdinalIgnoreCase))
                            {
                                physicianName = fileName
                                    .Substring("signature_physician_".Length)
                                    .Replace("_", " ");

                                physicianName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(physicianName.ToLower());
                            }
                        }

                        c.Item().Text($"Study Physician signature {physicianName}");

                        c.Item()
                            .Height(signingAreaHeight)
                            .AlignBottom()
                            .Column(inner =>
                            {
                                if (File.Exists(_physicianSignaturePath))
                                {
                                    inner.Item()
                                         .Height(signatureImageHeight)
                                         .AlignLeft()
                                         .Image(_physicianSignaturePath)
                                         .FitHeight();
                                }
                                else
                                {
                                    inner.Item()
                                         .Height(signatureImageHeight);
                                }

                                inner.Item()
                                     .LineHorizontal(1);
                            });
                    });
                });

                col.Item().PaddingTop(12).Text(
                    "By signing below, the study nurse confirms that the data imported into REDCap is identical to the data presented in this document."
                );

                col.Item().PaddingTop(10).Row(row =>
                {
                    row.Spacing(20);

                    // DATE
                    row.RelativeItem().Column(c =>
                    {
                        var created = File.Exists(_nurseSignaturePath)
                            ? File.GetCreationTime(_nurseSignaturePath).ToString("dd-MM-yyyy")
                            : "";

                        c.Item().Text("Date");

                        c.Item()
                            .Height(signingAreaHeight)
                            .AlignBottom()
                            .Column(inner =>
                            {
                                inner.Item()
                                     .PaddingBottom(2)
                                     .Text(created);

                                inner.Item()
                                     .LineHorizontal(1);
                            });
                    });

                    // NURSE SIGNATURE
                    row.RelativeItem().Column(c =>
                    {
                        string nurseName = "";

                        if (File.Exists(_nurseSignaturePath))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(_nurseSignaturePath);

                            if (fileName.StartsWith("signature_study_nurse_", StringComparison.OrdinalIgnoreCase))
                            {
                                nurseName = fileName
                                    .Substring("signature_study_nurse_".Length)
                                    .Replace("_", " ");

                                nurseName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(nurseName.ToLower());
                            }
                        }

                        c.Item().Text($"Study Nurse signature {nurseName}");

                        c.Item()
                            .Height(signingAreaHeight)
                            .AlignBottom()
                            .Column(inner =>
                            {
                                if (File.Exists(_nurseSignaturePath))
                                {
                                    inner.Item()
                                         .Height(signatureImageHeight)
                                         .AlignLeft()
                                         .Image(_nurseSignaturePath)
                                         .FitHeight();
                                }
                                else
                                {
                                    inner.Item()
                                         .Height(signatureImageHeight);
                                }

                                inner.Item()
                                     .LineHorizontal(1);
                            });
                    });
                });
            });
        }

        private static bool IsReboaDisabled(string timepoint)
        {
            return timepoint == "-10" || timepoint == "T0" || timepoint == "TI";
        }

        private static IContainer DisabledCell(IContainer container)
        {
            return container
                .Border(1)
                .Background(Colors.Grey.Lighten3) 
                .Padding(2)
                .AlignCenter()
                .AlignMiddle();
        }

        public DocumentMetadata GetMetadata() => new DocumentMetadata
        {
            Title = $"Monitor Report {_caseId}",
            Author = "CPReboaReporting"
        };

        private void ComposeMonitorTable(IContainer container, IReadOnlyList<MonitorTimepointRow> rows, bool greyAllReboaColumns = false)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(30);   // TP
                    columns.ConstantColumn(90);   // Timestamp

                    for (int i = 0; i < 9; i++)
                        columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("TP");
                    header.Cell().Element(HeaderCell).Text("Time");

                    header.Cell().Element(HeaderCell).Text("Schleuse\nBP syst");
                    header.Cell().Element(HeaderCell).Text("Schleuse\nBP mean");
                    header.Cell().Element(HeaderCell).Text("Schleuse\nBP diast");

                    header.Cell().Element(HeaderCell).Text("REBOA\nBP syst");
                    header.Cell().Element(HeaderCell).Text("REBOA\nBP mean");
                    header.Cell().Element(HeaderCell).Text("REBOA\nBP diast");

                    header.Cell().Element(HeaderCell).Text("NIRS\nLeft");
                    header.Cell().Element(HeaderCell).Text("NIRS\nRight");
                    header.Cell().Element(HeaderCell).Text("etCO2");
                });

                foreach (var row in rows)
                {
                    table.Cell().Element(Cell).Text(row.Timepoint ?? "");
                    table.Cell().Element(Cell).Text(row.Timestamp ?? "");

                    table.Cell().Element(Cell).Text(row.SchleuseBpSyst ?? "");
                    table.Cell().Element(Cell).Text(row.SchleuseBpMean ?? "");
                    table.Cell().Element(Cell).Text(row.SchleuseBpDiast ?? "");

                    Func<IContainer, IContainer> reboaStyle =
                        greyAllReboaColumns || IsReboaDisabled(row.Timepoint)
                            ? DisabledCell
                            : Cell;

                    var reboaBpSyst = greyAllReboaColumns ? "" : (row.ReboaBpSyst ?? "");
                    var reboaBpMean = greyAllReboaColumns ? "" : (row.ReboaBpMean ?? "");
                    var reboaBpDiast = greyAllReboaColumns ? "" : (row.ReboaBpDiast ?? "");

                    table.Cell().Element(reboaStyle).Text(reboaBpSyst);
                    table.Cell().Element(reboaStyle).Text(reboaBpMean);
                    table.Cell().Element(reboaStyle).Text(reboaBpDiast);

                    table.Cell().Element(Cell).Text(row.NirsLeft ?? "");
                    table.Cell().Element(Cell).Text(row.NirsRight ?? "");
                    table.Cell().Element(Cell).Text(row.EtCo2 ?? "");
                }
            });
        }

        private void ComposeBloodDrawTable(IContainer container, IReadOnlyList<BloodDrawTimeRow> rows)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(50);   // TP
                    columns.RelativeColumn();     // Blood Sample Time
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("TP");
                    header.Cell().Element(HeaderCell).Text("Blood Sample Time");
                });

                foreach (var row in rows)
                {
                    table.Cell().Element(Cell).Text(row.Timepoint ?? "");
                    table.Cell().Element(Cell).Text(row.BloodDrawTime ?? "");
                }
            });
        }



        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(15);

                // critical for wide tables
                page.DefaultTextStyle(x => x.FontSize(8));

                page.Header().Column(header =>
                {
                    header.Spacing(5);

                    // --- TOP ROW (logo + names) ---
                    header.Item().Row(row =>
                    {
                        row.Spacing(10);

                        // LEFT: LOGO
                        row.ConstantItem(200)
                           .AlignTop()
                           .Element(c =>
                           {
                               if (File.Exists(_logoSvgPath))
                                   c.Svg(_logoSvgPath).FitWidth();
                           });

                        // RIGHT: TITLES + NAMES
                        row.RelativeItem()
                           .AlignTop()
                           .AlignRight()
                           .Column(column =>
                           {
                               column.Spacing(0);

                               column.Item().AlignRight().Text("Klinikdirektor a. i.").FontSize(5);
                               column.Item().AlignRight().Text("Dr. med. Beat Lehmann").SemiBold().FontSize(6);

                               /*
                               column.Item().PaddingTop(1).Text("");

                               column.Item().AlignRight().Text("Leitender Arzt Forschung").FontSize(5);
                               column.Item().AlignRight().Text("Prof. Dr. med. Wolf Hautz, MME").SemiBold().FontSize(6);
                               */
                           });
                    });

                });


                page.Content().PaddingTop(10).PaddingBottom(15).Column(column =>
                {
                    // --- TITLE BLOCK ---
                    column.Item().Column(title =>
                    {
                        title.Spacing(6);

                        // MAIN TITLE
                        title.Item().AlignCenter().Text("CPReboa Study Report")
                            .SemiBold()
                            .FontSize(14);

                        // SUBTITLE / DESCRIPTION
                        title.Item().AlignCenter().Text("This document provides a structured summary of data collected during the study using a study-specific data capture application. The data presented reflects the information available at the time of report generation; additional data may be recorded subsequently.")
                            .FontSize(8);

                        // SPACER
                        title.Item().Height(5);

                        // STUDY DETAILS TABLE-LIKE LAYOUT
                        title.Item().AlignCenter().Column(details =>
                        {
                            details.Spacing(3);

                            void AddDetail(string label, string value)
                            {
                                details.Item().Row(row =>
                                {
                                    row.Spacing(12);

                                    row.ConstantItem(140)
                                       .AlignTop()
                                       .Text(label)
                                       .SemiBold()
                                       .FontSize(7)
                                       .AlignRight();

                                    row.ConstantItem(220)
                                       .AlignTop()
                                       .Text(value)
                                       .FontSize(7);
                                });
                            }

                            AddDetail("Study Name", "in-hospital cardiopulmonary resuscitation with balloon occlusion of the descending aorta");
                            AddDetail("Acronym", "CPReboa");
                            AddDetail("BASEC-Nr.", "2025-D0108");
                            AddDetail("DLF-Nr", "6281");
                            AddDetail("Clinic", "Universitätsklinik für Notfallmedizin, Inselspital Bern");
                            AddDetail("Sponsor Investigator", "PD Dr. med. et MME Tanja Birrenbach");
                        });
                    });

                    // spacing after title
                    column.Item().Height(10);


                    // --- ELIGIBILITY ---
                    column.Item().PaddingTop(12).Text("Eligibility and randomisation").SemiBold().FontSize(10);

                    column.Item().Element(c => ComposeValueField(
                         c,
                         "Record ID",
                         GetLookupValue("record_id"),
                         "[Number]"));
                    column.Spacing(10);

                    column.Item().Element(c => ComposeValueField(
                        c,
                        "Date and time at place of arrest",
                        FormatDateTime(GetLookupValue("dt_at_place")),
                        ("[dd-MM-yyyy HH:mm:ss]")
                        ));

                    column.Spacing(10);


                    // INCLUSION CRITERIA
                    column.Item().PaddingTop(12).Text("Inclusion criteria").SemiBold().FontSize(8);

                    

                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "Patients suffering from IHCA, including patients with OHCA and ROSC, transported to the ED, where they suffer a\r\nsecond arrest (IHCA)",
                        GetLookupValue("ihca_patients"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" }
                        }));
                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "Successful placement of a femoral artery introducer sheath",
                        GetLookupValue("succes_place_fem_art"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" }
                        }));
                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "Any electrical cardiac activity seen in the initial rhythm analysis",
                        GetLookupValue("any_elect_cardi_activity"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" }
                        }));
                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "Ongoing effort of resuscitation as\r\ndetermined by study-independent resuscitation lead",
                        GetLookupValue("ongoing_resuscitation"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" }
                        }));

                    // EXCLUSION CRITERIA
                    column.Item().PaddingTop(12).Text("Exclusion criteria").SemiBold().FontSize(8);

                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "IHCA in the operating room, on intensive care unit or in the cardiac catheter laboratory",
                        GetLookupValue("ihca_op_icu_cath_lab"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" }
                        }));
                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "Hospital visitors suffering from cardia arrest",
                        GetLookupValue("hospital_visitors"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" }
                        }));
                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "Asystole seen in the initial rhythm analysis",
                        GetLookupValue("asystole_init_rhyth"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" }
                        }));
                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "(Presumed) age under 18 years",
                        GetLookupValue("presumed_age_under_18_year"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" }
                        }));
                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "Known \"do not resuscitate\"-order",
                        GetLookupValue("known_no_resuscitate_order"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" }
                        }));
                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "Known or obvious pregnancy",
                        GetLookupValue("known_or_obvious_pregnancy"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" }
                        }));
                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "Traumatic cardiac arrest",
                        GetLookupValue("traumatic_cardiac_arrest"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" }
                        }));
                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "Known aortic pathologies that render cannulation impossible",
                        GetLookupValue("known_aortic_pathologies"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" }
                        }));
                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "Known allergies to radiographic contrast agents",
                        GetLookupValue("allergics_radio_contrast"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" }
                        }));

                    column.Item().Element(c => ComposeValueField(
                            c,
                            "Date and time of inclusion",
                            FormatDateTime(GetLookupValue("dt_inclusion")),
                            "[dd-MM-yyyy HH:mm:ss]"
                        ));
                    column.Spacing(10);


                    column.Item().PaddingTop(20).Element(c => ComposeValueField(
                        c,
                        "Date and time of randomisation",
                        FormatDateTime(GetLookupValue("dt_randomisation")),
                        "[dd-MM-yyyy HH:mm:ss]"
                        ));

                    column.Spacing(10);

                    column.Item().Element(c => ComposeChoiceField(
                       c,
                       "Randomisation",
                       GetLookupValue("randomisation"),
                       new List<ChoiceOption>
                       {
                            new ChoiceOption { Value = "1", Label = "Intervention group" },
                            new ChoiceOption { Value = "2", Label = "Control group" }
                       }));

                    // --- CONSENT ---
                    column.Item().ShowEntire().Column(block =>
                    {
                        block.Item().PaddingTop(12).Text("Consent").SemiBold().FontSize(10);

                        block.Item().PaddingTop(10).Element(c => ComposeValueField(
                            c,
                            "Date and time \"ok inclusion\" from resuscitation team",
                            FormatDateTime(GetLookupValue("dt_inclusion")),
                            "[dd-MM-yyyy HH:mm:ss]"
                        ));
                    });

                    column.Spacing(10);

                    column.Item().Element(c => ComposeValueField(
                        c,
                        "Name of Resuscitation Team physician",
                        GetLookupValue("acls_team_physicianname")
                    ));



                    // --- CPR CHARACTERISTICS ---
                    // --- CPR CHARACTERISTICS ---
                    column.Item().ShowEntire().Column(block =>
                    {
                        block.Item().PaddingTop(12).Text("CPR characteristics").SemiBold().FontSize(10);

                        block.Item().PaddingTop(10).Element(c => ComposeChoiceField(
                            c,
                            "Place of arrest",
                            GetLookupValue("place_arrest"),
                            new List<ChoiceOption>
                            {
                                new ChoiceOption { Value = "1", Label = "Ward" },
                                new ChoiceOption { Value = "2", Label = "Intermediate care unit" },
                                new ChoiceOption { Value = "3", Label = "Emergency room" },
                                new ChoiceOption { Value = "4", Label = "Outpatient clinic" },
                                new ChoiceOption { Value = "88", Label = "Other" }
                            }));
                    });

                    if (GetLookupValue("place_arrest") == "88")
                    {
                        column.Item().Element(c => ComposeValueField(
                        c,
                        "Unknown",
                        GetLookupValue("first_rhythm_unknown")));

                        column.Spacing(10);
                    }

                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "First rhythm",
                        GetLookupValue("first_rhythm"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "PEA" },
                            new ChoiceOption { Value = "2", Label = "VFib" },
                            new ChoiceOption { Value = "3", Label = "VT" },
                            new ChoiceOption { Value = "4", Label = "Unknown" }
                        }));


                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "Defibrillatable first rhythm",
                        GetLookupValue("defi_first_rhythm"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" },
                            new ChoiceOption { Value = "2", Label = "Unclear" }
                        }));


                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "Arrest observed",
                        GetLookupValue("observed"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" },
                            new ChoiceOption { Value = "99", Label = "Unclear" }
                        }));

                    column.Item().Element(c => ComposeValueField(
                        c,
                        "Downtime",
                        GetLookupValue("downtime"),
                        "[min]"));

                    column.Item().Element(c => ComposeValueField(
                        c,
                        "Conventional defibrillations",
                        GetLookupValue("conventional_defi"),
                        "[Number]"));
                    column.Spacing(10);

                    column.Item().Element(c => ComposeValueField(
                        c,
                        "Vector change defibrillations",
                        GetLookupValue("vector_change_defi"),
                        "[Number]"));
                    column.Spacing(10);

                    column.Item().Element(c => ComposeValueField(
                        c,
                        "Double sequential defibrillations",
                        GetLookupValue("double_sequential_defi"),
                        "[Number]"));
                    column.Spacing(10);

                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "ROSC (any)",
                        GetLookupValue("rosc"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" }
                        }));

                    if (GetLookupValue("rosc") == "1")
                    {
                        column.Item().Element(c => ComposeValueField(
                        c,
                        "Date and time ROSC",
                        FormatDateTime(GetLookupValue("dt_rosc")),
                        "[dd-MM-yyyy HH:mm:ss]"
                        ));

                        column.Spacing(10);
                    } else
                    {
                        column.Item().Element(c => ComposeValueField(
                        c,
                        "Reason termination of CPR",
                        GetLookupValue("reason_end_cpr")));

                        column.Spacing(10);
                    }



                    column.Item().Element(c => ComposeValueField(
                        c,
                        "Date and time End CPR",
                        FormatDateTime(GetLookupValue("dt_end_cpr")),
                        "[dd-MM-yyyy HH:mm:ss]"
                        ));

                    column.Spacing(10);

                    column.Item().Element(c => ComposeChoiceField(
                        c,
                        "Sustained ROSC (>20min)",
                        GetLookupValue("sustained_rosc_20min"),
                        new List<ChoiceOption>
                        {
                            new ChoiceOption { Value = "1", Label = "Yes" },
                            new ChoiceOption { Value = "0", Label = "No" }
                        }));

                    column.Spacing(10);

                    column.Item().Element(c => ComposeValueField(
                        c,
                        "Total adrenalin dose",
                        GetLookupValue("total_adrenalin_dose"),
                        "[mg]"));
                    column.Spacing(20);

                    // --- TIMEPOINT DATA ---

                    var randomisationValue = GetLookupValue("randomisation");
                    var isInterventionGroup = randomisationValue == "1";
                    var isControlGroup = randomisationValue == "2";

                    var isReboa1 = IsEnabled(GetLookupValue("reboa_1_inflation"));
                    var isReboa2 = IsEnabled(GetLookupValue("reboa_2_inflation"));
                    var isReboa3 = IsEnabled(GetLookupValue("reboa_3_inflation"));

                    column.Item().ShowEntire().Column(block =>
                    {
                        block.Item().PaddingTop(12).Text("Timepoint Data").SemiBold().FontSize(10);

                        if (isInterventionGroup)
                        {
                            block.Item().PaddingTop(10).Text("1. REBOA").SemiBold().FontSize(8);
                            block.Item().PaddingTop(10).Element(c => ComposeInflationRow(c, "REBOA 1. Inflation", GetLookupValue("reboa_1_inflation")));
                            column.Item().Element(c =>
                                c.DefaultTextStyle(x => x.FontSize(6))
                                 .Element(inner => ComposeMonitorTable(inner, _rows))
                            );
                        }
                        else if (isControlGroup)
                        {
                            column.Item().Element(c =>
                            c.DefaultTextStyle(x => x.FontSize(6))
                             .Element(inner => ComposeMonitorTable(inner, _rows, greyAllReboaColumns: true))
                            );
                        }
                    });

                    if (isInterventionGroup)
                    {

                        column.Item().PaddingTop(10).Text("2. REBOA").SemiBold().FontSize(8);
                        column.Item().Element(c => ComposeInflationRow(c, "REBOA 2. Inflation", GetLookupValue("reboa_2_inflation")));
                        if (isReboa2)
                        {
                            column.Item().Element(c =>
                                c.DefaultTextStyle(x => x.FontSize(6))
                                 .Element(inner => ComposeMonitorTable(inner, _repeatRows))
                            );
                        }

                        column.Item().PaddingTop(10).Text("3. REBOA").SemiBold().FontSize(8);
                        column.Item().Element(c => ComposeInflationRow(c, "REBOA 3. Inflation", GetLookupValue("reboa_3_inflation")));
                        if (isReboa3)
                        {
                            column.Item().Element(c =>
                                c.DefaultTextStyle(x => x.FontSize(6))
                                 .Element(inner => ComposeMonitorTable(inner, _repeat3Rows))
                            );
                        }
                    }

                    column.Spacing(10);


                    column.Item().ShowEntire().Column(block =>
                    {
                        block.Item().PaddingTop(12).Text("Blood Sample Times").SemiBold().FontSize(10);

                        block.Item()
                            .PaddingTop(10)
                            .Width(150)
                            .Element(c =>
                                c.DefaultTextStyle(x => x.FontSize(7))
                                 .Element(inner => ComposeBloodDrawTable(inner, _bloodDrawRows))
                            );
                    });

                    // --- REBOA ---
                    column.Item().ShowEntire().Column(block =>
                    {
                        block.Item().PaddingTop(12).Text("REBOA").SemiBold().FontSize(10);

                        block.Item().PaddingTop(10).Element(c => ComposeValueField(
                            c,
                            "Name of Study physician",
                            GetLookupValue("name_of_study_physician")
                        ));
                    });

                    column.Item().Element(c => ComposeValueField(
                        c,
                        "Date and time of femoral disinfection",
                        FormatDateTime(GetLookupValue("dt_femoral_disinfection")),
                        "[dd-MM-yyyy HH:mm:ss]"
                        ));
                    column.Spacing(10);

                    column.Item().Element(c => ComposeValueField(
                        c,
                        "Date and time of femoral access",
                        FormatDateTime(GetLookupValue("dt_femoral_access")),
                        "[dd-MM-yyyy HH:mm:ss]"
                        ));
                    column.Spacing(10);

                    if (isInterventionGroup)
                    {
                        column.Item().Element(c => ComposeValueField(
                        c,
                        "Date and time of REBOA 1. inflation",
                        FormatDateTime(GetLookupValue("dt_reboa_1_inflation")),
                        "[dd-MM-yyyy HH:mm:ss]"
                        ));

                        column.Item().Element(c => ComposeValueField(
                        c,
                        "How deep is the REBOA?",
                        GetLookupValue("how_deep_is_the_reboa"),
                        "[cm]"));
                        column.Spacing(10);

                        column.Item().Element(c => ComposeValueField(
                        c,
                        "Date and time of REBOA start 1. deflation",
                        FormatDateTime(GetLookupValue("dt_reboa_start_1_deflation")),
                        "[dd-MM-yyyy HH:mm:ss]"
                        ));

                        if (isReboa2)
                        {
                            column.Item().Element(c => ComposeValueField(
                                c,
                                "Date and time of REBOA 2. inflation",
                                FormatDateTime(GetLookupValue("dt_reboa_2_inflation")),
                                "[dd-MM-yyyy HH:mm:ss]"
                                ));

                            column.Item().Element(c => ComposeValueField(
                                c,
                                "Date and time of REBOA start 2. deflation",
                                FormatDateTime(GetLookupValue("dt_reboa_start_2_deflation")),
                                "[dd-MM-yyyy HH:mm:ss]"
                                ));
                        }

                        if (isReboa3)
                        {
                            column.Item().Element(c => ComposeValueField(
                                c,
                                "Date and time of REBOA 3. inflation",
                                FormatDateTime(GetLookupValue("dt_reboa_3_inflation")),
                                "[dd-MM-yyyy HH:mm:ss]"
                                ));

                            column.Item().Element(c => ComposeValueField(
                                c,
                                "Date and time of REBOA start 3. deflation",
                                FormatDateTime(GetLookupValue("dt_reboa_start_3_deflation")),
                                "[dd-MM-yyyy HH:mm:ss]"
                                ));
                        }
                    }

                    column.Item().ExtendVertical();

                    column.Item()
                        .ShowEntire()
                        .Element(c => ComposeSignatureBlock(c));







                });

                page.Footer()
                .BorderTop(1)
                .PaddingTop(5)
                .DefaultTextStyle(x => x.FontSize(6))
                .Row(row =>
                {
                    row.Spacing(10);

                    // Exported by
                    row.RelativeItem(2)
                       .AlignLeft()
                       .Text($"Exported by: {_exportedByUser}");

                    // Document ID (give it most space)
                    row.RelativeItem(4)
                       .AlignCenter()
                       .Text($"Document ID: {_documentId}")
                       .WrapAnywhere(false);

                    // Page numbers
                    row.RelativeItem(2)
                       .AlignRight()
                       .Text(x =>
                       {
                           x.Span("Page ");
                           x.CurrentPageNumber();
                           x.Span(" of ");
                           x.TotalPages();
                       });
                });

            });
        }

        // styles
        private static IContainer HeaderCell(IContainer container)
        {
            return container
                .Border(1)
                .Background(Colors.Grey.Lighten2)
                .Padding(2)
                .MinHeight(30)
                .AlignCenter()
                .AlignMiddle();
        }

        private static IContainer Cell(IContainer container)
        {
            return container
                .Border(1)
                .Padding(2)
                .AlignCenter()
                .AlignMiddle();
        }
    }
}
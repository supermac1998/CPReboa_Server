using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CPReboaMonitorLauncher;
using System.Threading;



// --- Processes ---
Process? intelliProc = null;
Process? masimoProc = null;
Process? microphoneProc = null;

string? intelliLastError = null;
string? masimoLastError = null;
string? microphoneLastError = null;

DateTime? intelliStartTime = null;
DateTime? masimoStartTime = null;
DateTime? microphoneStartTime = null;

string? intelliStopFile = null;
string? microphoneStopFile = null;

// --- ASP.NET builder ---
var builder = WebApplication.CreateBuilder(args);

// Force JSON camelCase to match JS
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

string ResolveExePath(string configuredPath)
{
    if (Path.IsPathRooted(configuredPath))
        return configuredPath;

    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
}

string intelliExePath = ResolveExePath(
    builder.Configuration["MonitorLaunchers:IntelliVueExe"]
    ?? throw new InvalidOperationException("Missing config: MonitorLaunchers:IntelliVueExe"));

string masimoExePath = ResolveExePath(
    builder.Configuration["MonitorLaunchers:MasimoExe"]
    ?? throw new InvalidOperationException("Missing config: MonitorLaunchers:MasimoExe"));

string microphoneExePath = ResolveExePath(
    builder.Configuration["MonitorLaunchers:MicrophoneExe"]
    ?? throw new InvalidOperationException("Missing config: MonitorLaunchers:MicrophoneExe"));

var app = builder.Build();

string? currentCaseId = null;
string? currentCaseDir = null;
string? currentEventsCsvFile = null;
string? currentFormCsvFile = null;

var _csvLock = new SemaphoreSlim(1, 1);

string appDataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    "CPReboa"
);

string sessionsDir = Path.Combine(appDataDir, "cases");

string templateCsvPath = Path.Combine(appDataDir, "CPReboa_ImportTemplate.csv");

// Ensure folder exists once at startup
Directory.CreateDirectory(sessionsDir);

void SetCurrentCase(string caseId)
{
    currentCaseId = Path.GetFileName(caseId); // basic safety
    currentCaseDir = Path.Combine(sessionsDir, currentCaseId);
    currentEventsCsvFile = Path.Combine(currentCaseDir, "events.csv");
    currentFormCsvFile = Path.Combine(currentCaseDir, "form.csv");
}

string CreateNewCase()
{
    var utcNow = DateTime.UtcNow;
    var caseId = $"case_{utcNow:yyyyMMdd_HHmmss}";

    var caseDir = Path.Combine(sessionsDir, caseId);
    Directory.CreateDirectory(caseDir);

    var eventsFile = Path.Combine(caseDir, "events.csv");
    var formFile = Path.Combine(caseDir, "form.csv");

    currentCaseId = caseId;
    currentCaseDir = caseDir;
    currentEventsCsvFile = eventsFile;
    currentFormCsvFile = formFile;

    return caseId;
}

bool HasActiveCase() =>
    !string.IsNullOrWhiteSpace(currentCaseId) &&
    !string.IsNullOrWhiteSpace(currentCaseDir) &&
    !string.IsNullOrWhiteSpace(currentEventsCsvFile) &&
    !string.IsNullOrWhiteSpace(currentFormCsvFile);

MonitorStatusFile? ReadIntelliVueStatusFile()
{
    if (string.IsNullOrWhiteSpace(currentCaseDir))
        return null;

    var path = Path.Combine(currentCaseDir, "intellivue", "status.json");
    if (!File.Exists(path))
        return null;

    try
    {
        using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);

        using var sr = new StreamReader(fs);
        var json = sr.ReadToEnd();

        return JsonSerializer.Deserialize<MonitorStatusFile>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
    }
    catch
    {
        return null;
    }
}

MonitorStatusFile? ReadMasimoStatusFile()
{
    if (string.IsNullOrWhiteSpace(currentCaseDir))
        return null;

    var path = Path.Combine(currentCaseDir, "masimo", "status.json");
    if (!File.Exists(path))
        return null;

    try
    {
        using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);

        using var sr = new StreamReader(fs);
        var json = sr.ReadToEnd();

        return JsonSerializer.Deserialize<MonitorStatusFile>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
    }
    catch
    {
        return null;
    }
}

string GetIntelliVueDisplayState()
{
    if (intelliProc == null)
        return "Stopped";

    try
    {
        if (intelliProc.HasExited)
            return "Stopped";
    }
    catch
    {
        return "Stopped";
    }

    var status = ReadIntelliVueStatusFile();
    if (status == null || string.IsNullOrWhiteSpace(status.State))
        return "Starting";

    var rawState = status.State.Trim().ToLowerInvariant();

    return rawState switch
    {
        "starting" => "Starting",
        "waiting_for_monitor" => "NotConnected",
        "recording" => "Recording",
        "stopped" => "Stopped",
        "error" => "Error",
        _ => "Starting"
    };
}
string GetIntelliVueAppState()
{
    return GetIntelliVueDisplayState() == "Recording"
        ? "Running"
        : "Stopped";
}

string GetMasimoDisplayState()
{
    if (masimoProc == null)
        return "Stopped";

    try
    {
        if (masimoProc.HasExited)
            return "Stopped";
    }
    catch
    {
        return "Stopped";
    }

    var status = ReadMasimoStatusFile();
    if (status == null || string.IsNullOrWhiteSpace(status.State))
        return "Starting";

    var rawState = status.State.Trim().ToLowerInvariant();

    return rawState switch
    {
        "starting" => "Starting",
        "waiting_for_monitor" => "NotConnected",
        "recording" => "Recording",
        "stopped" => "Stopped",
        "error" => "Error",
        _ => "Starting"
    };
}
string GetMasimoAppState()
{
    return GetMasimoDisplayState() == "Recording"
        ? "Running"
        : "Stopped";
}

// --- Start monitors ---
void StartMonitors()
{
    if (!HasActiveCase())
        throw new InvalidOperationException("No active case selected.");

    // Try IntelliVue
    try
    {
        if (!IsIntelliVueRunning())
        {
            if (intelliProc != null)
            {
                try
                {
                    if (intelliProc.HasExited)
                    {
                        intelliProc.Dispose();
                        intelliProc = null;
                    }
                }
                catch
                {
                    intelliProc = null;
                }
            }

            var intelliDir = Path.Combine(currentCaseDir!, "intellivue");
            Directory.CreateDirectory(intelliDir);

            intelliStopFile = Path.Combine(intelliDir, "stop.txt");
            if (File.Exists(intelliStopFile))
            {
                File.Delete(intelliStopFile);
            }

            var proc = new Process();
            proc.StartInfo.FileName = intelliExePath;
            proc.StartInfo.Arguments = $"-outdir \"{intelliDir}\" -stopfile \"{intelliStopFile}\"";
            proc.StartInfo.UseShellExecute = true;
            proc.StartInfo.CreateNoWindow = false;
            proc.EnableRaisingEvents = true;
            proc.Exited += (_, _) =>
            {
                intelliStartTime = null;
            };

            if (!File.Exists(intelliExePath))
                throw new FileNotFoundException("IntelliVue executable not found.", intelliExePath);

            proc.Start();

            intelliProc = proc;
            intelliStartTime = DateTime.UtcNow;
            intelliLastError = null;
        }
    }
    catch (Exception ex)
    {
        intelliProc = null;
        intelliStartTime = null;
        intelliLastError = ex.Message;
        Console.WriteLine($"IntelliVue failed to start: {ex}");
    }

    // Try Masimo
    try
    {
        if (!IsMasimoRunning())
        {
            if (masimoProc != null)
            {
                try
                {
                    if (masimoProc.HasExited)
                    {
                        masimoProc.Dispose();
                        masimoProc = null;
                    }
                }
                catch
                {
                    masimoProc = null;
                }
            }

            var masimoDir = Path.Combine(currentCaseDir!, "masimo");
            Directory.CreateDirectory(masimoDir);

            var proc = new Process();
            proc.StartInfo.FileName = masimoExePath;
            proc.StartInfo.Arguments = $"-outdir \"{masimoDir}\"";
            proc.StartInfo.UseShellExecute = true;
            proc.StartInfo.CreateNoWindow = false;
            proc.EnableRaisingEvents = true;
            proc.Exited += (_, _) =>
            {
                masimoProc = null;
                masimoStartTime = null;
            };

            if (!File.Exists(masimoExePath))
                throw new FileNotFoundException("Masimo executable not found.", masimoExePath);

            proc.Start();

            masimoProc = proc;
            masimoStartTime = DateTime.UtcNow;
            masimoLastError = null;
        }
    }
    catch (Exception ex)
    {
        masimoProc = null;
        masimoStartTime = null;
        masimoLastError = ex.Message;
        Console.WriteLine($"Masimo failed to start: {ex}");
    }

    // Try Microphone
    try
    {
        if (!IsMicrophoneRunning())
        {
            if (microphoneProc != null)
            {
                try
                {
                    if (microphoneProc.HasExited)
                    {
                        microphoneProc.Dispose();
                        microphoneProc = null;
                    }
                }
                catch
                {
                    microphoneProc = null;
                }
            }

            var microphoneDir = Path.Combine(currentCaseDir!, "microphone");
            Directory.CreateDirectory(microphoneDir);

            microphoneStopFile = Path.Combine(microphoneDir, "stop.txt");
            if (File.Exists(microphoneStopFile))
            {
                File.Delete(microphoneStopFile);
            }

            var proc = new Process();
            proc.StartInfo.FileName = microphoneExePath;
            proc.StartInfo.Arguments = $"-outdir \"{microphoneDir}\" -stopfile \"{microphoneStopFile}\"";
            proc.StartInfo.UseShellExecute = true;
            proc.StartInfo.CreateNoWindow = false;
            proc.EnableRaisingEvents = true;
            proc.Exited += (_, _) =>
            {
                microphoneProc = null;
                microphoneStartTime = null;
            };

            if (!File.Exists(microphoneExePath))
                throw new FileNotFoundException("Microphone executable not found.", microphoneExePath);

            proc.Start();

            microphoneProc = proc;
            microphoneStartTime = DateTime.UtcNow;
            microphoneLastError = null;
        }
    }
    catch (Exception ex)
    {
        microphoneProc = null;
        microphoneStartTime = null;
        microphoneLastError = ex.Message;
        Console.WriteLine($"Microphone failed to start: {ex}");
    }

    Console.WriteLine("Monitor start attempted at " + DateTime.Now);
}

// --- Stop monitors ---
void StopMonitors()
{
    var intelliToStop = intelliProc;
    intelliProc = null;
    intelliStartTime = null;

    if (intelliToStop != null)
    {
        try
        {
            if (!intelliToStop.HasExited)
            {
                if (!string.IsNullOrWhiteSpace(intelliStopFile))
                {
                    File.WriteAllText(intelliStopFile, "stop");
                }

                if (!intelliToStop.WaitForExit(8000))
                {
                    Console.WriteLine("IntelliVue did not exit gracefully, forcing kill.");
                    intelliToStop.Kill(true);
                    intelliToStop.WaitForExit(5000);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"IntelliVue stop failed: {ex}");
        }
        finally
        {
            try
            {
                intelliToStop.Dispose();
            }
            catch
            {
            }

            intelliStopFile = null;
        }
    }

    if (masimoProc != null && !masimoProc.HasExited)
    {
        masimoProc.Kill();
        masimoProc = null;
        masimoStartTime = null;
    }

    if (microphoneProc != null)
    {
        try
        {
            if (!microphoneProc.HasExited)
            {
                if (!string.IsNullOrWhiteSpace(microphoneStopFile))
                {
                    File.WriteAllText(microphoneStopFile, "stop");
                }

                if (!microphoneProc.WaitForExit(8000))
                {
                    Console.WriteLine("Microphone did not exit gracefully, forcing kill.");
                    microphoneProc.Kill(true);
                    microphoneProc.WaitForExit(5000);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Microphone stop failed: {ex}");
        }
        finally
        {
            try
            {
                microphoneProc.Dispose();
            }
            catch
            {
            }

            microphoneProc = null;
            microphoneStartTime = null;
            microphoneStopFile = null;
        }
    }

    Console.WriteLine($"Monitors stopped at {DateTime.Now}");
}

// --- Check if monitors are running ---
bool IsIntelliVueRunning() => intelliProc != null && !intelliProc.HasExited;
bool IsMasimoRunning() => masimoProc != null && !masimoProc.HasExited;
bool IsMicrophoneRunning() => microphoneProc != null && !microphoneProc.HasExited;

// --- Web endpoints ---
app.MapGet("/start", () =>
{
    try
    {
        StartMonitors();

        var intelliStatus = ReadIntelliVueStatusFile();
        var masimoStatus = ReadMasimoStatusFile();

        return Results.Json(new
        {
            message = "Start attempted",
            caseId = currentCaseId,
            intelliVue = new
            {
                state = GetIntelliVueAppState(),
                runtime = intelliStartTime.HasValue
                    ? (DateTime.UtcNow - intelliStartTime.Value).TotalSeconds
                    : 0,
                error = intelliLastError,
                statusMessage = intelliStatus?.Message,
                lastPacketReceivedUtc = intelliStatus?.LastPacketReceivedUtc
            },
            masimo = new
            {
                state = GetMasimoAppState(),
                runtime = masimoStartTime.HasValue
                    ? (DateTime.UtcNow - masimoStartTime.Value).TotalSeconds
                    : 0,
                error = masimoLastError,
                statusMessage = masimoStatus?.Message,
                lastDataReceivedUtc = masimoStatus?.LastDataReceivedUtc
            },
            microphone = new
            {
                state = IsMicrophoneRunning() ? "Running" : "Stopped",
                runtime = microphoneStartTime.HasValue
                    ? (DateTime.UtcNow - microphoneStartTime.Value).TotalSeconds
                    : 0,
                error = microphoneLastError
            }
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

app.MapGet("/stop", () =>
{
    StopMonitors();
    return Results.Ok("Monitors stopped");
});

app.MapGet("/status", () =>
{
    var intelliStatus = ReadIntelliVueStatusFile();
    var masimoStatus = ReadMasimoStatusFile();

    return Results.Json(new
    {
        intelliVue = new
        {
            state = GetIntelliVueAppState(),
            runtime = intelliStartTime.HasValue
            ? (DateTime.UtcNow - intelliStartTime.Value).TotalSeconds
            : 0,
            error = intelliLastError,
            message = intelliStatus?.Message,
            lastPacketReceivedUtc = intelliStatus?.LastPacketReceivedUtc
        },
        masimo = new
        {
            state = GetMasimoAppState(),
            rawState = masimoStatus?.State,
            runtime = masimoStartTime.HasValue
                ? (DateTime.UtcNow - masimoStartTime.Value).TotalSeconds
                : 0,
            error = masimoLastError,
            message = masimoStatus?.Message,
            lastDataReceivedUtc = masimoStatus?.LastDataReceivedUtc
        },
        mic = new
        {
            state = IsMicrophoneRunning() ? "Running" : "Stopped",
            runtime = microphoneStartTime.HasValue
                ? (DateTime.UtcNow - microphoneStartTime.Value).TotalSeconds
                : 0,
            error = microphoneLastError
        }
    });
});

string GetCsVFilePath(DateTime arrivalUtc)
{
    string timeStampForFile = arrivalUtc.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
    return Path.Combine(AppContext.BaseDirectory, $"variables_log_{timeStampForFile}.csv");
}

async Task LogToCsvAsync(string eventName, string utcTimestamp)
{
    if (currentEventsCsvFile == null)
        throw new InvalidOperationException("No active CSV session. Arrival not triggered yet.");

    await _csvLock.WaitAsync();
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(currentEventsCsvFile)!);

        if (!File.Exists(currentEventsCsvFile))
        {
            await File.AppendAllTextAsync(
                currentEventsCsvFile,
                "Event,TimestampUTC" + Environment.NewLine
            );
        }

        string line = $"{eventName},{utcTimestamp}";
        await File.AppendAllTextAsync(
            currentEventsCsvFile,
            line + Environment.NewLine
        );
    }
    finally
    {
        _csvLock.Release();
    }
}


app.MapPost("/arrival", async () =>
{
    if (!HasActiveCase())
        return Results.BadRequest("No active case selected.");

    var utcNow = DateTime.UtcNow;
    var localNow = utcNow.ToLocalTime();

    var utcString = utcNow.ToString("O");
    var localString = localNow.ToString("yyyy-MM-dd HH:mm:ss");

    await LogToCsvAsync("dt_at_place", utcString);

    return Results.Json(new
    {
        caseId = currentCaseId,
        caseName = currentCaseId,
        timestamp = localString
    });
});

app.MapPost("/event", async (EventRequest request) =>
{
    if (currentEventsCsvFile == null)
        return Results.BadRequest("Arrival must be triggered first.");

    var utcNow = DateTime.UtcNow;
    var utcString = utcNow.ToString("O");

    await LogToCsvAsync(request.EventName, utcString);

    if (request.EventName == "time_end_case")
    {
        await FinalizeCurrentCaseAsync();
    }

    // Return local time to Android for display
    var localString = utcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    return Results.Json(new { timestamp = localString });
});

app.MapPost("/value", async (EventRequest request) =>
{
    if (currentEventsCsvFile == null)
        return Results.BadRequest("Arrival must be triggered first.");

    await LogToCsvAsync(request.ValueName, request.Value);

    return Results.Ok();

});

// List available session CSVs
app.MapGet("/cases", () =>
{

    Directory.CreateDirectory(sessionsDir);

    var cases = Directory.EnumerateDirectories(sessionsDir)
        .Select(dir =>
        {
            var di = new DirectoryInfo(dir);
            var eventsPath = Path.Combine(dir, "events.csv");
            var eventsFile = new FileInfo(eventsPath);

            return new
            {
                id = di.Name,
                name = di.Name,
                createdUtc = di.CreationTimeUtc,
                sizeBytes = eventsFile.Exists ? eventsFile.Length : 0L
            };
        })
        .OrderByDescending(c => c.createdUtc);

    return Results.Json(cases);
});

// Get a specific CSV by filename
app.MapGet("/cases/{id}", (string id) =>
{
    Directory.CreateDirectory(sessionsDir);

    id = Path.GetFileName(id); // prevent path traversal

    var caseDir = Path.Combine(sessionsDir, id);
    var eventsPath = Path.Combine(caseDir, "events.csv");

    if (!Directory.Exists(caseDir) || !System.IO.File.Exists(eventsPath))
        return Results.NotFound("Case not found.");

    return Results.File(
        eventsPath,
        "text/csv; charset=utf-8",
        fileDownloadName: $"{id}_events.csv"
    );
});

app.MapPost("/cases/new", () =>
{
    Directory.CreateDirectory(sessionsDir);

    var caseId = CreateNewCase();

    return Results.Json(new
    {
        caseId = caseId,
        caseName = caseId
    });
});

app.MapPost("/cases/select/{id}", (string id) =>
{
    Directory.CreateDirectory(sessionsDir);

    id = Path.GetFileName(id); // prevent path traversal

    var caseDir = Path.Combine(sessionsDir, id);

    if (!Directory.Exists(caseDir))
        return Results.NotFound("Case not found.");

    SetCurrentCase(id);

    return Results.Json(new
    {
        caseId = currentCaseId,
        caseName = currentCaseId
    });
});

string EscapeCsv(string value)
{
    value ??= string.Empty;
    return "\"" + value.Replace("\"", "\"\"") + "\"";
}

async Task SaveFormValueAsync(string valueName, string value)
{
    if (currentFormCsvFile == null || currentCaseDir == null)
        throw new InvalidOperationException("No active case selected.");

    Directory.CreateDirectory(currentCaseDir);

    var rows = new List<(string Field, string Value)>();

    if (File.Exists(currentFormCsvFile))
    {
        var lines = await File.ReadAllLinesAsync(currentFormCsvFile);

        foreach (var line in lines.Skip(1)) // skip header
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var commaIndex = line.IndexOf(',');
            if (commaIndex < 0)
                continue;

            var field = line.Substring(0, commaIndex).Trim().Trim('"');
            var rawValue = line.Substring(commaIndex + 1).Trim();

            if (rawValue.StartsWith("\"") && rawValue.EndsWith("\"") && rawValue.Length >= 2)
            {
                rawValue = rawValue.Substring(1, rawValue.Length - 2).Replace("\"\"", "\"");
            }

            rows.Add((field, rawValue));
        }
    }

    var existingIndex = rows.FindIndex(r => string.Equals(r.Field, valueName, StringComparison.OrdinalIgnoreCase));

    if (existingIndex >= 0)
        rows[existingIndex] = (valueName, value);
    else
        rows.Add((valueName, value));

    var output = new List<string> { "Field,Value" };
    output.AddRange(rows.Select(r => $"{EscapeCsv(r.Field)},{EscapeCsv(r.Value)}"));

    await File.WriteAllLinesAsync(currentFormCsvFile, output);
}

app.MapPost("/form/value", async (FormValueRequest request) =>
{
    if (!HasActiveCase())
        return Results.BadRequest("No active case selected.");

    if (string.IsNullOrWhiteSpace(request.ValueName))
        return Results.BadRequest("ValueName is required.");

    await SaveFormValueAsync(request.ValueName, request.Value ?? string.Empty);

    return Results.Ok();
});

app.MapPost("/cases/finalize", async () =>
{
    if (!HasActiveCase())
        return Results.BadRequest("No active case selected.");

    await FinalizeCurrentCaseAsync();

    return Results.Ok(new { message = "Case finalized" });
});

Dictionary<string, string> ReadNameValueCsv(string path, string nameColumn, string valueColumn)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);

    if (!File.Exists(path))
        return result;

    var lines = File.ReadAllLines(path);
    if (lines.Length == 0)
        return result;

    var header = lines[0].Split(',');

    int nameIndex = Array.FindIndex(header, h => string.Equals(h.Trim().Trim('"'), nameColumn, StringComparison.OrdinalIgnoreCase));
    int valueIndex = Array.FindIndex(header, h => string.Equals(h.Trim().Trim('"'), valueColumn, StringComparison.OrdinalIgnoreCase));

    if (nameIndex < 0 || valueIndex < 0)
        throw new InvalidOperationException($"CSV '{path}' is missing required columns '{nameColumn}' and/or '{valueColumn}'.");

    foreach (var line in lines.Skip(1))
    {
        if (string.IsNullOrWhiteSpace(line))
            continue;

        var parts = SplitCsvLine(line);

        if (parts.Count <= Math.Max(nameIndex, valueIndex))
            continue;

        var name = parts[nameIndex];
        var value = parts[valueIndex];

        if (string.IsNullOrWhiteSpace(name))
            continue;

        result[name] = value; // last value wins
    }

    return result;
}

List<string> SplitCsvLine(string line)
{
    var result = new List<string>();
    if (line == null)
        return result;

    var sb = new System.Text.StringBuilder();
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

Dictionary<string, string> BuildCaseLookup(string caseDir)
{
    var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

    var eventsFile = Path.Combine(caseDir, "events.csv");
    var formFile = Path.Combine(caseDir, "form.csv");

    foreach (var kvp in ReadNameValueCsv(eventsFile, "Event", "TimestampUTC"))
        lookup[kvp.Key] = kvp.Value;

    foreach (var kvp in ReadNameValueCsv(formFile, "Field", "Value"))
        lookup[kvp.Key] = kvp.Value;

    AddIntelliVueTemplateValues(lookup, caseDir);
    AddMasimoTemplateValues(lookup, caseDir);

    return lookup;
}

async Task<string> FillTemplateCsvAsync(string templateCsvPath, string caseDir)
{
    if (!File.Exists(templateCsvPath))
        throw new FileNotFoundException("Template CSV not found.", templateCsvPath);

    var lookup = BuildCaseLookup(caseDir);

    var lines = await File.ReadAllLinesAsync(templateCsvPath);
    if (lines.Length == 0)
        throw new InvalidOperationException("Template CSV is empty.");

    var outputLines = new List<string>();

    var headerParts = SplitCsvLine(lines[0]);
    if (headerParts.Count < 2)
        throw new InvalidOperationException("Template CSV must have at least two columns.");

    outputLines.Add(lines[0]); // preserve header

    foreach (var line in lines.Skip(1))
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            outputLines.Add(line);
            continue;
        }

        var parts = SplitCsvLine(line);

        while (parts.Count < 2)
            parts.Add(string.Empty);

        var variableName = parts[0];

        if (!string.IsNullOrWhiteSpace(variableName) && lookup.TryGetValue(variableName, out var matchedValue))
        {
            parts[1] = matchedValue;
        }

        outputLines.Add(string.Join(",", parts.Select(EscapeCsv)));
    }

    var outputPath = Path.Combine(caseDir, "redcap.csv");
    await File.WriteAllLinesAsync(outputPath, outputLines);

    return outputPath;
}



async Task FinalizeCurrentCaseAsync()
{
    if (!HasActiveCase() || currentCaseDir == null)
        throw new InvalidOperationException("No active case selected.");

    StopMonitors();

    await FillTemplateCsvAsync(templateCsvPath, currentCaseDir);

    currentCaseId = null;
    currentCaseDir = null;
    currentEventsCsvFile = null;
    currentFormCsvFile = null;
}


// ---- TEST -----
async Task RunTemplateFillTestAsync(string caseDir, string templatePath)
{
    Console.WriteLine($"Testing case folder: {caseDir}");
    Console.WriteLine($"Using template:      {templatePath}");

    if (!Directory.Exists(caseDir))
        throw new DirectoryNotFoundException($"Case folder not found: {caseDir}");

    if (!File.Exists(templatePath))
        throw new FileNotFoundException("Template file not found.", templatePath);

    var outputPath = await FillTemplateCsvAsync(templatePath, caseDir);

    Console.WriteLine($"Created output file: {outputPath}");

    var lookup = BuildCaseLookup(caseDir);
    Console.WriteLine($"Loaded {lookup.Count} case values.");
}

if (args.Length > 0 && args[0] == "--test-fill")
{
    string caseDir = args.Length > 1
        ? args[1]
        : throw new ArgumentException("Missing case folder path.");

    string templatePath = args.Length > 2
        ? args[2]
        : throw new ArgumentException("Missing template CSV path.");

    await RunTemplateFillTestAsync(caseDir, templatePath);
    return;
}



bool TryParseUtcTimestamp(string value, out DateTime utc)
{
    utc = default;

    if (string.IsNullOrWhiteSpace(value))
        return false;

    if (DateTime.TryParse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
        out var parsed))
    {
        utc = parsed.ToUniversalTime();
        return true;
    }

    return false;
}

Dictionary<string, DateTime> ReadNamedEventTimes(string eventsCsvPath, params string[] names)
{
    var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

    if (!File.Exists(eventsCsvPath))
        return result;

    var lines = File.ReadAllLines(eventsCsvPath);
    if (lines.Length == 0)
        return result;

    var header = SplitCsvLine(lines[0]);

    int eventIndex = header.FindIndex(h =>
        string.Equals(h.Trim().Trim('"'), "Event", StringComparison.OrdinalIgnoreCase));

    int timestampIndex = header.FindIndex(h =>
        string.Equals(h.Trim().Trim('"'), "TimestampUTC", StringComparison.OrdinalIgnoreCase));

    if (eventIndex < 0 || timestampIndex < 0)
        throw new InvalidOperationException($"CSV '{eventsCsvPath}' is missing Event and/or TimestampUTC columns.");

    var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    foreach (var line in lines.Skip(1))
    {
        if (string.IsNullOrWhiteSpace(line))
            continue;

        var parts = SplitCsvLine(line);
        if (parts.Count <= Math.Max(eventIndex, timestampIndex))
            continue;

        var eventName = parts[eventIndex]?.Trim();
        var timestampText = parts[timestampIndex]?.Trim();

        if (string.IsNullOrWhiteSpace(eventName) || !wanted.Contains(eventName))
            continue;

        if (TryParseUtcTimestamp(timestampText, out var utc))
            result[eventName] = utc;
    }

    return result;
}

void AddIntelliVueTemplateValues(Dictionary<string, string> lookup, string caseDir)
{
    var eventsPath = Path.Combine(caseDir, "events.csv");
    var mpDataExportPath = Path.Combine(caseDir, "intellivue", "MPDataExport.csv");

    if (!File.Exists(eventsPath) || !File.Exists(mpDataExportPath))
        return;

    var timepoints = ReadNamedEventTimes(
    eventsPath,
    "T10",
    "T0",
    "T1", "T1_2", "T1_3",
    "T2", "T2_2", "T2_3",
    "T3", "T3_2", "T3_3",
    "T4", "T4_2", "T4_3",
    "T5", "T5_2", "T5_3"
);
    if (timepoints.Count == 0)
        return;

    var lines = File.ReadAllLines(mpDataExportPath);
    if (lines.Length < 2)
        return;

    var header = SplitCsvLine(lines[0]);

    int tsIndex = header.FindIndex(h =>
        string.Equals(h.Trim().Trim('"'), "PCTimestampUTC", StringComparison.OrdinalIgnoreCase));

    int abpSysIndex = header.FindIndex(h =>
        string.Equals(h.Trim().Trim('"'), "NOM_PRESS_BLD_ART_ABP_SYS", StringComparison.OrdinalIgnoreCase));

    int abpDiaIndex = header.FindIndex(h =>
        string.Equals(h.Trim().Trim('"'), "NOM_PRESS_BLD_ART_ABP_DIA", StringComparison.OrdinalIgnoreCase));

    int abpMeanIndex = header.FindIndex(h =>
        string.Equals(h.Trim().Trim('"'), "NOM_PRESS_BLD_ART_ABP_MEAN", StringComparison.OrdinalIgnoreCase));

    int aortSysIndex = header.FindIndex(h =>
        string.Equals(h.Trim().Trim('"'), "NOM_PRESS_BLD_AORT_SYS", StringComparison.OrdinalIgnoreCase));

    int aortDiaIndex = header.FindIndex(h =>
        string.Equals(h.Trim().Trim('"'), "NOM_PRESS_BLD_AORT_DIA", StringComparison.OrdinalIgnoreCase));

    int aortMeanIndex = header.FindIndex(h =>
        string.Equals(h.Trim().Trim('"'), "NOM_PRESS_BLD_AORT_MEAN", StringComparison.OrdinalIgnoreCase));

    int etco2Index = header.FindIndex(h =>
    string.Equals(h.Trim().Trim('"'), "NOM_AWAY_CO2_ET", StringComparison.OrdinalIgnoreCase));

    if (tsIndex < 0)
        throw new InvalidOperationException("MPDataExport.csv is missing PCTimestampUTC column.");

    var rows = new List<(DateTime TimestampUtc, List<string> Values)>();

    foreach (var line in lines.Skip(1))
    {
        if (string.IsNullOrWhiteSpace(line))
            continue;

        var parts = SplitCsvLine(line);

        while (parts.Count < header.Count)
            parts.Add(string.Empty);

        if (parts.Count <= tsIndex)
            continue;

        if (!TryParseUtcTimestamp(parts[tsIndex], out var rowUtc))
            continue;

        rows.Add((rowUtc, parts));
    }

    if (rows.Count == 0)
        return;

    foreach (var kvp in timepoints)
    {
        string eventName = kvp.Key;   // T0..T5
        DateTime targetUtc = kvp.Value;
        string suffix = eventName switch
        {
            "T10" => "10",
            _ => eventName.ToLowerInvariant()
        };

        var nearest = rows
            .Select(r => new
            {
                Row = r,
                Delta = (r.TimestampUtc - targetUtc).Duration()
            })
            .OrderBy(x => x.Delta)
            .FirstOrDefault();

        if (nearest == null || nearest.Delta > TimeSpan.FromSeconds(5))
            continue;

        string GetValue(int index)
        {
            if (index < 0 || index >= nearest.Row.Values.Count)
                return string.Empty;

            return nearest.Row.Values[index]?.Trim() ?? string.Empty;
        }

        // ABP -> r_*
        lookup[$"r_bp_syst_{suffix}"] = GetValue(abpSysIndex);
        lookup[$"r_bp_diast_{suffix}"] = GetValue(abpDiaIndex);
        lookup[$"r_bp_m_{suffix}"] = GetValue(abpMeanIndex);

        // AORT -> s_*
        lookup[$"s_bp_syst_{suffix}"] = GetValue(aortSysIndex);
        lookup[$"s_bp_diast_{suffix}"] = GetValue(aortDiaIndex);
        lookup[$"s_bp_m_{suffix}"] = GetValue(aortMeanIndex);

        // NOM_AWAY_CO2_ET -> etco2_*
        lookup[$"etco2_{suffix}"] = GetValue(etco2Index);

        // optional debug field
        lookup[$"pc_timestamp_{suffix}"] =
            nearest.Row.TimestampUtc.ToString("O", CultureInfo.InvariantCulture);
    }
}

void AddMasimoTemplateValues(Dictionary<string, string> lookup, string caseDir)
{
    var eventsPath = Path.Combine(caseDir, "events.csv");
    var masimoCsvPath = Path.Combine(caseDir, "masimo", "masimo.csv");

    if (!File.Exists(eventsPath) || !File.Exists(masimoCsvPath))
        return;

    var timepoints = ReadNamedEventTimes(
    eventsPath,
    "T10",
    "T0",
    "T1", "T1_2", "T1_3",
    "T2", "T2_2", "T2_3",
    "T3", "T3_2", "T3_3",
    "T4", "T4_2", "T4_3",
    "T5", "T5_2", "T5_3"
    );

    if (timepoints.Count == 0)
        return;

    var lines = File.ReadAllLines(masimoCsvPath);
    if (lines.Length < 2)
        return;

    var header = SplitCsvLine(lines[0]);

    int tsIndex = header.FindIndex(h =>
        string.Equals(h.Trim().Trim('"'), "PCTimestampUTC", StringComparison.OrdinalIgnoreCase));

    int leftIndex = header.FindIndex(h =>
        string.Equals(h.Trim().Trim('"'), "o3rSO2_1", StringComparison.OrdinalIgnoreCase));

    int rightIndex = header.FindIndex(h =>
        string.Equals(h.Trim().Trim('"'), "o3rSO2_2", StringComparison.OrdinalIgnoreCase));

    if (tsIndex < 0)
        throw new InvalidOperationException("masimo.csv is missing PCTimestampUTC column.");

    if (leftIndex < 0)
        throw new InvalidOperationException("masimo.csv is missing o3rSO2_1 column.");

    if (rightIndex < 0)
        throw new InvalidOperationException("masimo.csv is missing o3rSO2_2 column.");

    var rows = new List<(DateTime TimestampUtc, List<string> Values)>();

    foreach (var line in lines.Skip(1))
    {
        if (string.IsNullOrWhiteSpace(line))
            continue;

        var parts = SplitCsvLine(line);

        while (parts.Count < header.Count)
            parts.Add(string.Empty);

        if (parts.Count <= tsIndex)
            continue;

        if (!TryParseUtcTimestamp(parts[tsIndex], out var rowUtc))
            continue;

        rows.Add((rowUtc, parts));
    }

    if (rows.Count == 0)
        return;

    foreach (var kvp in timepoints)
    {
        string eventName = kvp.Key;   // T0..T5
        DateTime targetUtc = kvp.Value;
        string suffix = eventName switch
        {
            "T10" => "10",
            _ => eventName.ToLowerInvariant()
        };

        var nearest = rows
            .Select(r => new
            {
                Row = r,
                Delta = (r.TimestampUtc - targetUtc).Duration()
            })
            .OrderBy(x => x.Delta)
            .FirstOrDefault();

        if (nearest == null || nearest.Delta > TimeSpan.FromSeconds(5))
            continue;

        string GetCleanValue(int index)
        {
            if (index < 0 || index >= nearest.Row.Values.Count)
                return string.Empty;

            var value = nearest.Row.Values[index]?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            // remove trailing %
            value = value.Replace("%", "").Trim();

            // treat invalid placeholders as empty
            if (value.Contains("---"))
                return string.Empty;

            // optional: remove leading zeros, so 096 -> 96
            if (int.TryParse(value, out var n))
                return n.ToString(CultureInfo.InvariantCulture);

            return value;
        }

        lookup[$"nirs_l_{suffix}"] = GetCleanValue(leftIndex);
        lookup[$"nirs_r_{suffix}"] = GetCleanValue(rightIndex);
    }
}



app.MapGet("/", async context =>
{
    await context.Response.WriteAsync(@"
<html>
<head>
    <title>CPReboa</title>
    <style>
        body { font-family: 'Segoe UI', Roboto, sans-serif; margin: 0; padding: 20px; background: #f5f5f5; color: #333; }
        h1 { text-align: center; margin-bottom: 30px; font-size: 1.8em; color: #004080; }
        .button-bar { text-align: center; margin-bottom: 30px; }
        button { font-size: 1.2em; padding: 12px 24px; margin: 0 10px; border: none; border-radius: 8px; cursor: pointer; background-color: #0078d4; color: white; box-shadow: 0 2px 4px rgba(0,0,0,0.2); transition: 0.2s; }
        button:hover:not(:disabled) { background-color: #005a9e; }
        button:disabled { opacity: 0.5; cursor: not-allowed; }
        .monitor-card { background: white; border-radius: 12px; padding: 20px; margin-bottom: 20px; box-shadow: 0 2px 8px rgba(0,0,0,0.1); display: flex; align-items: center; justify-content: space-between; }
        .monitor-info { display: flex; align-items: center; gap: 15px; }
        .dot { width: 18px; height: 18px; border-radius: 50%; background: gray; display: inline-block; margin-left: 8px;}
        .runtime { font-family: monospace; font-size: 1.1em; }
        @media (max-width: 600px) { .monitor-card { flex-direction: column; align-items: flex-start; } .monitor-info { gap: 10px; } }
    </style>
</head>
<body>
<h1>CPReboa</h1>
<div class='button-bar'>
    <button id='startBtn'>Start Monitors</button>
    <button id='stopBtn'>Stop Monitors</button>
</div>

<div class='monitor-card'>
    <div class='monitor-info'>
        <strong>IntelliVue</strong>
        <div id='ivDot' class='dot'></div>
    </div>
    <div class='runtime' id='ivTime'><span id='ivH'>00</span>:<span id='ivM'>00</span>:<span id='ivS'>00</span></div>
</div>

<div class='monitor-card'>
    <div class='monitor-info'>
        <strong>Masimo</strong>
        <div id='masDot' class='dot'></div>
    </div>
    <div class='runtime' id='masTime'><span id='masH'>00</span>:<span id='masM'>00</span>:<span id='masS'>00</span></div>
</div>

<div id=""confirmModal"" style=""
    display:none;
    position:fixed;
    top:0; left:0;
    width:100%; height:100%;
    background:rgba(0,0,0,0.5);
    justify-content:center;
    align-items:center;
"">
    <div style=""
        background:white;
        padding:25px;
        border-radius:12px;
        width:300px;
        text-align:center;
        box-shadow:0 4px 15px rgba(0,0,0,0.3);
    "">
        <p id=""confirmText"" style=""margin-bottom:20px;""></p>
        <button id=""confirmYes"" style=""margin-right:10px;"">Yes</button>
        <button id=""confirmNo"">Cancel</button>
    </div>
</div>

<script>
let lastIvState = false, lastMasState = false;
let lastStartDisabled = false, lastStopDisabled = false;
let ivTime = 0, masTime = 0;
let lastUpdateTime = Date.now();

async function fetchStatus() {
    const res = await fetch('/status', { cache: 'no-store' });
    return await res.json();
}

// Update runtime display
function updateRuntimeSpans(prefix, time) {
    const h = Math.floor(time / 3600);
    const m = Math.floor((time % 3600) / 60);
    const s = Math.floor(time % 60);
    document.getElementById(prefix+'H').textContent = String(h).padStart(2,'0');
    document.getElementById(prefix+'M').textContent = String(m).padStart(2,'0');
    document.getElementById(prefix+'S').textContent = String(s).padStart(2,'0');
}

// Update dots/buttons
function updateUI(data) {
    const ivRunning = data.intelliVue.state === 'Running';
    const masRunning = data.masimo.state === 'Running';

    const startBtn = document.getElementById('startBtn');
    const stopBtn = document.getElementById('stopBtn');

    if (ivRunning !== lastIvState) {
        document.getElementById('ivDot').style.background = ivRunning ? 'green' : 'red';
        lastIvState = ivRunning;
    }
    if (masRunning !== lastMasState) {
        document.getElementById('masDot').style.background = masRunning ? 'green' : 'red';
        lastMasState = masRunning;
    }

    startBtn.disabled = ivRunning && masRunning;
    stopBtn.disabled = !ivRunning && !masRunning;

    return {
        ivRuntime: data.intelliVue.runtime,
        masRuntime: data.masimo.runtime
    };
}

// Disconnected UI
function setDisconnectedUI() {
    lastIvState = false;
    lastMasState = false;
    lastStartDisabled = true;
    lastStopDisabled = true;
    document.getElementById('ivDot').style.background = 'gray';
    document.getElementById('masDot').style.background = 'gray';
    document.getElementById('startBtn').disabled = true;
    document.getElementById('stopBtn').disabled = true;
}

// Animate runtime
function animateRuntime() {
    const now = Date.now();
    const delta = (now - lastUpdateTime) / 1000;
    lastUpdateTime = now;
    if (lastIvState) ivTime += delta;
    if (lastMasState) masTime += delta;
    updateRuntimeSpans('iv', ivTime);
    updateRuntimeSpans('mas', masTime);
}

// Fetch and update every 2s
setInterval(async () => {
    try {
        const data = await fetchStatus();
        const runtimes = updateUI(data);
        // IntelliVue
        if (runtimes.ivRuntime < ivTime) {
            // Server reset detected
            ivTime = runtimes.ivRuntime;
        } else if (runtimes.ivRuntime > ivTime) {
            ivTime = runtimes.ivRuntime;
        }

        // Masimo
        if (runtimes.masRuntime < masTime) {
            // Server reset detected
            masTime = runtimes.masRuntime;
        } else if (runtimes.masRuntime > masTime) {
            masTime = runtimes.masRuntime;
        }
    } catch (e) {
        console.log('Server unreachable');
    }
}, 2000);

// Animate runtime every second
setInterval(animateRuntime, 1000);

let confirmCallback = null;

function showConfirm(message, callback) {
    document.getElementById(""confirmText"").textContent = message;
    document.getElementById(""confirmModal"").style.display = ""flex"";
    confirmCallback = callback;
}

document.getElementById(""confirmYes"").onclick = async () => {
    document.getElementById(""confirmModal"").style.display = ""none"";
    if (confirmCallback) await confirmCallback();
};

document.getElementById(""confirmNo"").onclick = () => {
    document.getElementById(""confirmModal"").style.display = ""none"";
};


// Start/Stop buttons
document.getElementById('startBtn').onclick = () => {

    showConfirm(""Are you sure you want to START the monitors?"", async () => {

        await fetch('/start');
        const data = await fetchStatus();
        updateUI(data);
        ivTime = data.intelliVue.runtime;
        masTime = data.masimo.runtime;

    });
};

document.getElementById('stopBtn').onclick = () => {

    showConfirm(""This will terminate both monitors.\n\nAre you sure?"", async () => {

        await fetch('/stop');
        const data = await fetchStatus();
        updateUI(data);

    });
};

</script>
</body>
</html>
");
});

app.Run("http://+:5000");



// --- Monitor Status ---
sealed class MonitorStatusFile
{
    public string? State { get; set; }
    public string? Message { get; set; }
    public string? UtcNow { get; set; }
    public string? LastPacketReceivedUtc { get; set; }
    public string? LastDataReceivedUtc { get; set; }
}
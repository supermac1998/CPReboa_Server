using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class MasimoRootAscii1UsbReader
{
    static StringBuilder buffer = new StringBuilder();
    static StreamWriter csv;

    static string StatusFilePath = "";
    static readonly object StatusLock = new object();
    static DateTime? LastDataReceivedUtc = null;
    static string CurrentState = "starting";
    static string CurrentMessage = "Process starting";

    static readonly TimeSpan InitialDataTimeout = TimeSpan.FromSeconds(10);
    static readonly TimeSpan DataLossTimeout = TimeSpan.FromSeconds(10);

    // Fixed parameter order 
    static readonly string[] Columns =
    {
        "SN",
        "CHAN",
        "sysALARM",
        "o3rSO2_1",
        "o3deltaBase_1",
        "o3deltaSpO2_1",
        "o3AUC_1",
        "o3deltaO2Hb_1",
        "o3deltaHHb_1",
        "o3deltacHb_1",
        "o3ALARM_1",
        "o3EXC_1",
        "o3rSO2_2",
        "o3deltaBase_2",
        "o3deltaSpO2_2",
        "o3AUC_2",
        "o3ALARM_2",
        "o3EXC_2"
    };


    static void Main(string[] args)
    {
        SerialPort port = null;

        try
        {
            string outputDir = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-outdir" && i + 1 < args.Length)
                {
                    outputDir = args[i + 1];
                    break;
                }
            }

            string outputPath;
            if (!string.IsNullOrWhiteSpace(outputDir))
            {
                Directory.CreateDirectory(outputDir);
                outputPath = Path.Combine(outputDir, "masimo.csv");
            }
            else
            {
                outputPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "CPReboa",
                    "MasimoRoot_" + DateTime.UtcNow.ToString("ddMMyyyy_HHmm") + ".csv"
                );

                var fallbackDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(fallbackDir))
                {
                    Directory.CreateDirectory(fallbackDir);
                }
            }

            StatusFilePath = Path.Combine(Path.GetDirectoryName(outputPath), "status.json");
            WriteStatus("starting", "Process started");

            bool fileExists = File.Exists(outputPath);
            bool writeHeader = !fileExists || new FileInfo(outputPath).Length == 0;

            csv = new StreamWriter(outputPath, append: true);
            csv.AutoFlush = true;

            if (writeHeader)
            {
                csv.Write("PCTimestampUTC");
                foreach (string col in Columns)
                {
                    csv.Write("," + col);
                }
                csv.WriteLine();
            }

            port = new SerialPort
            {
                PortName = "COM4",
                BaudRate = 921600,
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
                Encoding = Encoding.ASCII
            };

            port.Open();
            Console.WriteLine("Masimo root connected (ASCII1). Logging CSV ... ");
            WriteStatus("waiting_for_monitor", "Connected to COM port, waiting for Masimo data");

            StartMasimoWatchdog();

            while (true)
            {
                string chunk = port.ReadExisting();
                if (string.IsNullOrEmpty(chunk))
                {
                    Thread.Sleep(50);
                    continue;
                }

                LastDataReceivedUtc = DateTime.UtcNow;
                WriteStatus("recording", "Receiving data from Masimo");

                buffer.Append(chunk);

                while (true)
                {
                    int eol = buffer.ToString().IndexOf("\r\n");
                    if (eol < 0)
                        break;

                    string line = buffer.ToString(0, eol).Trim();
                    buffer.Remove(0, eol + 2);

                    if (string.IsNullOrEmpty(line))
                        continue;

                    LastDataReceivedUtc = DateTime.UtcNow;
                    WriteStatus("recording", "Receiving data from Masimo");

                    WriteCsvRow(line);
                }
            }
        }
        catch (Exception ex)
        {
            ExitWithStatus("error", "Masimo startup/runtime failed: " + ex.Message, 30);
        }
        finally
        {
            try
            {
                csv?.Dispose();
            }
            catch
            {
            }

            try
            {
                if (port != null && port.IsOpen)
                    port.Close();
            }
            catch
            {
            }
        }
    }

    static void StartMasimoWatchdog()
    {
        Task.Run(() =>
        {
            DateTime startUtc = DateTime.UtcNow;

            while (true)
            {
                try
                {
                    if (LastDataReceivedUtc.HasValue)
                    {
                        var age = DateTime.UtcNow - LastDataReceivedUtc.Value;

                        if (age <= DataLossTimeout)
                        {
                            WriteStatus("recording", "Receiving data from Masimo");
                        }
                        else
                        {
                            ExitWithStatus(
                                "error",
                                "No recent data from Masimo. Exiting.",
                                31
                            );
                        }
                    }
                    else
                    {
                        var startupAge = DateTime.UtcNow - startUtc;

                        if (startupAge <= InitialDataTimeout)
                        {
                            WriteStatus("waiting_for_monitor", "No data received yet");
                        }
                        else
                        {
                            ExitWithStatus(
                                "error",
                                "No data received from Masimo within timeout. Exiting.",
                                32
                            );
                        }
                    }

                    Thread.Sleep(2000);
                }
                catch (Exception ex)
                {
                    ExitWithStatus(
                        "error",
                        "Masimo watchdog failure: " + ex.Message,
                        33
                    );
                }
            }
        });
    }

    static void WriteCsvRow(string line)
    {
        // Normalize ASCII1
        line = line.Replace("\r", " ").Replace("\n", " ").Replace("  ", " ").Trim();

        // Strip Masimo device timestamp if present
        if (!string.IsNullOrEmpty(line) && char.IsDigit(line[0]))
        {
            int idx = line.IndexOf("SN=");
            if (idx > 0)
                line = line.Substring(idx);
        }

        // Timestamp ONCE per Root sample
        string timestamp = DateTime.UtcNow.ToString("o");

        Dictionary<string, string> values = new Dictionary<string, string>();

        string[] pairs = line.Split(' ');

        foreach (string pair in pairs)
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0)
                continue;

            string key = pair.Substring(0, eq).Trim();
            string value = pair.Substring(eq + 1).Trim();

            values[key] = value;
        }

        // Write to CSV
        csv.Write(timestamp);
        foreach (string col in Columns)
        {
            csv.Write(",");
            csv.Write(values.TryGetValue(col, out string val) ? val : "");
        }
        csv.WriteLine();

        // Print to console
        Console.Write($"[{timestamp}]");
        foreach (string col in Columns)
        {
            if (values.TryGetValue(col, out string val))
            {
                Console.Write($"{col} {val}");
            }
        }
        Console.WriteLine();
    }

    static void ExitWithStatus(string state, string message, int exitCode)
    {
        try
        {
            WriteStatus(state, message);
        }
        catch
        {
        }

        Console.WriteLine(message);
        Environment.Exit(exitCode);
    }

    static void WriteStatus(string state, string message = null)
    {
        lock (StatusLock)
        {
            CurrentState = state;
            if (message != null)
                CurrentMessage = message;

            var json = "{\n" +
                       $"  \"state\": \"{CurrentState}\",\n" +
                       $"  \"message\": \"{(CurrentMessage ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")}\",\n" +
                       $"  \"utcNow\": \"{DateTime.UtcNow:O}\",\n" +
                       $"  \"lastDataReceivedUtc\": {(LastDataReceivedUtc.HasValue ? $"\"{LastDataReceivedUtc.Value:O}\"" : "null")}\n" +
                       "}";

            File.WriteAllText(StatusFilePath, json);
        }
    }

}

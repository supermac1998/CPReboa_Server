// Get USB Array Microphone
/*
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;

class Program
{
    static void Main()
    {
        var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

        foreach (var device in devices)
        {
            Console.WriteLine("Name: " + device.FriendlyName);
            Console.WriteLine("ID:   " + device.ID);
            Console.WriteLine("----------------------------------");
        }
    }
}
*/

/*
using System;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;

class Program
{
    static void Main()
    {
        var enumerator = new MMDeviceEnumerator();
        var device = enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .First(d => d.ID.Contains("1b9a4a52"));

        Console.WriteLine("Using: " + device.FriendlyName);

        using var capture = new WasapiCapture(device, true, 10);

        Console.WriteLine("Device format:");
        Console.WriteLine(capture.WaveFormat);

        using var writer = new WaveFileWriter("usb_recording.wav",
            capture.WaveFormat);   // use native format

        capture.DataAvailable += (s, e) =>
        {
            writer.Write(e.Buffer, 0, e.BytesRecorded);
        };

        capture.StartRecording();

        Console.WriteLine("Recording... Press ENTER to stop.");
        Console.ReadLine();

        capture.StopRecording();
    }
}*/

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

class Program
{
    private static WasapiCapture? _capture;
    private static WaveFileWriter? _writer;
    private static int _stopRequested = 0;

    // Time when audio buffer arrives
    private static readonly object _metadataLock = new();
    private static bool _firstChunkSeen = false;
    private static string? _jsonPath;
    private static RecordingMetadata? _metadata;

    static int Main(string[] args)
    {
        try
        {
            string? outDir = GetArgumentValue(args, "-outdir");

            if (string.IsNullOrWhiteSpace(outDir))
            {
                Console.WriteLine("Missing required argument: -outdir");
                return 1;
            }

            string? stopFile = GetArgumentValue(args, "-stopfile");

            Directory.CreateDirectory(outDir);

            var enumerator = new MMDeviceEnumerator();
            /*var device = enumerator
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .FirstOrDefault(d => d.ID.Contains("4DF4C068-435", StringComparison.OrdinalIgnoreCase));*/
            var device = enumerator
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .FirstOrDefault(d => d.FriendlyName.Contains("OSM09", StringComparison.OrdinalIgnoreCase));

            if (device == null)
            {
                Console.WriteLine("Could not find the USB array microphone.");
                return 2;
            }

            Console.WriteLine("Using: " + device.FriendlyName);
            Console.WriteLine("ID: " + device.ID);

            DateTime startUtc = DateTime.UtcNow;
            string wavFileName = $"audio_{startUtc:yyyyMMdd_HHmmss}.wav";
            string wavPath = Path.Combine(outDir, wavFileName);
            string jsonFileName = $"audio_metadata_{startUtc:yyyyMMdd_HHmmss}.json";
            string jsonPath = Path.Combine(outDir, jsonFileName);
            

            var metadata = new RecordingMetadata
            {
                DeviceName = device.FriendlyName,
                DeviceId = device.ID,
                RequestedStartUtc = startUtc.ToString("O", CultureInfo.InvariantCulture),
                RequestedStartLocal = startUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                FirstDataUtc = null,
                FirstDataLocal = null,
                StopUtc = null,
                StopLocal = null,
                OutputDirectory = outDir,
                AudioFile = wavFileName
            };

            _jsonPath = jsonPath;
            _metadata = metadata;

            SaveMetadata();

            _capture = new WasapiCapture(device, true, 10);
            Console.WriteLine("Device format:");
            Console.WriteLine(_capture.WaveFormat);

            _metadata.SampleRate = _capture.WaveFormat.SampleRate;
            _metadata.BitsPerSample = _capture.WaveFormat.BitsPerSample;
            _metadata.Channels = _capture.WaveFormat.Channels;
            SaveMetadata();

            _writer = new WaveFileWriter(wavPath, _capture.WaveFormat);

            _capture.DataAvailable += (s, e) =>
            {
                if (!_firstChunkSeen)
                {
                    lock (_metadataLock)
                    {
                        if (!_firstChunkSeen)
                        {
                            _firstChunkSeen = true;
                            DateTime firstDataUtc = DateTime.UtcNow;

                            if (_metadata != null)
                            {
                                _metadata.FirstDataUtc = firstDataUtc.ToString("O", CultureInfo.InvariantCulture);
                                _metadata.FirstDataLocal = firstDataUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                                SaveMetadata();
                            }

                            Console.WriteLine("First audio data received at UTC: " + _metadata?.FirstDataUtc);
                        }
                    }
                }

                _writer?.Write(e.Buffer, 0, e.BytesRecorded);
            };

            _capture.RecordingStopped += (s, e) =>
            {
                DateTime stopUtc = DateTime.UtcNow;
                lock (_metadataLock)
                {
                    if (_metadata != null && _metadata.StopUtc == null)
                    {
                        _metadata.StopUtc = stopUtc.ToString("O", CultureInfo.InvariantCulture);
                        _metadata.StopLocal = stopUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                        SaveMetadata();
                    }
                }

                try
                {
                    _writer?.Dispose();
                    _writer = null;

                    _capture?.Dispose();
                    _capture = null;

                    if (e.Exception != null)
                    {
                        Console.WriteLine("Recording stopped with error:");
                        Console.WriteLine(e.Exception);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Cleanup error:");
                    Console.WriteLine(ex);
                }
            };

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                RequestStop();
            };

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                RequestStop();
            };

            _capture.StartRecording();
            Console.WriteLine("Audio recording started.");

            while (Interlocked.CompareExchange(ref _stopRequested, 0, 0) == 0)
            {
                if (!string.IsNullOrWhiteSpace(stopFile) && File.Exists(stopFile))
                {
                    Console.WriteLine("Stop file detected.");
                    RequestStop();
                    break;
                }

                Thread.Sleep(200);
            }

            Thread.Sleep(500);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return 99;
        }
    }

    static void RequestStop()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) == 1)
            return;

        try
        {
            if (_capture != null)
            {
                _capture.StopRecording();
            }
            else
            {
                DateTime stopUtc = DateTime.UtcNow;

                lock (_metadataLock)
                {
                    if (_metadata != null && _metadata.StopUtc == null)
                    {
                        _metadata.StopUtc = stopUtc.ToString("O", CultureInfo.InvariantCulture);
                        _metadata.StopLocal = stopUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                        SaveMetadata();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("StopRecording failed:");
            Console.WriteLine(ex);

            try
            {
                DateTime stopUtc = DateTime.UtcNow;

                lock (_metadataLock)
                {
                    if (_metadata != null && _metadata.StopUtc == null)
                    {
                        _metadata.StopUtc = stopUtc.ToString("O", CultureInfo.InvariantCulture);
                        _metadata.StopLocal = stopUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                        SaveMetadata();
                    }
                }

                _writer?.Dispose();
                _writer = null;

                _capture?.Dispose();
                _capture = null;
            }
            catch
            {
            }
        }
    }

    static void SaveMetadata()
    {
        if (_jsonPath == null || _metadata == null)
            return;

        File.WriteAllText(
            _jsonPath,
            JsonSerializer.Serialize(_metadata, new JsonSerializerOptions { WriteIndented = true })
        );
    }

    static string? GetArgumentValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    sealed class RecordingMetadata
    {
        public string DeviceName { get; set; } = "";
        public string DeviceId { get; set; } = "";

        public string RequestedStartUtc { get; set; } = "";
        public string RequestedStartLocal { get; set; } = "";

        public string? FirstDataUtc { get; set; }
        public string? FirstDataLocal { get; set; }

        public string? StopUtc { get; set; }
        public string? StopLocal { get; set; }

        public int? SampleRate { get; set; }
        public int? BitsPerSample { get; set; }
        public int? Channels { get; set; }

        public string OutputDirectory { get; set; } = "";
        public string AudioFile { get; set; } = "";
    }
}
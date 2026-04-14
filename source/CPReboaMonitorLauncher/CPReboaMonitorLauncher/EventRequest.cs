namespace CPReboaMonitorLauncher
{
    public class EventRequest
    {
        public string EventName { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public record FormValueRequest(string ValueName, string Value);
}

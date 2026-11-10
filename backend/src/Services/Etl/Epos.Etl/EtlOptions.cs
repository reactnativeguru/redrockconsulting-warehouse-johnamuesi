namespace Epos.Etl;

/// <summary>Bound from the "Etl" config section (options pattern).</summary>
public class EtlOptions
{
    public const string Section = "Etl";

    /// <summary>Where the ePOS drops its nightly/hourly sales exports.</summary>
    public string InboundFolder { get; set; } = "data/inbound";
    public string ProcessedFolder { get; set; } = "data/processed";
    public string ErrorFolder { get; set; } = "data/error";
    /// <summary>Where we write files FOR the ePOS to pick up (price changes).</summary>
    public string OutboundFolder { get; set; } = "data/outbound";
    public int PollSeconds { get; set; } = 10;
}

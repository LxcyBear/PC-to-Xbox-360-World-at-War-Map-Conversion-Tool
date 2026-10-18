namespace WaW360.Core.Configuration;

public sealed class ConverterSettings
{
    public static string DefaultOutputFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "OneDrive", "Desktop", "My Xbox", "World at War", "Converter", "output");

    public static string LegacyOutputFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "WaW360Converter", "output");

    public string MapSourceFolder { get; set; } = WaW360.Core.Maps.MapDiscovery.DefaultPcMapsFolder;
    public string PcGameFolder { get; set; } = string.Empty;
    public string XboxGameFolder { get; set; } = string.Empty;
    public string CodxeFolder { get; set; } = string.Empty;
    public string OutputFolder { get; set; } = DefaultOutputFolder;
    public int MemoryBudgetMb { get; set; } = 200;
    public string BatchMode { get; set; } = "Fast Mode";
    public int MaxParallelMaps { get; set; } = 2;
}

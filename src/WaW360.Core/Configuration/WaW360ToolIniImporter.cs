namespace WaW360.Core.Configuration;

public static class WaW360ToolIniImporter
{
    public static ConverterSettings Import(string iniPath, ConverterSettings? seed = null)
    {
        var result = seed ?? new ConverterSettings();
        foreach (var raw in File.ReadLines(iniPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || !line.Contains('=')) continue;
            var split = line.Split('=', 2);
            var key = split[0].Trim();
            var value = split[1].Trim();
            switch (key.ToLowerInvariant())
            {
                case "pcgame": result.PcGameFolder = value; break;
                case "xboxgame": result.XboxGameFolder = value; break;
                case "codxe": result.CodxeFolder = value; break;
                case "memorybudgetmb": if (int.TryParse(value, out var mb)) result.MemoryBudgetMb = mb; break;
            }
        }
        return result;
    }
}

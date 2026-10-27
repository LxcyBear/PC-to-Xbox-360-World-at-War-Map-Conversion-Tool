using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

public sealed class T4XenonGraphSerializationPlan
{
    public required string DumpRoot { get; init; }
    public required int TechniqueSets { get; init; }
    public required int TechniqueReferences { get; init; }
    public required int UniqueTechniques { get; init; }
    public required int Passes { get; init; }
    public required int MultiPassTechniques { get; init; }
    public required int UniqueVertexDecls { get; init; }
    public required int UniqueVertexShaders { get; init; }
    public required int UniquePixelShaders { get; init; }
    public required int ShaderArguments { get; init; }
    public required int LiteralConstants { get; init; }
    public required int UniqueLiteralConstants { get; init; }
    public required long ShaderBlobBytes { get; init; }
    public required long EstimatedTempFixedBytes { get; init; }
    public required bool ReadyForSerialization { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonReportFile { get; init; }
}

public static class T4XenonGraphSerializationPlanner
{
    private const int TechniqueSetSize = 216;
    private const int MaterialPassSize = 88;
    private const int MaterialTechniqueHeaderSize = 8;
    private const int VertexDeclSize = 68;
    private const int VertexShaderSize = 16;
    private const int PixelShaderSize = 16;
    private const int ShaderArgumentSize = 8;
    private const int LiteralConstantSize = 16;

    public static T4XenonGraphSerializationPlan Build(
        string dumpRoot,
        string outputDirectory,
        int expectedTechniqueSets = 87,
        int expectedPixelShaders = 897)
    {
        var imported = T4XenonTechniqueGraphImporter.Load(
            dumpRoot,
            expectedTechniqueSets,
            expectedPixelShaders);
        if (!imported.Verified)
            throw new InvalidDataException(
                "Typed Xenon graph import is not verified: " +
                string.Join(" | ", imported.Warnings));

        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var techniques = new Dictionary<string, T4XenonTechniqueGraph>(StringComparer.Ordinal);
        var vertexDecls = new HashSet<string>(StringComparer.Ordinal);
        var vertexShaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pixelShaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var literals = new HashSet<string>(StringComparer.Ordinal);
        var notes = new List<string>();

        var techniqueRefs = 0;
        var passCount = 0;
        var multiPass = 0;
        var shaderArgs = 0;
        var literalCount = 0;

        foreach (var ts in imported.TechniqueSetsByName.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var slot in ts.Slots)
            {
                var tech = slot.Technique;
                if (tech is null) continue;

                techniqueRefs++;
                passCount += tech.Passes.Count;
                if (tech.Passes.Count != 1) multiPass++;

                var techKey = HashCanonical(tech);
                techniques.TryAdd(techKey, tech);

                foreach (var pass in tech.Passes)
                {
                    if (pass.VertexDecl is not null)
                        vertexDecls.Add(HashCanonical(pass.VertexDecl));

                    if (!string.IsNullOrWhiteSpace(pass.VertexShader?.Name))
                        vertexShaders.Add(pass.VertexShader.Name!);
                    foreach (var vs in pass.VertexShaderArray)
                        if (!string.IsNullOrWhiteSpace(vs?.Name))
                            vertexShaders.Add(vs!.Name!);

                    if (!string.IsNullOrWhiteSpace(pass.PixelShader?.Name))
                        pixelShaders.Add(pass.PixelShader.Name!);

                    shaderArgs += pass.Args.Count;
                    foreach (var arg in pass.Args)
                    {
                        if (arg.LiteralConst is null) continue;
                        literalCount++;
                        literals.Add(string.Join(
                            ",",
                            arg.LiteralConst.Select(x =>
                                BitConverter.SingleToInt32Bits(x).ToString("X8"))));
                    }
                }
            }
        }

        if (pixelShaders.Count != expectedPixelShaders)
            notes.Add(
                $"BLOCKER: graph plan references {pixelShaders.Count:N0} distinct PixelShaders; expected {expectedPixelShaders:N0}.");
        if (multiPass != 0)
            notes.Add(
                $"INFO: {multiPass:N0} Technique reference(s) are multi-pass; serializer must emit variable pass arrays.");
        else
            notes.Add(
                $"INFO: all {techniqueRefs:N0} non-null TechniqueSet slots resolve to single-pass techniques.");

        var estimatedTemp =
            checked((long)imported.TechniqueSets * TechniqueSetSize) +
            techniques.Values.Sum(t =>
                checked((long)MaterialTechniqueHeaderSize +
                        (long)t.Passes.Count * MaterialPassSize)) +
            checked((long)vertexDecls.Count * VertexDeclSize) +
            checked((long)vertexShaders.Count * VertexShaderSize) +
            checked((long)pixelShaders.Count * PixelShaderSize) +
            checked((long)shaderArgs * ShaderArgumentSize) +
            checked((long)literals.Count * LiteralConstantSize);

        notes.Add(
            $"ABI sizes pinned for 32-bit T4 Xenon: TechniqueSet={TechniqueSetSize}, " +
            $"MaterialPass={MaterialPassSize}, VertexDecl={VertexDeclSize}, " +
            $"VertexShader={VertexShaderSize}, PixelShader={PixelShaderSize}, ShaderArg={ShaderArgumentSize}.");
        notes.Add(
            "Estimated TEMP fixed bytes exclude strings, shader cached blobs, alignment padding and top-level XAsset/script tables; PHYSICAL shader bytes are accounted separately by the importer.");
        notes.Add(
            "Serialization policy: PixelShaders remain top-level assets; MaterialPass.pixelShader binds by exact identity. Vertex declarations, vertex shaders, techniques and literal constants are reusable graph nodes and will be emitted once then referenced by packed TEMP offsets.");

        var ready =
            imported.Verified &&
            pixelShaders.Count == expectedPixelShaders &&
            techniques.Count != 0 &&
            vertexShaders.Count != 0;

        var txt = Path.Combine(outputDirectory, "xenon_graph_serialization_plan.txt");
        var json = Path.Combine(outputDirectory, "xenon_graph_serialization_plan.json");

        var result = new T4XenonGraphSerializationPlan
        {
            DumpRoot = imported.DumpRoot,
            TechniqueSets = imported.TechniqueSets,
            TechniqueReferences = techniqueRefs,
            UniqueTechniques = techniques.Count,
            Passes = passCount,
            MultiPassTechniques = multiPass,
            UniqueVertexDecls = vertexDecls.Count,
            UniqueVertexShaders = vertexShaders.Count,
            UniquePixelShaders = pixelShaders.Count,
            ShaderArguments = shaderArgs,
            LiteralConstants = literalCount,
            UniqueLiteralConstants = literals.Count,
            ShaderBlobBytes = imported.ShaderBlobBytes,
            EstimatedTempFixedBytes = estimatedTemp,
            ReadyForSerialization = ready,
            Notes = notes,
            TextReportFile = txt,
            JsonReportFile = json
        };

        File.WriteAllText(txt, Render(result), new UTF8Encoding(false));
        File.WriteAllText(
            json,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));

        return result;
    }

    private static string HashCanonical<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string Render(T4XenonGraphSerializationPlan r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WaW360Converter — NUCLEAR STAGE 4B XENON GRAPH SERIALIZATION PLAN");
        sb.AppendLine(new string('=', 92));
        sb.AppendLine($"Graph root: {r.DumpRoot}");
        sb.AppendLine();
        sb.AppendLine($"TechniqueSets              : {r.TechniqueSets:N0}");
        sb.AppendLine($"Technique references       : {r.TechniqueReferences:N0}");
        sb.AppendLine($"Unique techniques          : {r.UniqueTechniques:N0}");
        sb.AppendLine($"Passes                     : {r.Passes:N0}");
        sb.AppendLine($"Multi-pass references      : {r.MultiPassTechniques:N0}");
        sb.AppendLine($"Unique vertex declarations : {r.UniqueVertexDecls:N0}");
        sb.AppendLine($"Unique vertex shaders      : {r.UniqueVertexShaders:N0}");
        sb.AppendLine($"Unique pixel shaders       : {r.UniquePixelShaders:N0}");
        sb.AppendLine($"Shader arguments           : {r.ShaderArguments:N0}");
        sb.AppendLine($"Literal constants          : {r.LiteralConstants:N0}");
        sb.AppendLine($"Unique literal constants   : {r.UniqueLiteralConstants:N0}");
        sb.AppendLine($"Shader blob bytes          : {r.ShaderBlobBytes:N0}");
        sb.AppendLine($"Estimated TEMP fixed bytes : {r.EstimatedTempFixedBytes:N0}");
        sb.AppendLine($"READY FOR SERIALIZATION    : {r.ReadyForSerialization}");
        sb.AppendLine();
        foreach (var note in r.Notes)
            sb.AppendLine(note);
        return sb.ToString();
    }
}

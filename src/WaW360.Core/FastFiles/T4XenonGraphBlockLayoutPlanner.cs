using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WaW360.Core.FastFiles;

public sealed record T4XenonLayoutNode(
    string Kind,
    string Key,
    string Block,
    long Offset,
    long Size,
    uint PackedPointer);

public sealed record T4XenonRendererRootLayout(
    int RootIndex,
    string TypeName,
    string Name,
    long InsertOffset,
    uint StablePointer);

public sealed class T4XenonGraphBlockLayoutResult
{
    public required int NativeRoots { get; init; }
    public required int ScriptStrings { get; init; }
    public required int RendererTechniqueSets { get; init; }
    public required int RendererPixelShaders { get; init; }
    public required int RealImageRoots { get; init; }
    public required long LargeRuntimeBytes { get; init; }
    public required long LargeBytes { get; init; }
    public required int InsertSlots { get; init; }
    public required int ForwardPixelShaderReferences { get; init; }
    public required int ReusedTechniques { get; init; }
    public required int ReusedVertexDecls { get; init; }
    public required int ReusedVertexShaders { get; init; }
    public required int ReusedLiteralConstants { get; init; }
    public required long TempBytes { get; init; }
    public required long VirtualBytes { get; init; }
    public required long PhysicalBytes { get; init; }
    public required long SerializedBlockBytes { get; init; }
    public required long ProjectedUncompressedZoneBytes { get; init; }
    public required IReadOnlyList<T4XenonRendererRootLayout> RendererRoots { get; init; }
    public required IReadOnlyList<T4XenonLayoutNode> Nodes { get; init; }
    public required IReadOnlyList<string> Blockers { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required bool LayoutReady { get; init; }
    public required string TextReportFile { get; init; }
    public required string JsonManifestFile { get; init; }
}

public static class T4XenonGraphBlockLayoutPlanner
{
    private const int TechniqueSetSize = 216;
    private const int TechniqueHeaderSize = 8;
    private const int PassSize = 88;
    private const int VertexDeclSize = 68;
    private const int VertexShaderSize = 16;
    private const int PixelShaderSize = 16;
    private const int ShaderArgSize = 8;
    private const int LiteralSize = 16;

    public static async Task<T4XenonGraphBlockLayoutResult> BuildAsync(
        string dumpRoot,
        string nativeFastFile,
        string nativeIdentityManifest,
        string bindingManifest,
        string outputDirectory,
        string canonicalBaseName,
        int expectedTechniqueSets = 87,
        int expectedPixelShaders = 897,
        CancellationToken cancellationToken = default,
        string? imageGraphDumpRoot = null,
        int expectedImages = 0,
        int imageGraftLimit = 0)
    {
        var graph = T4XenonTechniqueGraphImporter.Load(
            dumpRoot, expectedTechniqueSets, expectedPixelShaders);
        if (!graph.Verified)
            throw new InvalidDataException("Stage 4D requires the verified Stage 4B graph.");

        T4XenonImageGraphImportResult? imageGraph = null;
        if (!string.IsNullOrWhiteSpace(imageGraphDumpRoot))
        {
            imageGraph = T4XenonImageGraphImporter.Load(
                imageGraphDumpRoot,
                expectedImages > 0 ? expectedImages : null);
            if (!imageGraph.Verified)
                throw new InvalidDataException(
                    "Stage 4D image graph is not verified: " +
                    string.Join(" | ", imageGraph.Errors.Take(20)));
        }

        var native = await FastFileAnalysisCache.GetZoneAsync(nativeFastFile, cancellationToken);
        if (native.Platform != WaWPlatform.Xbox360)
            throw new InvalidDataException("Stage 4D requires an Xbox360 native oracle.");

        var roots = LoadNativeIdentity(nativeIdentityManifest);
        var bindings = LoadBindings(bindingManifest);
        var blockers = new List<string>();
        var notes = new List<string>();

        if (!bindings.Ready) blockers.Add("Stage 4C root binding is not ready.");
        if (roots.Count != native.Document.AssetCount)
            blockers.Add($"Native root count mismatch: identity={roots.Count:N0}, parsed={native.Document.AssetCount:N0}.");

        var techByRoot = bindings.Techniques.ToDictionary(x => x.NativeRootIndex);
        var pixelByRoot = bindings.Pixels.ToDictionary(x => x.NativeRootIndex);
        var pixelByName = bindings.Pixels.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

        if (techByRoot.Count != expectedTechniqueSets)
            blockers.Add($"TechniqueSet binding count {techByRoot.Count:N0} != {expectedTechniqueSets:N0}.");
        if (pixelByRoot.Count != expectedPixelShaders)
            blockers.Add($"PixelShader binding count {pixelByRoot.Count:N0} != {expectedPixelShaders:N0}.");

        var pixelDefs = CollectPixelShaders(graph, blockers);
        var state = new CursorState();
        var nodes = new List<T4XenonLayoutNode>();
        var rendererRoots = new List<T4XenonRendererRootLayout>();
        var pixelStable = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var techniques = new Dictionary<string, long>(StringComparer.Ordinal);
        var decls = new Dictionary<string, long>(StringComparer.Ordinal);
        var vertexShaders = new Dictionary<string, long>(StringComparer.Ordinal);
        var literals = new Dictionary<string, long>(StringComparer.Ordinal);

        var forwardPixelRefs = 0;
        var reusedTechniques = 0;
        var reusedDecls = 0;
        var reusedVertexShaders = 0;
        var reusedLiterals = 0;
        var rendererTechsets = 0;
        var rendererPixels = 0;
        var realImages = 0;
        var targetImageRoots = imageGraph is null
            ? 0
            : imageGraftLimit > 0
                ? Math.Min(imageGraftLimit, imageGraph.Images)
                : imageGraph.Images;

        // XAssetList lives outside the blocks. Script pointer array, strings and
        // XAsset[] are the first VIRTUAL allocations.
        if (native.Document.ScriptStrings.Count != 0)
        {
            state.VirtualAlloc(native.Document.ScriptStrings.Count * 4L, 4, true);
            foreach (var s in native.Document.ScriptStrings)
            {
                if (s.RawPointer == 0) continue;
                if (s.Value is null || s.Value.StartsWith("<alias ", StringComparison.Ordinal))
                {
                    blockers.Add($"Unresolved native ScriptString #{s.Index}.");
                    continue;
                }
                state.VirtualAlloc(Utf8Z(s.Value), 1, true);
            }
        }
        state.VirtualAlloc(roots.Count * 8L, 4, true);

        foreach (var root in roots.OrderBy(x => x.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var insert = state.VirtualAlloc(4, 4, false);
            var stable = SafePointer(T4ZoneBlock.Virtual, insert, blockers);
            state.InsertSlots++;

            if (pixelByRoot.TryGetValue(root.Index, out var pixelBinding))
            {
                rendererPixels++;
                state.TempRoot(PixelShaderSize);
                rendererRoots.Add(new(root.Index, root.TypeName, root.Name, insert, stable));

                if (!pixelDefs.TryGetValue(pixelBinding.Name, out var ps))
                {
                    blockers.Add($"Missing graph PixelShader '{pixelBinding.Name}'.");
                    state.VirtualAlloc(Utf8Z(root.Name), 1, true);
                }
                else
                {
                    state.VirtualAlloc(Utf8Z(root.Name), 1, true);
                    EmitShaderLoadDef(state, ps);
                    pixelStable[root.Name] = stable;
                }
                continue;
            }

            if (techByRoot.TryGetValue(root.Index, out var techBinding))
            {
                rendererTechsets++;
                state.TempRoot(TechniqueSetSize);
                rendererRoots.Add(new(root.Index, root.TypeName, root.Name, insert, stable));
                state.VirtualAlloc(Utf8Z(root.Name), 1, true);

                if (!graph.TechniqueSetsByName.TryGetValue(techBinding.Name, out var ts))
                {
                    blockers.Add($"Missing graph TechniqueSet '{techBinding.Name}'.");
                    continue;
                }

                foreach (var slot in ts.Slots)
                {
                    if (slot.Technique is null) continue;
                    var tk = Hash(slot.Technique);
                    if (techniques.ContainsKey(tk))
                    {
                        reusedTechniques++;
                        continue;
                    }

                    var to = state.VirtualAlloc(
                        TechniqueHeaderSize + slot.Technique.Passes.Count * (long)PassSize, 4, true);
                    techniques.Add(tk, to);
                    nodes.Add(Node("technique", tk, T4ZoneBlock.Virtual, to,
                        TechniqueHeaderSize + slot.Technique.Passes.Count * (long)PassSize, blockers));

                    foreach (var pass in slot.Technique.Passes)
                    {
                        if (pass.VertexDecl is not null)
                        {
                            var dk = Hash(pass.VertexDecl);
                            if (decls.ContainsKey(dk)) reusedDecls++;
                            else
                            {
                                var d = state.VirtualAlloc(VertexDeclSize, 4, true);
                                decls.Add(dk, d);
                                nodes.Add(Node("vertex-decl", dk, T4ZoneBlock.Virtual, d, VertexDeclSize, blockers));
                            }
                        }

                        foreach (var vs in pass.VertexShaderArray)
                            EmitVertexShader(state, vs, vertexShaders, nodes, blockers, ref reusedVertexShaders);
                        EmitVertexShader(state, pass.VertexShader, vertexShaders, nodes, blockers, ref reusedVertexShaders);

                        var psName = pass.PixelShader?.Name;
                        if (!string.IsNullOrWhiteSpace(psName))
                        {
                            if (!pixelByName.ContainsKey(psName))
                                blockers.Add($"TechniqueSet '{ts.Name}' references unbound PixelShader '{psName}'.");
                            else if (!pixelStable.ContainsKey(psName))
                                forwardPixelRefs++;
                        }

                        if (pass.Args.Count != 0)
                        {
                            state.VirtualAlloc(pass.Args.Count * (long)ShaderArgSize, 4, true);
                            foreach (var arg in pass.Args)
                            {
                                if (arg.LiteralConst is null) continue;
                                var lk = LiteralKey(arg.LiteralConst);
                                if (literals.ContainsKey(lk)) reusedLiterals++;
                                else
                                {
                                    var lo = state.VirtualAlloc(LiteralSize, 4, true);
                                    literals.Add(lk, lo);
                                    nodes.Add(Node("literal", lk, T4ZoneBlock.Virtual, lo, LiteralSize, blockers));
                                }
                            }
                        }
                    }

                    // T4 Xenon ZoneCode explicitly reorders MaterialTechnique:
                    // passArray first, then name. All pass child traversal must
                    // complete before the technique XString is serialized.
                    if (!string.IsNullOrEmpty(slot.Technique.Name))
                        state.VirtualAlloc(Utf8Z(slot.Technique.Name), 1, true);
                }
                continue;
            }

            if (imageGraph is not null &&
                realImages < targetImageRoots &&
                Normalize(root.TypeName) == "image" &&
                imageGraph.ImagesByName.TryGetValue(root.Name, out var image))
            {
                realImages++;

                // GfxImage (40) and its GfxImageLoadDef (16) share one TEMP root scope.
                state.TempRoot(56);

                // GfxImageLoadDef is reusable in T4 Xenon ZoneCode, so its first
                // body reserves a non-serialized VIRTUAL insert slot.
                state.VirtualAlloc(4, 4, false);

                // ZoneCode child order: name, pixels, texture.
                state.VirtualAlloc(Utf8Z(image.Name), 1, true);
                if (image.PixelsBytes != 0)
                {
                    // T4 Xenon uses LARGE_RUNTIME for delayed map textures and
                    // LARGE for immediate/generated images. The graph preserves
                    // delayLoadPixels from OAT, so the block choice must follow it.
                    if (image.DelayLoadPixels)
                        state.LargeRuntimeAlloc(image.PixelsBytes, 4096, true);
                    else
                        state.LargeAlloc(image.PixelsBytes, 4096, true);
                }
                state.VirtualAlloc(52, 4, true);
                continue;
            }

            var shellSize = ShellSize(root.TypeName);
            if (shellSize == 0)
            {
                blockers.Add($"Unsupported all-name-shell type '{root.TypeName}' at root #{root.Index}.");
                shellSize = 4;
            }
            state.TempRoot(shellSize);
            EmitShellChildren(state, root, canonicalBaseName);
        }

        if (forwardPixelRefs != 0)
            blockers.Add($"{forwardPixelRefs:N0} PixelShader reference(s) occur before their top-level root insert slot.");
        if (rendererTechsets != expectedTechniqueSets)
            blockers.Add($"Upgraded TechniqueSets={rendererTechsets:N0}; expected {expectedTechniqueSets:N0}.");
        if (rendererPixels != expectedPixelShaders)
            blockers.Add($"Upgraded PixelShaders={rendererPixels:N0}; expected {expectedPixelShaders:N0}.");
        if (imageGraph is not null && realImages != targetImageRoots)
            blockers.Add($"Upgraded image roots={realImages:N0}; target={targetImageRoots:N0}; graph contains {imageGraph.Images:N0}.");
        if (state.Virtual > 0x20000000L || state.Physical > 0x20000000L ||
            state.LargeRuntime > 0x20000000L || state.Large > 0x20000000L)
            blockers.Add("Projected packed-pointer block exceeds T4 29-bit block offset space.");

        var ready = blockers.Count == 0;
        notes.Add("TEMP is modeled as scratch: every top-level asset starts at TEMP+0; header TEMP size is the maximum root scope.");
        notes.Add("Each first top-level root reserves one 4-byte VIRTUAL insert slot; insert slots and alignment holes consume block address space but no serialized bytes.");
        notes.Add("TechniqueSet children/cached shader parts are VIRTUAL; physical shader parts are PHYSICAL aligned to 32 bytes.");
        if (imageGraph is not null)
            notes.Add($"Image graph graft: {realImages:N0}/{imageGraph.Images:N0} real GfxImage roots; limit={(imageGraftLimit > 0 ? imageGraftLimit : imageGraph.Images):N0}; LARGE_RUNTIME={state.LargeRuntime:N0} bytes; LARGE={state.Large:N0} bytes; source graph is validation-only unless independently PC-sourced.");
        notes.Add(ready
            ? "BLOCK LAYOUT CLOSURE: PASS. Stage 4E can write bytes against these exact cursors."
            : "BLOCK LAYOUT CLOSURE: BLOCKED. Stage 4E remains fenced.");

        Directory.CreateDirectory(outputDirectory);
        var txt = Path.Combine(outputDirectory, "nuclear_xenon_block_layout.txt");
        var json = Path.Combine(outputDirectory, "nuclear_xenon_block_layout.json");
        var result = new T4XenonGraphBlockLayoutResult
        {
            NativeRoots = roots.Count,
            ScriptStrings = native.Document.ScriptStrings.Count,
            RendererTechniqueSets = rendererTechsets,
            RendererPixelShaders = rendererPixels,
            RealImageRoots = realImages,
            LargeRuntimeBytes = state.LargeRuntime,
            LargeBytes = state.Large,
            InsertSlots = state.InsertSlots,
            ForwardPixelShaderReferences = forwardPixelRefs,
            ReusedTechniques = reusedTechniques,
            ReusedVertexDecls = reusedDecls,
            ReusedVertexShaders = reusedVertexShaders,
            ReusedLiteralConstants = reusedLiterals,
            TempBytes = state.TempMax,
            VirtualBytes = state.Virtual,
            PhysicalBytes = state.Physical,
            SerializedBlockBytes = state.Serialized,
            ProjectedUncompressedZoneBytes = 36L + 16L + state.Serialized,
            RendererRoots = rendererRoots,
            Nodes = nodes,
            Blockers = blockers,
            Notes = notes,
            LayoutReady = ready,
            TextReportFile = txt,
            JsonManifestFile = json
        };

        await File.WriteAllTextAsync(txt, Render(result), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(json,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false), cancellationToken);
        return result;
    }

    private static void EmitVertexShader(
        CursorState state,
        T4XenonGraphShaderRef? shader,
        IDictionary<string, long> seen,
        ICollection<T4XenonLayoutNode> nodes,
        ICollection<string> blockers,
        ref int reused)
    {
        if (shader is null) return;
        var key = ShaderKey(shader);
        if (seen.ContainsKey(key)) { reused++; return; }

        var off = state.VirtualAlloc(VertexShaderSize, 4, true);
        seen.Add(key, off);
        nodes.Add(Node("vertex-shader", key, T4ZoneBlock.Virtual, off, VertexShaderSize, blockers));
        if (!string.IsNullOrEmpty(shader.Name)) state.VirtualAlloc(Utf8Z(shader.Name), 1, true);
        EmitShaderLoadDef(state, shader);
    }

    private static void EmitShaderLoadDef(CursorState state, T4XenonGraphShaderRef shader)
    {
        if (shader.PhysicalPartSize != 0) state.PhysicalAlloc(shader.PhysicalPartSize, 32, true);
        if (shader.CachedPartSize != 0) state.VirtualAlloc(shader.CachedPartSize, 4, true);
    }

    private static void EmitShellChildren(
        CursorState state,
        T4XenonNativeIdentityEntry root,
        string canonicalBaseName)
    {
        var type = Normalize(root.TypeName);
        if (type == "localize")
        {
            state.VirtualAlloc(1, 1, true);
            state.VirtualAlloc(Utf8Z(root.Name), 1, true);
        }
        else if (type == "gfxworld")
        {
            state.VirtualAlloc(Utf8Z(root.Name), 1, true);
            state.VirtualAlloc(Utf8Z(canonicalBaseName), 1, true);
        }
        else
        {
            state.VirtualAlloc(Utf8Z(root.Name), 1, true);
        }
    }

    private static Dictionary<string, T4XenonGraphShaderRef> CollectPixelShaders(
        T4XenonGraphImportResult graph,
        ICollection<string> blockers)
    {
        var map = new Dictionary<string, T4XenonGraphShaderRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var ts in graph.TechniqueSetsByName.Values)
        foreach (var slot in ts.Slots)
        {
            if (slot.Technique is null) continue;
            foreach (var pass in slot.Technique.Passes)
            {
                var ps = pass.PixelShader;
                if (ps is null || string.IsNullOrWhiteSpace(ps.Name)) continue;
                if (!map.TryGetValue(ps.Name, out var prior)) map.Add(ps.Name, ps);
                else if (!string.Equals(ShaderSignature(prior), ShaderSignature(ps), StringComparison.Ordinal))
                    blockers.Add($"PixelShader '{ps.Name}' has conflicting blob signatures.");
            }
        }
        return map;
    }

    private static T4XenonLayoutNode Node(
        string kind, string key, T4ZoneBlock block, long offset, long size, ICollection<string> blockers) =>
        new(kind, key, block.ToString(), offset, size, SafePointer(block, offset, blockers));

    private static uint SafePointer(T4ZoneBlock block, long offset, ICollection<string> blockers)
    {
        try { return T4ZonePointer.Encode(block, checked((uint)offset)); }
        catch (Exception ex)
        {
            blockers.Add($"Packed pointer {block}+0x{offset:X} failed: {ex.Message}");
            return 0;
        }
    }

    private static int ShellSize(string typeName) => Normalize(typeName) switch
    {
        "xanim" => 92,
        "xmodel" => 0xD4,
        "material" => 96,
        "pixelshader" => 16,
        "techset" or "techniqueset" => 216,
        "image" => 40,
        "sound" => 12,
        "loadedsound" => 160,
        "weapon" => 0x9AC,
        "fx" => 36,
        "rawfile" => 12,
        "physpreset" => 48,
        "clipmappvs" or "clipmap" => 332,
        "comworld" => 64,
        "gameworldsp" => 44,
        "gfxworld" => 900,
        "localize" => 8,
        "mapents" => 12,
        _ => 0
    };

    private static string Normalize(string value) =>
        value.Replace("_", string.Empty, StringComparison.Ordinal)
             .Replace("-", string.Empty, StringComparison.Ordinal)
             .ToLowerInvariant();

    private static string ShaderKey(T4XenonGraphShaderRef shader) =>
        !string.IsNullOrWhiteSpace(shader.Name) ? shader.Name! : "<anon>:" + ShaderSignature(shader);

    private static string ShaderSignature(T4XenonGraphShaderRef shader) =>
        string.Join("|", shader.CachedPartSize, shader.PhysicalPartSize,
            (shader.CachedFile ?? "").Replace('\\', '/').ToLowerInvariant(),
            (shader.PhysicalFile ?? "").Replace('\\', '/').ToLowerInvariant());

    private static string LiteralKey(IReadOnlyList<float> v) =>
        string.Join(",", v.Select(x => BitConverter.SingleToInt32Bits(x).ToString("X8")));

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static int Utf8Z(string value) => Encoding.UTF8.GetByteCount(value) + 1;

    private sealed record BindingData(
        bool Ready,
        IReadOnlyList<T4XenonGraphRootBindingEntry> Techniques,
        IReadOnlyList<T4XenonGraphRootBindingEntry> Pixels);

    private static BindingData LoadBindings(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var r = doc.RootElement;
        if (r.GetProperty("format").GetString() != "WaW360.T4Xenon.GraphRootBinding.v1")
            throw new InvalidDataException("Unexpected Stage 4C binding manifest.");
        return new(
            r.GetProperty("exactRootBindingReady").GetBoolean(),
            ParseBindings(r.GetProperty("techniqueSetBindings")),
            ParseBindings(r.GetProperty("pixelShaderBindings")));
    }

    private static IReadOnlyList<T4XenonGraphRootBindingEntry> ParseBindings(JsonElement a) =>
        a.EnumerateArray().Select(e => new T4XenonGraphRootBindingEntry(
            e.GetProperty("Kind").GetString() ?? "",
            e.GetProperty("Name").GetString() ?? "",
            e.GetProperty("NativeRootIndex").GetInt32(),
            e.GetProperty("NativeTypeId").GetInt32(),
            e.GetProperty("NativeTypeName").GetString() ?? "")).ToList();

    private static IReadOnlyList<T4XenonNativeIdentityEntry> LoadNativeIdentity(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var r = doc.RootElement;
        if (r.GetProperty("format").GetString() != "WaW360.T4Xenon.NativeIdentityManifest.v1" ||
            !r.GetProperty("typeSequenceVerified").GetBoolean())
            throw new InvalidDataException("Stage 4C native identity manifest is not verified.");

        return r.GetProperty("entries").EnumerateArray().Select(e => new T4XenonNativeIdentityEntry(
            e.GetProperty("Index").GetInt32(),
            e.GetProperty("TypeId").GetInt32(),
            e.GetProperty("TypeName").GetString() ?? "",
            e.GetProperty("OatTypeName").GetString() ?? "",
            e.GetProperty("Name").GetString() ?? "")).OrderBy(x => x.Index).ToList();
    }

    private static string Render(T4XenonGraphBlockLayoutResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("NUCLEAR STAGE 4D - OAT-STYLE XENON BLOCK LAYOUT");
        sb.AppendLine("================================================");
        sb.AppendLine($"Native roots:            {r.NativeRoots:N0}");
        sb.AppendLine($"ScriptStrings:           {r.ScriptStrings:N0}");
        sb.AppendLine($"Renderer TechniqueSets:  {r.RendererTechniqueSets:N0}");
        sb.AppendLine($"Renderer PixelShaders:   {r.RendererPixelShaders:N0}");
        sb.AppendLine($"Real image roots:        {r.RealImageRoots:N0}");
        sb.AppendLine($"LARGE_RUNTIME:           0x{r.LargeRuntimeBytes:X}");
        sb.AppendLine($"VIRTUAL insert slots:    {r.InsertSlots:N0}");
        sb.AppendLine($"Forward PixelShader refs:{r.ForwardPixelShaderReferences,10:N0}");
        sb.AppendLine($"TEMP max:                0x{r.TempBytes:X}");
        sb.AppendLine($"VIRTUAL final:           0x{r.VirtualBytes:X}");
        sb.AppendLine($"PHYSICAL final:          0x{r.PhysicalBytes:X}");
        sb.AppendLine($"Serialized block bytes:  {r.SerializedBlockBytes:N0}");
        sb.AppendLine($"Projected zone bytes:    {r.ProjectedUncompressedZoneBytes:N0}");
        sb.AppendLine($"LAYOUT READY:            {r.LayoutReady}");
        sb.AppendLine();
        foreach (var blocker in r.Blockers) sb.AppendLine("BLOCKER: " + blocker);
        foreach (var note in r.Notes) sb.AppendLine(note);
        return sb.ToString();
    }

    private sealed class CursorState
    {
        private long _virtual;
        private long _physical;
        private long _largeRuntime;
        private long _large;

        public long Virtual => _virtual;
        public long Physical => _physical;
        public long LargeRuntime => _largeRuntime;
        public long Large => _large;
        public long TempMax { get; private set; }
        public long Serialized { get; private set; }
        public int InsertSlots { get; set; }

        public long VirtualAlloc(long size, int align, bool serialized) =>
            Allocate(ref _virtual, size, align, serialized);

        public long PhysicalAlloc(long size, int align, bool serialized) =>
            Allocate(ref _physical, size, align, serialized);

        public long LargeRuntimeAlloc(long size, int align, bool serialized) =>
            Allocate(ref _largeRuntime, size, align, serialized);

        public long LargeAlloc(long size, int align, bool serialized) =>
            Allocate(ref _large, size, align, serialized);

        public void TempRoot(int size)
        {
            TempMax = Math.Max(TempMax, size);
            Serialized += size;
        }

        private long Allocate(ref long cursor, long size, int align, bool serialized)
        {
            var mask = align - 1L;
            cursor = (cursor + mask) & ~mask;
            var result = cursor;
            cursor += size;
            if (serialized) Serialized += size;
            return result;
        }
    }
}

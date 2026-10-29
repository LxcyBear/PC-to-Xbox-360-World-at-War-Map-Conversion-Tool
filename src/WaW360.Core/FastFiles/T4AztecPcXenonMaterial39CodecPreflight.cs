using System.Text.Json;

namespace WaW360.Core.FastFiles;

/// <summary>
/// First direct bridge into the EXISTING Xenon Material graph codec:
/// load all 39 original-PC converted 96-byte material blobs with a strictly
/// source-owned binding manifest, validate all graph shapes and attempt
/// real codec planning. A binding pass means ROOT IDENTITY ONLY, not closure.
/// This never calls the actual FF emitter or marks any Xbox gate passing.
/// </summary>
public static class T4AztecPcXenonMaterial39CodecPreflight
{
    public static async Task<AztecPcXenon39CodecPreflightReport> RunAsync(
        string candidateReport, string nativeIdentityFile,
        string outputDirectory, CancellationToken ct=default)
    {
        using var cd=JsonDocument.Parse(await File.ReadAllTextAsync(candidateReport,ct));
        using var nd=JsonDocument.Parse(await File.ReadAllTextAsync(nativeIdentityFile,ct));
        var c=cd.RootElement; var n=nd.RootElement;
        if(c.GetProperty("Schema").GetString()!="WaW360.AztecPcXenonMaterial39SourceBridge.v1" ||
           c.GetProperty("Status").GetString()!="AZTEC_39_PC_MATERIALS_TO_XENON_96B_SOURCE_GRAPH_CANDIDATES_ONLY" ||
           c.GetProperty("SourceMaterialHeaders").GetInt32()!=39 ||
           c.GetProperty("SourceMaterialChildrenConverted").GetInt32()!=39 ||
           c.GetProperty("CoveredWorldSurfaces").GetInt32()!=735 ||
           c.GetProperty("CoveredWorldTriangles").GetInt32()!=19551 ||
           c.GetProperty("TextureDescriptors").GetInt32()!=87 ||
           c.GetProperty("ConstantRecords").GetInt32()!=93 ||
           c.GetProperty("StateBitsRecords").GetInt32()!=192 ||
           c.GetProperty("RealXenonMaterialRootsEmitted").GetBoolean() ||
           c.GetProperty("IntegratedOatFfValidated").GetBoolean() ||
           c.GetProperty("XboxHardwareReady").GetBoolean() ||
           n.GetProperty("format").GetString()!="WaW360.T4Xenon.NativeIdentityManifest.v1" ||
           n.GetProperty("assets").GetInt32()!=8509 ||
           !n.GetProperty("typeSequenceVerified").GetBoolean())
            throw new InvalidDataException("Exact source-owned 39 Material/Xenon candidate and 8509 native root identity required.");
        if(Path.GetFullPath(nativeIdentityFile)!=Path.GetFullPath(
            c.GetProperty("OriginalXboxNativeIdentityManifest").GetString()!))
            throw new InvalidDataException("Native Xbox identity manifest differs from candidate source proof.");

        var roots=JsonSerializer.Deserialize<List<T4XenonNativeIdentityEntry>>(
            n.GetProperty("entries").GetRawText(),new JsonSerializerOptions{
                PropertyNameCaseInsensitive=true
            }) ?? throw new InvalidDataException("Native root identities missing.");
        var materials=c.GetProperty("Materials").EnumerateArray().ToArray();
        if(roots.Count!=8509 || materials.Length!=39 ||
           roots.Where((x,i)=>x.Index!=i).Any())
            throw new InvalidDataException("Native root order or 39 owned Material corpus changed.");

        var dir=Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(dir);
        var graphRoot=Path.GetFullPath(c.GetProperty("CandidateGraphRoot").GetString()!);
        var rootDir=Path.Combine(graphRoot,"xenon_material_graphs");
        if(!Directory.Exists(rootDir) || Directory.GetFiles(rootDir,"*.json",SearchOption.AllDirectories).Length!=39)
            throw new InvalidDataException("Exactly 39 source-converted Material graph JSON entries required.");

        // Only this isolated, unique 39-world-graph source-candidate binding
        // is accepted. The already existing production FRUmansion 502
        // material owner list and old Aztec two-canary owners are untouched.
        var rows=new List<object>(39);
        var seen=new HashSet<int>();
        var usedBy=new HashSet<string>(StringComparer.Ordinal);
        foreach(var m in materials)
        {
            ct.ThrowIfCancellationRequested();
            int id=m.GetProperty("GoldenXboxRootIndex").GetInt32();
            string name=m.GetProperty("VerifiedMaterialName").GetString()!;
            int ordinal=m.GetProperty("MaterialMemoryOrdinal").GetInt32();
            if(id<0||id>=roots.Count||!seen.Add(id)||ordinal!=rows.Count ||
               !usedBy.Add(name)||
               roots[id].OatTypeName!="material" || roots[id].Name!=name ||
               m.GetProperty("WorldSurfaceCount").GetInt32()<=0 ||
               m.GetProperty("WorldTriangleCount").GetInt32()<=0 ||
               m.GetProperty("RealXenonWorldMaterialRootWritten").GetBoolean() ||
               m.GetProperty("OatLoadedMaterialGraphVerified").GetBoolean())
                throw new InvalidDataException($"Invalid source-owned Material graph/name/root identity at ordinal {ordinal}.");
            rows.Add(new{
                NativeRootIndex=id,NativeName=name,CorpusName=name,
                PcSourceMaterialOrdinal=ordinal
            });
        }

        var bridge=Path.Combine(dir,"aztec_39_source_technique_mapping_EMPTY_NOT_VERIFIED.json");
        await File.WriteAllTextAsync(bridge,
            JsonSerializer.Serialize(new{
                Format="WaW360.Aztec39MaterialTechniqueMappingCandidate.v1",
                Mappings=Array.Empty<object>(),
                ValidationOnly=true,
                Note="NO automatic technique substitution; explicit Xbox technique name mapping remains pending."
            },new JsonSerializerOptions{WriteIndented=true}),ct);

        var bindingPath=Path.Combine(dir,"aztec_39_source_material_root_identity_binding_NOT_RUNTIME.json");
        await File.WriteAllTextAsync(bindingPath,
            JsonSerializer.Serialize(new {
                Format="WaW360.AztecPc39MaterialCodecIdentityCandidate.v1",
                BindingPass=true,
                ValidationOnly=true,
                NativeRoots=8509,
                BridgeManifest=bridge,
                OwnedBindings=rows,
                RuntimeMaterialBodiesWritten=false,
                CompleteDependencyClosure=false,
                Note="BindingPass validates only 39 native root INDEX/name owners; no FF, no converted shaders/images."
            },new JsonSerializerOptions{WriteIndented=true}),ct);

        // EXISTING CODEC validates all graph manifests and ABI fields here;
        // this is the code path the integrated writer also consumes.
        var corpus=T4XenonMaterialCorpus.Load(graphRoot,bindingPath,roots,expectedOwned:39);
        if(corpus.OwnedByRoot.Count!=39)
            throw new InvalidDataException("Native MaterialGraphCodec did not load exactly 39 source-owned bodies.");
        var codecErrors=corpus.ValidateDependencies(roots).ToArray();

        var unresolved=new List<AztecPcXenon39UnresolvedReference>();
        var seenUnresolved=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var planFailures=new List<AztecPcXenon39PlanFailure>();
        var planned=0;
        var sourceBytesPlanned=0L;
        var unverifiedLegacyTail=0;
        int countExact=0,countMissing=0,countAmbiguous=0;
        foreach(var m in materials)
        {
            ct.ThrowIfCancellationRequested();
            var name=m.GetProperty("VerifiedMaterialName").GetString()!;
            var id=m.GetProperty("GoldenXboxRootIndex").GetInt32();
            var item=corpus.OwnedByRoot[id];
            if(item.Graph.Name!=name || item.Graph.Abi.Material!=96 ||
               item.Graph.Counts.Textures!=m.GetProperty("TextureCount").GetInt32() ||
               item.Graph.Counts.Constants!=m.GetProperty("ConstantCount").GetInt32() ||
               item.Graph.Counts.StateBits!=m.GetProperty("StateBitsCount").GetInt32())
                throw new InvalidDataException($"Existing MaterialGraphCodec parsed mismatched ABI fields for {name}.");

            if(m.GetProperty("TruncatedPcStateBitsTailHasNonFFBytes").GetBoolean())
                unverifiedLegacyTail++;

            void Analyze(string type,string? nameCandidate)
            {
                if(string.IsNullOrWhiteSpace(nameCandidate)) return;
                var types=type=="image"
                    ?new[]{"image"}:new[]{"techset","techniqueset"};
                var matches=T4XenonMaterialCorpus.FindDependencyCandidates(roots,types,nameCandidate!);
                if(matches.Count==1){countExact++;return;}
                if(matches.Count==0)countMissing++;
                else countAmbiguous++;
                var key=type+":"+nameCandidate;
                if(!seenUnresolved.Add(key))return;
                // Suggestions are for human investigation only; never
                // choose a donor name or invent a cross-platform mapping.
                string shortName=nameCandidate!.TrimStart(',');
                int last=Math.Max(shortName.LastIndexOf('/'),shortName.LastIndexOf('\\'));
                string basename=shortName[(last+1)..];
                var closest=roots
                    .Where(x=>types.Contains(x.OatTypeName))
                    .Select(x=>x.Name)
                    .Where(x=>x.Contains(basename,StringComparison.OrdinalIgnoreCase) ||
                        (x.Contains(shortName,StringComparison.OrdinalIgnoreCase)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(6).ToArray();
                unresolved.Add(new(type,nameCandidate!,matches.Count,
                    T4XenonMaterialCorpus.IsExplicitReference(nameCandidate!),
                    basename,closest,materials.Count(x=>x.GetProperty("DependencyCandidates")
                        .EnumerateArray().Any(dep=>
                            dep.GetProperty("Type").GetString()==type &&
                            string.Equals(dep.GetProperty("OriginalPcName").GetString(),
                                nameCandidate,StringComparison.OrdinalIgnoreCase)))));
            }
            Analyze("techset",item.ProductionTechniqueSet);
            foreach(var texture in item.Graph.Textures)
                Analyze("image",texture.Image);
            long bytes=0;
            var scopes=new List<int>();
            try
            {
                T4XenonMaterialGraphCodec.Plan(item,roots,
                    (length,alignment,_)=>{
                        if(length<0||length>1_000_000||alignment<=0 ||
                           (alignment&(alignment-1))!=0)
                            throw new InvalidDataException("Material plan allocation invalid.");
                        bytes=checked(bytes+length);
                    },scope=>{
                        if(scope<=0||scope>1_000_000)
                            throw new InvalidDataException("Material plan TEMP allocation invalid.");
                        scopes.Add(scope);
                    });
                if(scopes.Count!=1)
                    throw new InvalidDataException("Expected exactly one Material TEMP root scope.");
                planned++;sourceBytesPlanned=checked(sourceBytesPlanned+bytes);
            }
            catch(Exception e) when(e is InvalidDataException or InvalidOperationException)
            {
                planFailures.Add(new(name,id,e.Message));
            }
        }
        if(countExact+countMissing+countAmbiguous!=
           materials.Sum(x=>x.GetProperty("DependencyCandidates").GetArrayLength()))
            throw new InvalidDataException("Native renderer MaterialGraphCodec dependency inventory differs from original-PC candidate report.");

        var result=new AztecPcXenon39CodecPreflightReport(
            "WaW360.AztecPcXenonMaterial39CodecPreflight.v1",
            "AZTEC_39_SOURCE_XENON_MATERIAL_GRAPHS_EXISTING_CODEC_LOAD_PASS_DEPENDENCY_AUDIT",
            Path.GetFullPath(candidateReport),Path.GetFullPath(nativeIdentityFile),
            bindingPath,bridge,graphRoot,
            39,735,19551,
            countExact,countMissing,countAmbiguous,unresolved.Count,
            codecErrors.Length,planned,planFailures.Count,sourceBytesPlanned,
            unverifiedLegacyTail,
            unresolved.OrderBy(x=>x.Type).ThenBy(x=>x.OriginalPcName).ToArray(),
            codecErrors,planFailures.ToArray(),
            false,false,false,false,
            "All 39 original-PC derived Xenon ABI graph files were loaded and shape-" +
            "validated by the EXISTING native FF writer MaterialGraphCodec.Load. " +
            "The codec's real allocation planner was exercised for each entry " +
            "and all missing/ambiguous dependency names recorded. The binding " +
            "manifest is intentionally source-only, NOT production authorized: " +
            "39/39 real FF roots, semantic stateBits mapping, image and technique " +
            "cross-platform dependencies, GfxWorld/DPVS and OAT FF all PENDING. " +
            "No FF/hardware payload emitted.");
        var report=Path.Combine(dir,"aztec_39_material_existing_codec_preflight.json");
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(result,
            new JsonSerializerOptions{WriteIndented=true}),ct);
        return result;
    }
}
public sealed record AztecPcXenon39UnresolvedReference(
    string Type,string OriginalPcName,int NativeRootMatches,
    bool AlreadyExplicitExternalReference,string SourceBasename,
    string[] HumanReviewCandidates,int AffectedMaterialCount);
public sealed record AztecPcXenon39PlanFailure(
    string MaterialName,int NativeRootIndex,string PlanError);
public sealed record AztecPcXenon39CodecPreflightReport(
    string Schema,string Status,
    string CandidateMaterialCorpusReport,string NativeXboxIdentityManifest,
    string SourceOnlyRootBindingManifest,string UnverifiedTechniqueMappingManifest,
    string CandidateGraphRoot,
    int MaterialGraphsLoadedByExistingCodec,int OriginalPcWorldSurfaces,
    int OriginalPcWorldTriangles,
    int UniqueNativeDependencyOccurrences,int MissingNativeDependencyOccurrences,
    int AmbiguousNativeDependencyOccurrences,int UniqueUnresolvedDependencyNames,
    int ExistingCodecDependencyErrorCount,int MaterialGraphsPlannedByExistingCodec,
    int ExistingCodecPlanFailures,long TotalPlannedVirtualChildBytes,
    int MaterialsWithUnverifiedPcStateBitsTail,
    IReadOnlyList<AztecPcXenon39UnresolvedReference> UnresolvedReferences,
    IReadOnlyList<string> ExistingCodecDependencyErrors,
    IReadOnlyList<AztecPcXenon39PlanFailure> PlanFailureDetails,
    bool RealXboxWorldMaterialBodiesEmitted,bool AllMaterialSemanticDependenciesVerified,
    bool IntegratedOatFastFileValidated,bool XboxHardwareReady,
    string Note);

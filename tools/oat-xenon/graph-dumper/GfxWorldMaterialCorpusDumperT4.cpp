#include "GfxWorldMaterialCorpusDumperT4.h"

#include <cmath>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <format>
#include <iomanip>
#include <ostream>
#include <sstream>
#include <string>
#include <string_view>
#include <vector>

using namespace T4;

namespace
{
    constexpr uintptr_t FOLLOWING = 0xFFFFFFFFu;
    template <typename T> T* Following()
    {
        return reinterpret_cast<T*>(FOLLOWING);
    }
    std::string SafeStem(const std::string_view value)
    {
        std::ostringstream os;
        for (const unsigned char ch : value)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') ||
                (ch >= '0' && ch <= '9') || ch == '-' ||
                ch == '_' || ch == '.')
                os << static_cast<char>(ch);
            else
                os << '_';
        }
        return os.str();
    }
    void JsonString(std::ostream& o, std::string_view s)
    {
        o << '"';
        for (const unsigned char ch : s)
        {
            switch (ch)
            {
            case '"': o << "\\\""; break;
            case '\\': o << "\\\\"; break;
            case '\n': o << "\\n"; break;
            case '\r': o << "\\r"; break;
            case '\t': o << "\\t"; break;
            default:
                if (ch < 32)
                    o << "\\u00" << std::uppercase << std::hex
                      << std::setw(2) << std::setfill('0')
                      << static_cast<unsigned>(ch) << std::dec
                      << std::nouppercase << std::setfill(' ');
                else o << static_cast<char>(ch);
            }
        }
        o << '"';
    }
    void JsonNullable(std::ostream& o, const char* s)
    {
        if (s) JsonString(o, s);
        else o << "null";
    }
    template <typename T>
    bool WriteBlob(AssetDumpingContext& context,
        const std::string& path, const T* data, size_t count)
    {
        auto dest = context.OpenAssetFile(path);
        if (!dest) return false;
        if (data && count)
            dest->write(reinterpret_cast<const char*>(data),
                static_cast<std::streamsize>(sizeof(T) * count));
        return dest->good();
    }
}

namespace gfxworld
{
    void MaterialCorpusDumperT4::DumpAsset(
        AssetDumpingContext& context,
        const XAssetInfo<GfxWorld>& asset)
    {
        static_assert(sizeof(void*) == 4, "PC loaded Material corpus needs x86 OAT.");

        const auto& world=*asset.Asset();
        if (world.materialMemoryCount != 39 ||
            world.surfaceCount != 735 || !world.materialMemory ||
            !world.dpvs.surfaces)
            return;
        const auto worldName=world.name
            ? std::string_view(world.name) : std::string_view(asset.m_name);
        const auto root=std::format("pc_gfxworld_39_material_source_corpus/{}",
            SafeStem(worldName));
        const auto jsonPath=root+"/manifest.json";
        auto dest=context.OpenAssetFile(jsonPath);
        if (!dest) return;
        auto& out=*dest;
        const bool abiPass=
            sizeof(Material)==112 &&
            sizeof(MaterialTextureDef)==16 &&
            sizeof(MaterialConstantDef)==32 &&
            sizeof(GfxStateBits)==8;
        out << "{\"format\":\"WaW360.T4PcGfxWorld39MaterialSourceCorpus.v1\"";
        out << ",\"originalPcWorld\":";
        JsonString(out,worldName);
        out << ",\"sourcePlatform\":\"PC_LOADED_T4_X86_LITTLE_ENDIAN\"";
        out << ",\"materialMemoryCount\":39,\"surfaceCount\":735";
        out << ",\"abi\":{\"material\":" << sizeof(Material)
            << ",\"textureDef\":" << sizeof(MaterialTextureDef)
            << ",\"constantDef\":" << sizeof(MaterialConstantDef)
            << ",\"stateBits\":" << sizeof(GfxStateBits) << '}';
        out << ",\"abiVerified\":" << (abiPass?"true":"false");
        // Proven original PC source world/root counts and internal DPVS
        // positions. This complements the independently measured Xenon ABI.
        // No original Xbox world child payloads are copied or inferred.
        out << ",\"pcWorldRootAbi\":{";
        out << "\"gfxWorldSize\":" << sizeof(GfxWorld);
        out << ",\"dpvsStaticSize\":" << sizeof(GfxWorldDpvsStatic);
        out << ",\"dpvsPlanesSize\":" << sizeof(GfxWorldDpvsPlanes);
        out << ",\"materialMemorySize\":" << sizeof(MaterialMemory);
        out << ",\"nameAt\":" << offsetof(GfxWorld, name);
        out << ",\"baseNameAt\":" << offsetof(GfxWorld, baseName);
        out << ",\"indexCountAt\":" << offsetof(GfxWorld, indexCount);
        out << ",\"indicesAt\":" << offsetof(GfxWorld, indices);
        out << ",\"surfaceCountAt\":" << offsetof(GfxWorld, surfaceCount);
        out << ",\"vertexCountAt\":" << offsetof(GfxWorld, vertexCount);
        out << ",\"vertexDataAt\":" << offsetof(GfxWorld, vd);
        out << ",\"vertexLayerDataAt\":" << offsetof(GfxWorld, vld);
        out << ",\"dpvsPlanesAt\":" << offsetof(GfxWorld, dpvsPlanes);
        out << ",\"materialMemoryAt\":" << offsetof(GfxWorld, materialMemory);
        out << ",\"dpvsStaticAt\":" << offsetof(GfxWorld, dpvs);
        out << ",\"dpvsStaticSurfaceCountAt\":"
            << offsetof(GfxWorldDpvsStatic, staticSurfaceCount);
        out << ",\"dpvsStaticSurfacesAt\":"
            << offsetof(GfxWorldDpvsStatic, surfaces);
        out << "}";
        out << ",\"pcLoadedWorldRootShape\":{";
        out << "\"planeCount\":" << world.planeCount;
        out << ",\"nodeCount\":" << world.nodeCount;
        out << ",\"cellCount\":" << world.dpvsPlanes.cellCount;
        out << ",\"indexCount\":" << world.indexCount;
        out << ",\"surfaceCount\":" << world.surfaceCount;
        out << ",\"vertexCount\":" << world.vertexCount;
        out << ",\"vertexLayerDataSize\":" << world.vertexLayerDataSize;
        out << ",\"materialMemoryCount\":" << world.materialMemoryCount;
        out << ",\"smodelCount\":" << world.dpvs.smodelCount;
        out << ",\"staticSurfaceCount\":" << world.dpvs.staticSurfaceCount;
        out << ",\"smodelVisDataCount\":" << world.dpvs.smodelVisDataCount;
        out << ",\"surfaceVisDataCount\":" << world.dpvs.surfaceVisDataCount;
        out << ",\"sortedSurfIndexPresent\":"
            << (world.dpvs.sortedSurfIndex ? "true":"false");
        out << ",\"surfacesPresent\":"
            << (world.dpvs.surfaces ? "true":"false");
        out << ",\"materialMemoryPresent\":"
            << (world.materialMemory ? "true":"false");
        out << ",\"indicesPresent\":"
            << (world.indices ? "true":"false");
        out << ",\"verticesPresent\":"
            << (world.vd.vertices ? "true":"false");
        out << "}";

        // Gate 8 ORIGINAL PC child-source export, additive to the existing
        // 39 Material corpus. PC OAT has already resolved all source pointers:
        // use typed arrays rather than inferring physical positions from
        // packed 0x84... zone offsets. This is NOT a Xenon payload.
        const bool gate8Shape =
            world.planeCount == 3447 &&
            world.nodeCount == 24511 &&
            world.dpvs.smodelCount == 1903 &&
            world.dpvs.staticSurfaceCount == 613 &&
            world.dpvsPlanes.cellCount == 11 &&
            sizeof(cplane_s) == 20 &&
            sizeof(uint16_t) == 2 &&
            world.dpvsPlanes.planes && world.dpvsPlanes.nodes &&
            world.dpvs.smodelDrawInsts &&
            world.vertexLayerDataSize > 0 &&
            world.vertexLayerDataSize <= 32 * 1024 * 1024 &&
            world.vld.data;
        const auto planesPath = root + "/gate8_planes_pc_le.bin";
        const auto nodesPath = root + "/gate8_nodes_pc_le.bin";
        const auto layerPath = root + "/gate8_vertex_layer_pc_raw.bin";
        bool gate8Blobs = false;
        if (gate8Shape)
        {
            gate8Blobs =
                WriteBlob(context, planesPath, world.dpvsPlanes.planes, static_cast<size_t>(world.planeCount)) &&
                WriteBlob(context, nodesPath, world.dpvsPlanes.nodes, static_cast<size_t>(world.nodeCount)) &&
                WriteBlob(context, layerPath, world.vld.data, static_cast<size_t>(world.vertexLayerDataSize));
        }
        out << ",\"gate8ChildSource\":{";
        out << "\"status\":";
        JsonString(out, gate8Blobs
            ? "T4_PC_GATE8_TYPED_CHILD_BLOBS_EXPORTED"
            : "T4_PC_GATE8_SOURCE_SHAPE_OR_EXPORT_BLOCKED");
        out << ",\"planeCount\":" << world.planeCount;
        out << ",\"nodeCount\":" << world.nodeCount;
        out << ",\"smodelCount\":" << world.dpvs.smodelCount;
        out << ",\"cellCount\":" << world.dpvsPlanes.cellCount;
        out << ",\"vertexLayerDataSize\":" << world.vertexLayerDataSize;
        out << ",\"planeStride\":" << sizeof(cplane_s);
        out << ",\"nodeStride\":" << sizeof(uint16_t);
        out << ",\"planesPath\":"; JsonString(out, planesPath);
        out << ",\"nodesPath\":"; JsonString(out, nodesPath);
        out << ",\"vertexLayerPath\":"; JsonString(out, layerPath);
        out << ",\"planesPresent\":" << (world.dpvsPlanes.planes ? "true":"false");
        out << ",\"nodesPresent\":" << (world.dpvsPlanes.nodes ? "true":"false");
        out << ",\"layerPresent\":" << (world.vld.data ? "true":"false");
        out << ",\"staticModelDrawInstsPresent\":"
            << (world.dpvs.smodelDrawInsts ? "true":"false");
        // Save ONLY source XModel identities here; the raw draw records
        // contain host-memory XModel* pointers and are never dumped verbatim.
        out << ",\"staticModelDrawNames\":[";
        if (world.dpvs.smodelDrawInsts &&
            world.dpvs.smodelCount <= 4096u)
        {
            // smodelCount is unsigned in the pinned T4 PC ABI.
            for (unsigned int i = 0; i < world.dpvs.smodelCount; ++i)
            {
                if (i) out << ',';
                const auto* model = world.dpvs.smodelDrawInsts[i].model;
                JsonNullable(out, model ? model->name : nullptr);
            }
        }
        out << "]}";

        // Gate11: ONE original-PC-loaded 11-cell typed topology pass.
        // No host pointer values or Xbox-reference bodies are exported.
        // Portals refer to target cells by validated ordinal, not address.
        const bool hasCells =
            world.dpvsPlanes.cellCount == 11 && world.cells != nullptr;
        bool cellTopologyConsistent = hasCells;
        unsigned totalAabb = 0, totalPortals = 0, totalCullRefs = 0;
        unsigned unresolvedPortalTargets = 0;
        out << ",\"gate11CellTopology\":{";
        out << "\"schema\":\"WaW360.T4PcGate11CellTopology.v1\"";
        out << ",\"cellCount\":" << world.dpvsPlanes.cellCount;
        out << ",\"cellsPresent\":" << (hasCells ? "true" : "false");
        out << ",\"worldCellsPointerAt\":" << offsetof(GfxWorld, cells);
        out << ",\"pcCellSize\":" << sizeof(GfxCell);
        out << ",\"pcAabbTreeSize\":" << sizeof(GfxAabbTree);
        out << ",\"pcPortalSize\":" << sizeof(GfxPortal);
        out << ",\"pcPortalCellPointerAt\":" << offsetof(GfxPortal, cell);
        out << ",\"pcPortalVerticesPointerAt\":" << offsetof(GfxPortal, vertices);
        out << ",\"pcCellAabbTreePointerAt\":" << offsetof(GfxCell, aabbTree);
        out << ",\"pcCellPortalsPointerAt\":" << offsetof(GfxCell, portals);
        out << ",\"pcCellCullGroupsPointerAt\":" << offsetof(GfxCell, cullGroups);
        out << ",\"pcCellReflectionProbesPointerAt\":" << offsetof(GfxCell, reflectionProbes);
        out << ",\"cells\":[";
        if (hasCells)
        {
            for(int cidx=0;cidx<11;cidx++)
            {
                if(cidx) out << ',';
                const auto& c=world.cells[cidx];
                const unsigned probeCount =
                    static_cast<unsigned>(static_cast<unsigned char>(c.reflectionProbeCount));
                const bool bounded =
                    c.aabbTreeCount >= 0 && c.aabbTreeCount <= 32768 &&
                    c.portalCount >= 0 && c.portalCount <= 8192 &&
                    c.cullGroupCount >= 0 && c.cullGroupCount <= 32768 &&
                    (!c.aabbTreeCount || c.aabbTree) &&
                    (!c.portalCount || c.portals) &&
                    (!c.cullGroupCount || c.cullGroups) &&
                    (!probeCount || c.reflectionProbes);
                if(!bounded) cellTopologyConsistent=false;
                out << "{\"ordinal\":" << cidx;
                out << ",\"aabbTreeCount\":" << c.aabbTreeCount;
                out << ",\"aabbTreePresent\":" << (c.aabbTree?"true":"false");
                out << ",\"portalCount\":" << c.portalCount;
                out << ",\"portalsPresent\":" << (c.portals?"true":"false");
                out << ",\"cullGroupCount\":" << c.cullGroupCount;
                out << ",\"cullGroupsPresent\":" << (c.cullGroups?"true":"false");
                out << ",\"reflectionProbeCount\":" << probeCount;
                out << ",\"reflectionProbesPresent\":"
                    << (c.reflectionProbes?"true":"false");
                out << ",\"pointerAndCountShapeValid\":"
                    << (bounded?"true":"false");
                out << ",\"portals\":[";
                if(bounded)
                {
                    totalAabb+=static_cast<unsigned>(c.aabbTreeCount);
                    totalPortals+=static_cast<unsigned>(c.portalCount);
                    totalCullRefs+=static_cast<unsigned>(c.cullGroupCount);
                    for(int pidx=0;pidx<c.portalCount;pidx++)
                    {
                        if(pidx)out<<',';
                        const auto& portal=c.portals[pidx];
                        int targetIndex=-1;
                        for(int k=0;k<11;k++)
                        {
                            if(portal.cell==&world.cells[k])
                            {
                                targetIndex=k;
                                break;
                            }
                        }
                        if(targetIndex<0)unresolvedPortalTargets++;
                        const auto vertexCount=static_cast<unsigned>(
                            static_cast<unsigned char>(portal.vertexCount));
                        if((vertexCount && !portal.vertices) || vertexCount>128)
                            cellTopologyConsistent=false;
                        out << "{\"ordinal\":" << pidx;
                        out << ",\"targetCellIndex\":" << targetIndex;
                        out << ",\"vertexCount\":" << vertexCount;
                        out << ",\"verticesPresent\":"
                            << (portal.vertices?"true":"false");
                        out << ",\"sourcePortalPointerBytesExported\":false}";
                    }
                }
                out << "]}";
            }
        }
        out << ']';
        out << ",\"totalAabbTrees\":" << totalAabb;
        out << ",\"totalPortals\":" << totalPortals;
        out << ",\"totalCellCullGroupReferences\":" << totalCullRefs;
        out << ",\"unresolvedCrossCellPortalTargets\":" << unresolvedPortalTargets;
        out << ",\"cellTopologyConsistent\":"
            << (cellTopologyConsistent?"true":"false");
        out << ",\"sourcePointerBytesExported\":false";
        out << ",\"xboxCellBodiesEmitted\":false";
        out << ",\"xboxHardwareReady\":false}";

        // Gate11B: emit FULL typed PC-owned 11-cell + 740 AABB tree source
        // graph as JSON numbers, NEVER process pointers or Xbox donor bytes.
        // A separate file allows the next converter to endian-convert this
        // verified data without copying T4 PC pointer-bearing structs.
        const auto sourceGraphPath=root+"/gate11_cells_aabb_typed_source.json";
        auto sourceGraphFile=context.OpenAssetFile(sourceGraphPath);
        bool graphExportOk=hasCells && cellTopologyConsistent &&
            totalAabb==740 && totalPortals==0 && totalCullRefs==0 &&
            unresolvedPortalTargets==0 && static_cast<bool>(sourceGraphFile);
        unsigned exportedTrees=0,exportedIndexes=0,exportedProbeIndexes=0;
        if(sourceGraphFile)
        {
            auto& g=*sourceGraphFile;
            g << std::setprecision(std::numeric_limits<float>::max_digits10);
            g << "{\"schema\":\"WaW360.T4PcGate11TypedGfxCellAabbSource.v1\"";
            g << ",\"originalPcWorld\":";
            JsonString(g,worldName);
            g << ",\"cellCount\":11,\"aabbTreeCountExpected\":740";
            g << ",\"originalPcSourcePointerBytesExported\":false";
            g << ",\"xboxReferencePayloadUsed\":false";
            g << ",\"cells\":[";
            auto vec3=[&](const float* v)
            {
                g << '[';
                for(int k=0;k<3;k++)
                {
                    if(k)g<<',';
                    if(!std::isfinite(v[k])){graphExportOk=false;g<<'0';}
                    else g<<v[k];
                }
                g<<']';
            };
            for(int cidx=0;cidx<11 && hasCells;cidx++)
            {
                const auto& c=world.cells[cidx];
                if(cidx)g<<',';
                const unsigned probeCount=static_cast<unsigned>(
                    static_cast<unsigned char>(c.reflectionProbeCount));
                const bool bounded=c.aabbTreeCount>=0 &&
                    c.aabbTreeCount<=32768 &&
                    (c.aabbTreeCount==0 || c.aabbTree) &&
                    c.portalCount==0 && c.cullGroupCount==0 &&
                    (probeCount==0 || c.reflectionProbes);
                if(!bounded)graphExportOk=false;
                g<<"{\"ordinal\":"<<cidx<<",\"mins\":";
                vec3(c.mins);
                g<<",\"maxs\":";vec3(c.maxs);
                g<<",\"aabbTreeCount\":"<<c.aabbTreeCount;
                g<<",\"portalCount\":"<<c.portalCount;
                g<<",\"cullGroupCount\":"<<c.cullGroupCount;
                g<<",\"reflectionProbeCount\":"<<probeCount;
                g<<",\"reflectionProbeIndices\":[";
                if(bounded)
                {
                    for(unsigned j=0;j<probeCount;j++)
                    {
                        if(j)g<<',';
                        const unsigned id=static_cast<unsigned>(
                            static_cast<unsigned char>(c.reflectionProbes[j]));
                        if(id>=world.reflectionProbeCount)
                            graphExportOk=false;
                        g<<id;
                        exportedProbeIndexes++;
                    }
                }
                g<<"],\"aabbTrees\":[";
                if(bounded)
                {
                    for(int i=0;i<c.aabbTreeCount;i++)
                    {
                        if(i)g<<',';
                        const auto& tree=c.aabbTree[i];
                        const unsigned count=tree.smodelIndexCount;
                        if(count>world.dpvs.smodelCount ||
                           (count && !tree.smodelIndexes) ||
                           tree.childCount>32768 ||
                           tree.startSurfIndex>world.surfaceCount ||
                           tree.surfaceCount>world.surfaceCount)
                            graphExportOk=false;
                        const bool safeIndexCount=count<=4096 &&
                            (count==0 || tree.smodelIndexes!=nullptr);
                        g<<"{\"ordinal\":"<<i<<",\"mins\":";
                        vec3(tree.mins);
                        g<<",\"maxs\":";vec3(tree.maxs);
                        g<<",\"childCount\":"<<tree.childCount;
                        g<<",\"surfaceCount\":"<<tree.surfaceCount;
                        g<<",\"startSurfIndex\":"<<tree.startSurfIndex;
                        g<<",\"smodelIndexCount\":"<<count;
                        g<<",\"childrenOffset\":"<<tree.childrenOffset;
                        g<<",\"smodelIndexes\":[";
                        if(safeIndexCount)
                        {
                            for(unsigned k=0;k<count;k++)
                            {
                                if(k)g<<',';
                                const unsigned index=tree.smodelIndexes[k];
                                if(index>=world.dpvs.smodelCount)
                                    graphExportOk=false;
                                g<<index;
                                exportedIndexes++;
                            }
                        }
                        else graphExportOk=false;
                        g<<"]}";
                        exportedTrees++;
                    }
                }
                g<<"]}";
            }
            g<<"]";
            g<<",\"actualExportedTreeCount\":"<<exportedTrees;
            g<<",\"actualExportedSmodelIndexes\":"<<exportedIndexes;
            g<<",\"actualExportedCellReflectionIndexes\":"<<exportedProbeIndexes;
            g<<",\"completeAndValidated\":"
             << ((graphExportOk && exportedTrees==740)?"true":"false");
            g<<",\"xboxGfxCellsPhysicallyEmitted\":false";
            g<<",\"xboxHardwareReady\":false}";
            graphExportOk=graphExportOk && g.good() &&
                exportedTrees==740;
        }
        out << ",\"gate11CellSourceGraph\":{";
        out << "\"schema\":\"WaW360.T4PcGate11TypedGfxCellAabbSourcePointerFree.v1\"";
        out << ",\"path\":";JsonString(out,sourceGraphPath);
        out << ",\"exportedTrees\":"<<exportedTrees;
        out << ",\"exportedModelIndexes\":"<<exportedIndexes;
        out << ",\"exportedCellReflectionIndexes\":"<<exportedProbeIndexes;
        out << ",\"typedGraphExportValid\":"
            << (graphExportOk?"true":"false");
        out << ",\"nativeFfChildrenEmitted\":false}";

        

        // GATE11-REMAINDER: export ALL remaining GfxWorld child-family
        // provenance together. Typed native PC OAT structures are read
        // once; image/material asset pointers become NAMES, leaf arrays are
        // original-PC LITTLE-ENDIAN binary blobs that need independent
        // Xenon conversion. Never serialize x86 process pointer values.
        // Runtime-only blocks are counted but NEVER exported as donor bytes.
        const auto remainderPath=root+"/gate11_remaining_world_source.json";
        auto remDest=context.OpenAssetFile(remainderPath);
        bool remOk=static_cast<bool>(remDest);
        const auto validArray=[&](auto count, auto limit, const auto* p)
        {
            return static_cast<uint64_t>(count)<=
                static_cast<uint64_t>(limit) && (count==0 || p);
        };
        const bool lightGridRowsValid=world.lightGrid.rowAxis<3 &&
            world.lightGrid.maxs[world.lightGrid.rowAxis]>=
                world.lightGrid.mins[world.lightGrid.rowAxis];
        const unsigned gridRows=lightGridRowsValid
            ? (static_cast<unsigned>(world.lightGrid.maxs[
                world.lightGrid.rowAxis])-
               static_cast<unsigned>(world.lightGrid.mins[
                world.lightGrid.rowAxis])+1u) : 0u;
        const auto& grid=world.lightGrid;
        const bool outerCountsValid=
            validArray(world.skySurfCount,100000,world.skyStartSurfs) &&
            validArray(world.reflectionProbeCount,4096,world.reflectionProbes) &&
            validArray(world.coronaCount,4096,world.coronas) &&
            validArray(world.lightmapCount,4096,world.lightmaps) &&
            validArray(world.modelCount,4096,world.models) &&
            validArray(world.cullGroupCount,8192,world.dpvs.cullGroups) &&
            validArray(world.worldLodChainCount,100000,world.worldLodChains) &&
            validArray(world.worldLodInfoCount,100000,world.worldLodInfos) &&
            validArray(world.worldLodSurfaceCount,1000000,world.worldLodSurfaces) &&
            validArray(world.primaryLightCount,4096,world.shadowGeom) &&
            validArray(world.primaryLightCount,4096,world.lightRegion) &&
            lightGridRowsValid &&
            validArray(gridRows,65536,grid.rowDataStart) &&
            validArray(grid.rawRowDataSize,128u*1024u*1024u,grid.rawRowData) &&
            validArray(grid.entryCount,2000000u,grid.entries) &&
            validArray(grid.colorCount,500000u,grid.colors);
        remOk=remOk && outerCountsValid;
        const auto emitVec=[&](std::ostream& stream, const float* v, unsigned n)
        {
            stream<<'[';
            for(unsigned k=0;k<n;k++)
            {
                if(k)stream<<',';
                if(!std::isfinite(v[k])){remOk=false;stream<<'0';}
                else stream<<std::setprecision(std::numeric_limits<float>::max_digits10)<<v[k];
            }
            stream<<']';
        };
        auto blobWritten=[&](std::ostream& o,
            const char* field, const auto* p, unsigned count)
        {
            const auto path=root+"/gate11_rem_"+std::string(field)+"_pc_le.bin";
            // The WRITE is forbidden unless bounded pointer-aware source
            // census first passed; otherwise preserve a manifest failure.
            const bool good=outerCountsValid &&
                WriteBlob(context,path,p,static_cast<size_t>(count));
            if(!good)remOk=false;
            o<<'"'<<field<<"\":{";
            o<<"\"sourceLittleEndian\":true,\"containsProcessPointers\":false";
            o<<",\"path\":";JsonString(o,path);
            o<<",\"count\":"<<count;
            o<<",\"elementBytes\":"<<sizeof(*p);
            o<<",\"exported\":"<<(good?"true":"false")<<'}';
        };
        if(remDest)
        {
            auto& q=*remDest;
            q<<"{\"schema\":\"WaW360.T4PcGate11WorldRemainderCensus.v1\"";
            q<<",\"originalPcWorld\":";JsonString(q,worldName);
            q<<",\"sourceOnly\":true,\"hardwareReady\":false";
            q<<",\"originalPcProcessPointerValuesExported\":false";
            q<<",\"nativeXboxPayloadBytesUsed\":false";
            q<<",\"validCountAndPointerShape\":"
             <<(outerCountsValid?"true":"false");
            q<<",\"counts\":{";
            q<<"\"skySurfaces\":"<<world.skySurfCount;
            q<<",\"reflectionProbes\":"<<world.reflectionProbeCount;
            q<<",\"coronas\":"<<world.coronaCount;
            q<<",\"lightmaps\":"<<world.lightmapCount;
            q<<",\"brushModels\":"<<world.modelCount;
            q<<",\"cullGroups\":"<<world.cullGroupCount;
            q<<",\"primaryLights\":"<<world.primaryLightCount;
            q<<",\"worldLodChains\":"<<world.worldLodChainCount;
            q<<",\"worldLodInfos\":"<<world.worldLodInfoCount;
            q<<",\"worldLodSurfaces\":"<<world.worldLodSurfaceCount;
            q<<",\"lightGridRows\":"<<gridRows;
            q<<",\"lightGridRawBytes\":"<<grid.rawRowDataSize;
            q<<",\"lightGridEntries\":"<<grid.entryCount;
            q<<",\"lightGridColors\":"<<grid.colorCount;
            q<<",\"waterBuffer0Bytes\":"<<world.waterBuffers[0].bufferSize;
            q<<",\"waterBuffer1Bytes\":"<<world.waterBuffers[1].bufferSize;
            q<<",\"dynamicWordCount0\":"<<world.dpvsDyn.dynEntClientWordCount[0];
            q<<",\"dynamicWordCount1\":"<<world.dpvsDyn.dynEntClientWordCount[1];
            q<<",\"dynamicClientCount0\":"<<world.dpvsDyn.dynEntClientCount[0];
            q<<",\"dynamicClientCount1\":"<<world.dpvsDyn.dynEntClientCount[1];
            q<<"}";
            q<<",\"sourceImageAndMaterialReferences\":{";
            q<<"\"skyImage\":";JsonNullable(q,world.skyImage?world.skyImage->name:nullptr);
            q<<",\"outdoorImage\":";JsonNullable(q,world.outdoorImage?world.outdoorImage->name:nullptr);
            q<<",\"skyBoxModel\":";JsonNullable(q,world.skyBoxModel);
            q<<",\"sunLightPresent\":"<<(world.sunLight?"true":"false");
            q<<",\"sunflareSpriteMaterialPresent\":"<<(world.sun.spriteMaterial?"true":"false");
            q<<",\"sunflareFlareMaterialPresent\":"<<(world.sun.flareMaterial?"true":"false");
            q<<",\"waterMaterialPresent\":"<<(world.waterMaterial?"true":"false");
            q<<"}";
            // Runtime allocation fields and Xbox-only world streaming
            // structures must NEVER be mistaken for PC-source file payloads.
            q<<",\"runtimeOnlyPointerPresence\":{";
            q<<"\"cellCasterBits\":"<<(world.cellCasterBits?"true":"false");
            q<<",\"sceneDynModel\":"<<(world.sceneDynModel?"true":"false");
            q<<",\"sceneDynBrush\":"<<(world.sceneDynBrush?"true":"false");
            q<<",\"primaryLightEntityShadowVis\":"
             <<(world.primaryLightEntityShadowVis?"true":"false");
            q<<",\"nonSunPrimaryLightForModelDynEnt\":"
             <<(world.nonSunPrimaryLightForModelDynEnt?"true":"false");
            q<<",\"smodelVis0\":"<<(world.dpvs.smodelVisData[0]?"true":"false");
            q<<",\"surfaceVis0\":"<<(world.dpvs.surfaceVisData[0]?"true":"false");
            q<<",\"lodData\":"<<(world.dpvs.lodData?"true":"false");
            q<<",\"dpvsDynCellBits0\":"<<(world.dpvsDyn.dynEntCellBits[0]?"true":"false");
            q<<"}";
            q<<",\"requiresXenonStreamingMetadataReconstruction\":true";
            q<<",\"requiresImageAssetRelocation\":true";
            q<<",\"requiresMaterialStateBitsRuntimeSemanticProof\":true";
            // All these raw source-side primitive/leaf arrays are safe to
            // export as PC LITTLE-ENDIAN payload; the converter must do an
            // independent type-aware Xenon conversion, NEVER a byte graft.
            q<<",\"littleEndianSourceLeafBlobs\":{";
            blobWritten(q,"skyStartSurfs",world.skyStartSurfs,
                outerCountsValid?static_cast<unsigned>(world.skySurfCount):0);
            q<<',';
            blobWritten(q,"coronas",world.coronas,
                outerCountsValid?world.coronaCount:0);
            q<<',';
            blobWritten(q,"brushModels",world.models,
                outerCountsValid?static_cast<unsigned>(world.modelCount):0);
            q<<',';
            blobWritten(q,"cullGroups",world.dpvs.cullGroups,
                outerCountsValid?static_cast<unsigned>(world.cullGroupCount):0);
            q<<',';
            blobWritten(q,"worldLodChains",world.worldLodChains,
                outerCountsValid?world.worldLodChainCount:0);
            q<<',';
            blobWritten(q,"worldLodInfos",world.worldLodInfos,
                outerCountsValid?world.worldLodInfoCount:0);
            q<<',';
            blobWritten(q,"worldLodSurfaces",world.worldLodSurfaces,
                outerCountsValid?world.worldLodSurfaceCount:0);
            q<<',';
            blobWritten(q,"gridRowDataStart",grid.rowDataStart,
                outerCountsValid?gridRows:0);
            q<<',';
            blobWritten(q,"gridRawRowData",grid.rawRowData,
                outerCountsValid?grid.rawRowDataSize:0);
            q<<',';
            blobWritten(q,"gridEntries",grid.entries,
                outerCountsValid?grid.entryCount:0);
            q<<',';
            blobWritten(q,"gridColors",grid.colors,
                outerCountsValid?grid.colorCount:0);
            for(unsigned n=0;n<2;n++)
            {
                const auto& w=world.waterBuffers[n];
                bool waterOk=w.bufferSize<=(64u*1024u*1024u) &&
                    (w.bufferSize%16u)==0 &&
                    (w.bufferSize==0 || w.buffer);
                if(!waterOk)remOk=false;
                q<<',';
                blobWritten(q,n==0?"waterBuffer0":"waterBuffer1",
                    w.buffer,(outerCountsValid && waterOk)?
                    static_cast<unsigned>(w.bufferSize/16u):0);
            }
            q<<"}";
            q<<",\"reflectionProbeRefs\":[";
            if(outerCountsValid)
                for(unsigned n=0;n<world.reflectionProbeCount;n++)
                {
                    const auto& probe=world.reflectionProbes[n];
                    if(n)q<<',';
                    q<<"{\"index\":"<<n<<",\"origin\":";
                    emitVec(q,probe.origin,3);
                    q<<",\"image\":";
                    JsonNullable(q,probe.reflectionImage?probe.reflectionImage->name:nullptr);
                    q<<'}';
                }
            q<<"]";
            q<<",\"lightmapRefs\":[";
            if(outerCountsValid)
                for(unsigned n=0;n<world.lightmapCount;n++)
                {
                    const auto& lm=world.lightmaps[n];
                    if(n)q<<',';
                    q<<"{\"index\":"<<n<<",\"primary\":";
                    JsonNullable(q,lm.primary?lm.primary->name:nullptr);
                    q<<",\"secondary\":";
                    JsonNullable(q,lm.secondary?lm.secondary->name:nullptr);
                    q<<'}';
                }
            q<<"]";
            // GfxShadowGeometry has nested POINTERS; do not blob/copy it.
            q<<",\"shadowGeometry\":[";
            if(outerCountsValid)
                for(unsigned n=0;n<world.primaryLightCount;n++)
                {
                    const auto& shadow=world.shadowGeom[n];
                    const bool ok=validArray(shadow.surfaceCount,735,shadow.sortedSurfIndex) &&
                        validArray(shadow.smodelCount,1903,shadow.smodelIndex);
                    if(!ok)remOk=false;
                    if(n)q<<',';
                    q<<"{\"index\":"<<n<<",\"surfaceCount\":"<<shadow.surfaceCount;
                    q<<",\"smodelCount\":"<<shadow.smodelCount;
                    q<<",\"sortedSurfIndex\":[";
                    if(ok)
                        for(unsigned k=0;k<shadow.surfaceCount;k++)
                        {
                            if(k)q<<',';
                            const unsigned id=shadow.sortedSurfIndex[k];
                            if(id>=world.surfaceCount)remOk=false;
                            q<<id;
                        }
                    q<<"],\"smodelIndex\":[";
                    if(ok)
                        for(unsigned k=0;k<shadow.smodelCount;k++)
                        {
                            if(k)q<<',';
                            const unsigned id=shadow.smodelIndex[k];
                            if(id>=world.dpvs.smodelCount)remOk=false;
                            q<<id;
                        }
                    q<<"]}";
                }
            q<<"]";
            // Nested hull/axis traversal normalized to numbers, no OAT
            // process-address bytes. Bound every count before dereference.
            q<<",\"lightRegions\":[";
            if(outerCountsValid)
                for(unsigned n=0;n<world.primaryLightCount;n++)
                {
                    const auto& region=world.lightRegion[n];
                    const bool hullOk=validArray(region.hullCount,4096,region.hulls);
                    if(!hullOk)remOk=false;
                    if(n)q<<',';
                    q<<"{\"index\":"<<n<<",\"hullCount\":"<<region.hullCount;
                    q<<",\"hulls\":[";
                    if(hullOk)
                        for(unsigned k=0;k<region.hullCount;k++)
                        {
                            const auto& hull=region.hulls[k];
                            const bool axisOk=validArray(hull.axisCount,128,hull.axis);
                            if(!axisOk)remOk=false;
                            if(k)q<<',';
                            q<<"{\"index\":"<<k<<",\"kdopMidPoint\":";
                            emitVec(q,hull.kdopMidPoint,9);
                            q<<",\"kdopHalfSize\":";
                            emitVec(q,hull.kdopHalfSize,9);
                            q<<",\"axisCount\":"<<hull.axisCount;
                            q<<",\"axis\":[";
                            if(axisOk)
                                for(unsigned h=0;h<hull.axisCount;h++)
                                {
                                    if(h)q<<',';
                                    q<<"{\"dir\":";
                                    emitVec(q,hull.axis[h].dir,3);
                                    q<<",\"midPoint\":"<<hull.axis[h].midPoint;
                                    q<<",\"halfSize\":"<<hull.axis[h].halfSize<<"}";
                                }
                            q<<"]}";
                        }
                    q<<"]}";
                }
            q<<"]";
            q<<",\"sourceOnlyComplete\":"<<(remOk?"true":"false");
            q<<",\"xboxSerializedWorldChildren\":false";
            q<<",\"hardwareReady\":false}";
            remOk=remOk && q.good();
        }
        out<<",\"gate11RemainingWorldCorpus\":{";
        out<<"\"schema\":\"WaW360.T4PcGate11RemainingWorldSourceCorpus.v1\"";
        out<<",\"path\":";JsonString(out,remainderPath);
        out<<",\"pointerFreeSourceGraphWritten\":"<<(remOk?"true":"false");
        out<<",\"singleFutureNativeFfBatchRequested\":true";
        out<<",\"nativeFfBuildPerformed\":false}";

                // Gate8C: export the actual original-PC 1903 static draw placement
        // records AS TYPED FIELDS. Native PC XModel* process addresses MUST
        // NEVER be serialized: each source model is identified by name.
        // PC GfxPackedPlacement has a 3x3 FLOAT axis; Xenon uses three
        // PackedUnitVec words instead. These are SOURCE-ONLY records,
        // not yet a putative Xenon ABI or a safe 1903-draw graft.
        const bool staticDrawAbi =
            sizeof(GfxPackedPlacement) == 52 &&
            sizeof(GfxStaticModelDrawInst) == 92 &&
            offsetof(GfxStaticModelDrawInst, placement) == 4 &&
            offsetof(GfxStaticModelDrawInst, model) == 56;
        const auto drawPath=root+"/gate8_static_draws_pc_typed.json";
        bool drawOk=gate8Shape && staticDrawAbi;
        unsigned int drawSourceRows=0;
        if(drawOk)
        {
            auto drawDest=context.OpenAssetFile(drawPath);
            drawOk=static_cast<bool>(drawDest);
            if(drawDest)
            {
                auto& dOut=*drawDest;
                dOut << std::setprecision(std::numeric_limits<float>::max_digits10);
                dOut << "{\"format\":\"WaW360.T4PcGate8StaticDrawSource.v1\"";
                dOut << ",\"sourceType\":\"PC_T4_X86_LOADED_TYPED_FIELDS\"";
                dOut << ",\"pcDrawSize\":" << sizeof(GfxStaticModelDrawInst);
                dOut << ",\"pcPlacementSize\":" << sizeof(GfxPackedPlacement);
                dOut << ",\"modelPointerOffset\":" << offsetof(GfxStaticModelDrawInst, model);
                dOut << ",\"count\":" << world.dpvs.smodelCount;
                dOut << ",\"records\":[";
                for(unsigned int i=0;i<world.dpvs.smodelCount;i++)
                {
                    const auto& d=world.dpvs.smodelDrawInsts[i];
                    const auto& p=d.placement;
                    if(!d.model || !d.model->name || !std::isfinite(d.cullDist) ||
                       !std::isfinite(p.scale))
                    {
                        drawOk=false;break;
                    }
                    bool finite=true;
                    for(int a=0;a<3;a++)
                    {
                        finite=finite && std::isfinite(p.origin[a]);
                        for(int b=0;b<3;b++)
                            finite=finite && std::isfinite(p.axis[a][b]);
                    }
                    if(!finite){drawOk=false;break;}
                    if(i)dOut<<',';
                    dOut << "{\"ordinal\":" << i << ",\"model\":";
                    JsonString(dOut,d.model->name);
                    dOut << ",\"cullDist\":" << d.cullDist;
                    dOut << ",\"origin\":[";
                    for(int a=0;a<3;a++){if(a)dOut<<',';dOut<<p.origin[a];}
                    dOut << "],\"axis\":[";
                    for(int a=0;a<3;a++)
                    {
                        if(a)dOut<<',';
                        dOut<<'[';
                        for(int b=0;b<3;b++){if(b)dOut<<',';dOut<<p.axis[a][b];}
                        dOut<<']';
                    }
                    dOut << "],\"scale\":" << p.scale;
                    dOut << ",\"lodFade\":" << d.lodFade.value;
                    dOut << ",\"cachedLightSettingIndex\":" << d.cachedLightSettingIndex;
                    dOut << ",\"field44\":" << static_cast<unsigned>(static_cast<unsigned char>(d.field_44));
                    dOut << ",\"field45\":" << static_cast<unsigned>(static_cast<unsigned char>(d.field_45));
                    dOut << ",\"reflectionProbeIndex\":" << static_cast<unsigned>(d.reflectionProbeIndex);
                    dOut << ",\"primaryLightIndex\":" << static_cast<unsigned>(d.primaryLightIndex);
                    dOut << ",\"flags\":" << d.flags;
                    dOut << ",\"smodelCacheIndex\":[";
                    for(int a=0;a<4;a++){if(a)dOut<<',';dOut<<d.smodelCacheIndex[a];}
                    dOut << "]}";
                    ++drawSourceRows;
                }
                dOut << "]}";
                drawOk=drawOk && dOut.good() && drawSourceRows==1903;
            }
        }
        out << ",\"gate8StaticDrawSource\":{";
        out << "\"status\":";
        JsonString(out,drawOk
            ? "T4_PC_GATE8_1903_TYPED_DRAWS_SOURCE_EXPORTED"
            : "T4_PC_GATE8_STATIC_DRAW_ABI_OR_SOURCE_BLOCKED");
        out << ",\"count\":" << drawSourceRows;
        out << ",\"pcDrawSize\":" << sizeof(GfxStaticModelDrawInst);
        out << ",\"pcPlacementSize\":" << sizeof(GfxPackedPlacement);
        out << ",\"modelPointerOffset\":" << offsetof(GfxStaticModelDrawInst, model);
        out << ",\"pointerBytesExported\":false";
        out << ",\"xboxDrawRecordsWritten\":false";
        out << ",\"recordsPath\":";
        JsonString(out,drawPath);
        out << '}';

        int exported=0,missing=0,totalTextures=0,totalConstants=0,
            totalState=0,unresolvedWater=0,errors=0;
        out << ",\"materials\":[";
        for(int i=0;i<39;i++)
        {
            if(i) out << ',';
            const auto* m=world.materialMemory[i].material;
            out << "{\"ordinal\":" << i << ",\"name\":";
            JsonNullable(out,m?m->info.name:nullptr);
            if(!m || !m->info.name || !abiPass)
            {
                out << ",\"sourceHeaderLoaded\":false"
                       ",\"completeSourceTablesExported\":false"
                       ",\"blocker\":\"MATERIAL_NULL_OR_PC_ABI_MISMATCH\"}";
                ++missing;
                continue;
            }
            const bool smallCounts =
                m->textureCount<=64 && m->constantCount<=64 &&
                m->stateBitsCount<=64;
            const bool tableShape=smallCounts &&
                (!m->textureCount||m->textureTable) &&
                (!m->constantCount||m->constantTable) &&
                (!m->stateBitsCount||m->stateBitsTable);
            if(!tableShape)
            {
                out << ",\"sourceHeaderLoaded\":true"
                       ",\"completeSourceTablesExported\":false"
                       ",\"blocker\":\"MATERIAL_TABLE_POINTER_OR_COUNT_INVALID\"}";
                ++errors;
                continue;
            }

            const auto p=std::format("{}/material_{:02}",root,i);
            const auto headerFile=p+"_header_pc_le.bin";
            const auto texFile=p+"_textures_pc_le.bin";
            const auto constFile=p+"_constants_pc_le.bin";
            const auto statesFile=p+"_statebits_pc_le.bin";
            auto header=*m;
            header.info.name=Following<const char>();
            header.techniqueSet=nullptr; // Original name recorded in JSON.
            header.textureTable=m->textureTable?Following<MaterialTextureDef>():nullptr;
            header.constantTable=m->constantTable?Following<MaterialConstantDef>():nullptr;
            header.stateBitsTable=m->stateBitsTable?Following<GfxStateBits>():nullptr;
            bool files=WriteBlob(context,headerFile,&header,1);

            std::vector<MaterialTextureDef> textures;
            textures.reserve(m->textureCount);
            int waterCount=0;
            for(unsigned k=0;k<m->textureCount;k++)
            {
                auto t=m->textureTable[k];
                // Raw pointers from x86 OAT process MUST NOT leak to disk:
                // images become null; water requires separate conversion.
                if(t.semantic==TS_WATER_MAP)
                {
                    ++waterCount;
                    t.u.water=t.u.water?Following<water_t>():nullptr;
                }
                else t.u.image=nullptr;
                textures.push_back(t);
            }
            files=WriteBlob(context,texFile,textures.data(),textures.size())&&files;
            files=WriteBlob(context,constFile,m->constantTable,m->constantCount)&&files;
            files=WriteBlob(context,statesFile,m->stateBitsTable,m->stateBitsCount)&&files;
            totalTextures+=m->textureCount;
            totalConstants+=m->constantCount;
            totalState+=m->stateBitsCount;
            unresolvedWater+=waterCount;
            if(files)++exported;
            else ++errors;

            out << ",\"sourceHeaderLoaded\":true";
            out << ",\"completeSourceTablesExported\":" << (files?"true":"false");
            out << ",\"counts\":{\"textures\":" << static_cast<unsigned>(m->textureCount)
                << ",\"constants\":" << static_cast<unsigned>(m->constantCount)
                << ",\"stateBits\":" << static_cast<unsigned>(m->stateBitsCount)<<'}';
            out << ",\"techniqueSet\":";
            JsonNullable(out,m->techniqueSet?m->techniqueSet->name:nullptr);
            out << ",\"headerFile\":";
            JsonString(out,headerFile);
            out << ",\"textureTableFile\":";
            JsonString(out,texFile);
            out << ",\"constantTableFile\":";
            JsonString(out,constFile);
            out << ",\"stateBitsTableFile\":";
            JsonString(out,statesFile);
            out << ",\"unserializedWaterTextures\":" << waterCount;
            out << ",\"textures\":[";
            for(unsigned k=0;k<m->textureCount;k++)
            {
                if(k)out<<',';
                const auto& t=m->textureTable[k];
                out << "{\"index\":" << k
                    << ",\"nameHash\":" << t.nameHash
                    << ",\"semantic\":" << static_cast<unsigned>(t.semantic)
                    << ",\"image\":";
                if(t.semantic==TS_WATER_MAP)
                    JsonNullable(out,t.u.water&&t.u.water->image?t.u.water->image->name:nullptr);
                else
                    JsonNullable(out,t.u.image?t.u.image->name:nullptr);
                out << ",\"requiresWaterConversion\":"
                    << (t.semantic==TS_WATER_MAP?"true":"false")
                    << '}';
            }
            out << "]}";
        }
        out << ']';
        out << ",\"exportedMaterialHeaders\":" << exported;
        out << ",\"missingMaterials\":" << missing;
        out << ",\"failedMaterialTables\":" << errors;
        out << ",\"totalTextureDescriptors\":" << totalTextures;
        out << ",\"totalConstantRecords\":" << totalConstants;
        out << ",\"totalStateBitsRecords\":" << totalState;
        out << ",\"waterTexturesNeedingSeparateConversion\":" << unresolvedWater;
        out << ",\"xenonMaterialGraphBodiesEmitted\":0";
        out << ",\"xboxGfxWorldRootEmitted\":false,\"xboxHardwareReady\":false";
        out << ",\"status\":\"PC_39_WORLD_MATERIAL_SOURCE_CORPUS_EXPORTED_NOT_XBOX_GRAPHS\"";
        out << ",\"note\":\"These are little-endian ORIGINAL-PC material headers,"
               " texture/constant/state-bit tables taken from OAT-resolved Material*"
               " ownership. In-memory pointers are normalized; technique/image"
               " dependency identities are listed. This is direct input for"
               " future PC-to-Xenon Material graph conversion, NOT 39 valid"
               " Xbox Material bodies. Water nested data and Xbox technique"
               " binding require separate work. NO donor payloads/FF.\"}";
    }
}

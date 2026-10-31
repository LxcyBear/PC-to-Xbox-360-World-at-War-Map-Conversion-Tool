#include "GfxWorldSurfaceAbiWitnessT4Xenon.h"

#include <cmath>
#include <cstddef>
#include <cstdint>
#include <format>
#include <iomanip>
#include <ostream>
#include <sstream>
#include <string>
#include <string_view>
#include <vector>

using namespace T4Xenon;

namespace
{
    std::string SafeStem(const std::string_view value)
    {
        std::ostringstream result;
        for (const unsigned char ch : value)
        {
            if ((ch >= 'a' && ch <= 'z') ||
                (ch >= 'A' && ch <= 'Z') ||
                (ch >= '0' && ch <= '9') ||
                ch == '_' || ch == '-' || ch == '.')
                result << static_cast<char>(ch);
            else
                result << '_';
        }
        return result.str();
    }

    void JsonString(std::ostream& out, const char* value)
    {
        if (!value)
        {
            out << "null";
            return;
        }
        out << '"';
        for (const unsigned char ch : std::string_view(value))
        {
            switch (ch)
            {
            case '"': out << "\\\""; break;
            case '\\': out << "\\\\"; break;
            case '\n': out << "\\n"; break;
            case '\r': out << "\\r"; break;
            case '\t': out << "\\t"; break;
            default:
                if (ch < 0x20)
                    out << "\\u00" << std::uppercase << std::hex << std::setw(2)
                        << std::setfill('0') << static_cast<unsigned>(ch)
                        << std::nouppercase << std::dec << std::setfill(' ');
                else
                    out << static_cast<char>(ch);
                break;
            }
        }
        out << '"';
    }

    void WriteTriple(std::ostream& out, const float* v)
    {
        // Invalid floats must not corrupt the JSON witness; downstream
        // calibration treats null as unresolved rather than guessed.
        if (!std::isfinite(v[0]) || !std::isfinite(v[1]) ||
            !std::isfinite(v[2]))
        {
            out << "null";
            return;
        }
        out << '[' << std::setprecision(9) << v[0] << ','
            << v[1] << ',' << v[2] << ']';
    }
}

namespace gfxworld
{
    void SurfaceAbiWitnessT4Xenon::DumpAsset(
        AssetDumpingContext& context,
        const XAssetInfo<GfxWorld>& asset)
    {
        static_assert(sizeof(void*) == 4,
            "This native pointer witness requires x86 OAT.");
        static_assert(sizeof(GfxWorldVertex) == 44,
            "T4Xenon world vertex size changed.");
        static_assert(sizeof(srfTriangles_t) == 40,
            "T4Xenon srfTriangles_t requires 6 extra topMip floats.");
        static_assert(sizeof(GfxSurface) == 76,
            "T4Xenon GfxSurface is 76B, not PC 52B.");
        static_assert(offsetof(GfxSurface, material) == 44,
            "T4Xenon surface material reference must be at byte +44.");
        static_assert(offsetof(GfxSurface, bounds) == 52,
            "T4Xenon surface bounds must be at +52.");
        // Gate 8F: native Xbox static-draw ABI, *measured* in pinned
        // T4Xenon compiler structs. This is an observation, not a graft.
        static_assert(sizeof(GfxPackedPlacement) == 28,
            "Xbox static model packed placement must be 28 bytes.");
        static_assert(sizeof(GfxStaticModelDrawInst) == 60,
            "Xbox static draw must be 60 bytes, not PC 92 bytes.");
        static_assert(offsetof(GfxStaticModelDrawInst, placement) == 4,
            "Xbox static draw placement offset changed.");
        static_assert(offsetof(GfxStaticModelDrawInst, model) == 32,
            "Xbox XModel pointer offset changed.");
        static_assert(offsetof(GfxStaticModelDrawInst, reflectionProbeIndex) == 36,
            "Xbox reflection probe offset changed.");
        static_assert(offsetof(GfxStaticModelDrawInst, primaryLightIndex) == 37,
            "Xbox primary light offset changed.");
        static_assert(offsetof(GfxStaticModelDrawInst, lightingHandle) == 38,
            "Xbox 16-bit lighting handle offset changed.");
        static_assert(offsetof(GfxStaticModelDrawInst, flags) == 40,
            "Xbox flags offset changed.");
        static_assert(offsetof(GfxStaticModelDrawInst, pad) == 41,
            "Xbox draw padding offset changed.");


        const auto& world = *asset.Asset();
        const auto worldName = world.name
            ? std::string_view(world.name)
            : std::string_view(asset.m_name);
        const auto file = context.OpenAssetFile(
            std::format("xenon_gfxworld_surface_abi_witness/{}.json",
                SafeStem(worldName)));
        if (!file)
            return;
        auto& out = *file;
        out << "{\"schema\":\"WaW360.T4XenonGfxWorldSurfaceAbiWitness.v1\"";
        out << ",\"worldName\":";
        JsonString(out, world.name);
        out << ",\"platform\":\"T4Xenon\"";
        out << ",\"abi\":{";
        out << "\"gfxSurfaceSize\":" << sizeof(GfxSurface);
        out << ",\"trianglesSize\":" << sizeof(srfTriangles_t);
        out << ",\"worldVertexSize\":" << sizeof(GfxWorldVertex);
        out << ",\"topMipMinAt\":" << offsetof(srfTriangles_t, topMipMins);
        out << ",\"topMipMaxAt\":" << offsetof(srfTriangles_t, topMipMaxs);
        out << ",\"materialFieldAt\":" << offsetof(GfxSurface, material);
        out << ",\"flagsAt\":" << offsetof(GfxSurface, lightmapIndex);
        out << ",\"boundsAt\":" << offsetof(GfxSurface, bounds);
        out << "}";
        // Stage 8 world-root and DPVS serialization ABI. These offsetof
        // measurements come from the PINNED ORIGINAL XBOX T4Xenon headers
        // compiled as x86, not from guessed PC 52B offsets. The accompanying
        // values are READ-ONLY, OAT-loaded native reference shape, not donor
        // body bytes to copy into a converted FastFile.
        out << ",\"gfxWorldRootAbi\":{";
        out << "\"gfxWorldSize\":" << sizeof(GfxWorld);
        out << ",\"dpvsStaticSize\":" << sizeof(GfxWorldDpvsStatic);
        out << ",\"dpvsPlanesSize\":" << sizeof(GfxWorldDpvsPlanes);
        out << ",\"vertexDataSize\":" << sizeof(GfxWorldVertexData);
        out << ",\"vertexLayerDataSize\":" << sizeof(GfxWorldVertexLayerData);
        out << ",\"materialMemorySize\":" << sizeof(MaterialMemory);
        out << ",\"nameAt\":" << offsetof(GfxWorld, name);
        out << ",\"baseNameAt\":" << offsetof(GfxWorld, baseName);
        out << ",\"indicesAt\":" << offsetof(GfxWorld, indices);
        out << ",\"indexCountAt\":" << offsetof(GfxWorld, indexCount);
        out << ",\"surfaceCountAt\":" << offsetof(GfxWorld, surfaceCount);
        out << ",\"vertexCountAt\":" << offsetof(GfxWorld, vertexCount);
        out << ",\"vertexDataAt\":" << offsetof(GfxWorld, vd);
        out << ",\"vertexLayerDataAt\":" << offsetof(GfxWorld, vld);
        out << ",\"dpvsPlanesAt\":" << offsetof(GfxWorld, dpvsPlanes);
        out << ",\"materialMemoryCountAt\":" << offsetof(GfxWorld, materialMemoryCount);
        out << ",\"materialMemoryAt\":" << offsetof(GfxWorld, materialMemory);
        out << ",\"dpvsStaticAt\":" << offsetof(GfxWorld, dpvs);
        out << ",\"dpvsDynamicAt\":" << offsetof(GfxWorld, dpvsDyn);
        out << ",\"dpvsStaticSurfaceCountAt\":" << offsetof(GfxWorldDpvsStatic, staticSurfaceCount);
        out << ",\"dpvsStaticSurfacesAt\":" << offsetof(GfxWorldDpvsStatic, surfaces);
        out << ",\"dpvsStaticSortedSurfaceIndexAt\":" << offsetof(GfxWorldDpvsStatic, sortedSurfIndex);
        out << ",\"dpvsStaticSurfaceVisDataAt\":" << offsetof(GfxWorldDpvsStatic, surfaceVisData);
        out << ",\"dpvsStaticSurfaceMaterialsAt\":" << offsetof(GfxWorldDpvsStatic, surfaceMaterials);
        out << ",\"dpvsPlanesCellCountAt\":" << offsetof(GfxWorldDpvsPlanes, cellCount);
        out << ",\"dpvsPlanesNodesAt\":" << offsetof(GfxWorldDpvsPlanes, nodes);
        out << ",\"dpvsPlanesPlanesAt\":" << offsetof(GfxWorldDpvsPlanes, planes);
        // Gate11 native STRUCT ABI only, original Xbox body is a witness,
        // NEVER an exportable conversion payload.
        out << ",\"cellsAt\":" << offsetof(GfxWorld, cells);
        out << ",\"gfxCellSize\":" << sizeof(GfxCell);
        out << ",\"gfxAabbTreeSize\":" << sizeof(GfxAabbTree);
        out << ",\"gfxPortalSize\":" << sizeof(GfxPortal);
        out << ",\"cellAabbTreeAt\":" << offsetof(GfxCell, aabbTree);
        out << ",\"cellPortalsAt\":" << offsetof(GfxCell, portals);
        out << ",\"cellCullGroupsAt\":" << offsetof(GfxCell, cullGroups);
        out << ",\"cellReflectionProbesAt\":" << offsetof(GfxCell, reflectionProbes);
        out << ",\"portalCellAt\":" << offsetof(GfxPortal, cell);
        out << ",\"portalVerticesAt\":" << offsetof(GfxPortal, vertices);

        out << ",\"cellMinsAt\":" << offsetof(GfxCell, mins);
        out << ",\"cellMaxsAt\":" << offsetof(GfxCell, maxs);
        out << ",\"cellAabbTreeCountAt\":" << offsetof(GfxCell, aabbTreeCount);
        out << ",\"cellPortalCountAt\":" << offsetof(GfxCell, portalCount);
        out << ",\"cellCullGroupCountAt\":" << offsetof(GfxCell, cullGroupCount);
        out << ",\"cellReflectionProbeCountAt\":" << offsetof(GfxCell, reflectionProbeCount);
        out << ",\"aabbMinsAt\":" << offsetof(GfxAabbTree, mins);
        out << ",\"aabbMaxsAt\":" << offsetof(GfxAabbTree, maxs);
        out << ",\"aabbChildCountAt\":" << offsetof(GfxAabbTree, childCount);
        out << ",\"aabbSurfaceCountAt\":" << offsetof(GfxAabbTree, surfaceCount);
        out << ",\"aabbStartSurfIndexAt\":" << offsetof(GfxAabbTree, startSurfIndex);
        out << ",\"aabbSmodelIndexCountAt\":" << offsetof(GfxAabbTree, smodelIndexCount);
        out << ",\"aabbSmodelIndexesAt\":" << offsetof(GfxAabbTree, smodelIndexes);
        out << ",\"aabbChildrenOffsetAt\":" << offsetof(GfxAabbTree, childrenOffset);
        out << "}";

        // Remaining GfxWorld closure: a single pinned native T4Xenon ABI
        // witness covering all subsequent original-PC, pointer-free source
        // families. This is sizeof/offsetof ONLY: never copy Xbox asset
        // bodies or runtime graphics pointers into PC-derived FastFiles.
        out << ",\"gate11RemainingNativeAbi\":{";
        out << "\"schema\":\"WaW360.T4XenonGate11RemainingWorldNativeAbi.v1\"";
        out << ",\"gfxWorldBytes\":" << sizeof(GfxWorld);
        out << ",\"worldStreamInfoAt\":" << offsetof(GfxWorld, streamInfo);
        out << ",\"streamInfoBytes\":" << sizeof(GfxWorldStreamInfo);
        out << ",\"skySurfCountAt\":" << offsetof(GfxWorld, skySurfCount);
        out << ",\"skyStartSurfsAt\":" << offsetof(GfxWorld, skyStartSurfs);
        out << ",\"skyImageAt\":" << offsetof(GfxWorld, skyImage);
        out << ",\"skySamplerAt\":" << offsetof(GfxWorld, skySamplerState);
        out << ",\"skyBoxModelAt\":" << offsetof(GfxWorld, skyBoxModel);
        out << ",\"sunLightAt\":" << offsetof(GfxWorld, sunLight);
        out << ",\"sunColorFromBspAt\":" << offsetof(GfxWorld, sunColorFromBsp);
        out << ",\"sunPrimaryLightIndexAt\":" << offsetof(GfxWorld, sunPrimaryLightIndex);
        out << ",\"primaryLightCountAt\":" << offsetof(GfxWorld, primaryLightCount);
        out << ",\"cullGroupCountAt\":" << offsetof(GfxWorld, cullGroupCount);
        out << ",\"reflectionProbeCountAt\":" << offsetof(GfxWorld, reflectionProbeCount);
        out << ",\"reflectionProbesAt\":" << offsetof(GfxWorld, reflectionProbes);
        out << ",\"reflectionProbeTexturesAt\":" << offsetof(GfxWorld, reflectionProbeTextures);
        out << ",\"coronaCountAt\":" << offsetof(GfxWorld, coronaCount);
        out << ",\"coronasAt\":" << offsetof(GfxWorld, coronas);
        out << ",\"lightmapCountAt\":" << offsetof(GfxWorld, lightmapCount);
        out << ",\"lightmapsAt\":" << offsetof(GfxWorld, lightmaps);
        out << ",\"lightGridAt\":" << offsetof(GfxWorld, lightGrid);
        out << ",\"lightmapPrimaryTexturesAt\":" << offsetof(GfxWorld, lightmapPrimaryTextures);
        out << ",\"lightmapSecondaryTexturesAt\":" << offsetof(GfxWorld, lightmapSecondaryTextures);
        out << ",\"modelCountAt\":" << offsetof(GfxWorld, modelCount);
        out << ",\"modelsAt\":" << offsetof(GfxWorld, models);
        out << ",\"sunflareAt\":" << offsetof(GfxWorld, sun);
        out << ",\"outdoorImageAt\":" << offsetof(GfxWorld, outdoorImage);
        out << ",\"shadowGeomAt\":" << offsetof(GfxWorld, shadowGeom);
        out << ",\"lightRegionAt\":" << offsetof(GfxWorld, lightRegion);
        out << ",\"dpvsDynamicAt\":" << offsetof(GfxWorld, dpvsDyn);
        out << ",\"lodChainCountAt\":" << offsetof(GfxWorld, worldLodChainCount);
        out << ",\"lodChainsAt\":" << offsetof(GfxWorld, worldLodChains);
        out << ",\"lodInfoCountAt\":" << offsetof(GfxWorld, worldLodInfoCount);
        out << ",\"lodInfosAt\":" << offsetof(GfxWorld, worldLodInfos);
        out << ",\"lodSurfaceCountAt\":" << offsetof(GfxWorld, worldLodSurfaceCount);
        out << ",\"lodSurfacesAt\":" << offsetof(GfxWorld, worldLodSurfaces);
        out << ",\"waterBuffersAt\":" << offsetof(GfxWorld, waterBuffers);
        out << ",\"waterMaterialAt\":" << offsetof(GfxWorld, waterMaterial);
        out << ",\"reflectionProbeBytes\":" << sizeof(GfxReflectionProbe);
        out << ",\"reflectionProbeOriginAt\":" << offsetof(GfxReflectionProbe, origin);
        out << ",\"reflectionProbeImageAt\":" << offsetof(GfxReflectionProbe, reflectionImage);
        out << ",\"lightmapArrayBytes\":" << sizeof(GfxLightmapArray);
        out << ",\"lightmapPrimaryAt\":" << offsetof(GfxLightmapArray, primary);
        out << ",\"lightmapSecondaryAt\":" << offsetof(GfxLightmapArray, secondary);
        out << ",\"lightGridBytes\":" << sizeof(GfxLightGrid);
        out << ",\"gridRowDataStartAt\":" << offsetof(GfxLightGrid, rowDataStart);
        out << ",\"gridRawRowDataAt\":" << offsetof(GfxLightGrid, rawRowData);
        out << ",\"gridEntriesAt\":" << offsetof(GfxLightGrid, entries);
        out << ",\"gridColorsAt\":" << offsetof(GfxLightGrid, colors);
        out << ",\"gridEntryBytes\":" << sizeof(GfxLightGridEntry);
        out << ",\"gridColorsBytes\":" << sizeof(GfxLightGridColors);
        out << ",\"coronaBytes\":" << sizeof(GfxLightCorona);
        out << ",\"brushModelBytes\":" << sizeof(GfxBrushModel);
        out << ",\"cullGroupBytes\":" << sizeof(GfxCullGroup);
        out << ",\"shadowGeometryBytes\":" << sizeof(GfxShadowGeometry);
        out << ",\"shadowSortedAt\":" << offsetof(GfxShadowGeometry, sortedSurfIndex);
        out << ",\"shadowModelsAt\":" << offsetof(GfxShadowGeometry, smodelIndex);
        out << ",\"lightRegionBytes\":" << sizeof(GfxLightRegion);
        out << ",\"lightRegionHullBytes\":" << sizeof(GfxLightRegionHull);
        out << ",\"lightRegionAxisBytes\":" << sizeof(GfxLightRegionAxis);
        out << ",\"lodChainBytes\":" << sizeof(GfxWorldLodChain);
        out << ",\"lodInfoBytes\":" << sizeof(GfxWorldLodInfo);
        out << ",\"waterBufferBytes\":" << sizeof(GfxWaterBuffer);
        out << ",\"dpvsDynamicBytes\":" << sizeof(GfxWorldDpvsDynamic);
        out << ",\"requiresRuntimeBufferReallocation\":true";
        out << ",\"containsOriginalXboxPayload\":false}";

        out << ",\"loadedWorldRootShape\":{";
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
        out << ",\"surfaceMaterialsPresent\":"
            << (world.dpvs.surfaceMaterials ? "true":"false");
        out << "}";
        // A completely separate read-only calibration of ORIGINAL Xbox
        // draw fields, not serialized Xbox draw bytes and not a fixup list.
        // Only publish rows if the original loader actually fixed the
        // expected 1903 native draw instances; never fabricate missing data.
        out << ",\"nativeStaticDrawAbi\":{";
        out << "\"stride\":" << sizeof(GfxStaticModelDrawInst);
        out << ",\"placementStride\":" << sizeof(GfxPackedPlacement);
        out << ",\"cullDistAt\":" << offsetof(GfxStaticModelDrawInst,cullDist);
        out << ",\"placementAt\":" << offsetof(GfxStaticModelDrawInst,placement);
        out << ",\"originAt\":" << (offsetof(GfxStaticModelDrawInst,placement) + offsetof(GfxPackedPlacement,origin));
        out << ",\"axisAt\":" << (offsetof(GfxStaticModelDrawInst,placement) + offsetof(GfxPackedPlacement,axis));
        out << ",\"scaleAt\":" << (offsetof(GfxStaticModelDrawInst,placement) + offsetof(GfxPackedPlacement,scale));
        out << ",\"modelAt\":" << offsetof(GfxStaticModelDrawInst,model);
        out << ",\"reflectionAt\":" << offsetof(GfxStaticModelDrawInst,reflectionProbeIndex);
        out << ",\"primaryLightAt\":" << offsetof(GfxStaticModelDrawInst,primaryLightIndex);
        out << ",\"lightingHandleAt\":" << offsetof(GfxStaticModelDrawInst,lightingHandle);
        out << ",\"flagsAt\":" << offsetof(GfxStaticModelDrawInst,flags);
        out << ",\"padAt\":" << offsetof(GfxStaticModelDrawInst,pad);
        out << "}";
        const bool drawReady = world.dpvs.smodelCount == 1903 &&
            world.dpvs.smodelDrawInsts != nullptr;
        out << ",\"nativeStaticDrawCount\":" << world.dpvs.smodelCount;
        out << ",\"nativeStaticDrawArrayAvailable\":" << (drawReady?"true":"false");
        out << ",\"nativeStaticDraws\":[";
        if (drawReady)
        {
            for (decltype(world.dpvs.smodelCount) i = 0; i < world.dpvs.smodelCount; ++i)
            {
                if(i) out << ',';
                const auto& d = world.dpvs.smodelDrawInsts[i];
                out << "{\"ordinal\":" << i;
                out << ",\"model\":";
                JsonString(out,d.model ? d.model->name : nullptr);
                out << ",\"cullDist\":" << std::setprecision(9) << d.cullDist;
                out << ",\"origin\":";
                WriteTriple(out,d.placement.origin);
                out << ",\"packedAxisWords\":["
                    << d.placement.axis[0].packed << ','
                    << d.placement.axis[1].packed << ','
                    << d.placement.axis[2].packed << ']';
                out << ",\"scale\":" << d.placement.scale;
                out << ",\"reflectionProbeIndex\":" << static_cast<unsigned>(d.reflectionProbeIndex);
                out << ",\"primaryLightIndex\":" << static_cast<unsigned>(d.primaryLightIndex);
                out << ",\"lightingHandle\":" << static_cast<unsigned>(d.lightingHandle);
                out << ",\"flags\":" << static_cast<unsigned>(d.flags);
                unsigned int padNonZero=0;
                for(const auto octet:d.pad){if(octet)++padNonZero;}
                out << ",\"paddingNonZeroBytes\":" << padNonZero;
                out << '}';
            }
        }
        out << ']';
        out << ",\"nativeStaticDrawWitnessOnly\":true";

        // Gate11 independent Xenon AFTER-LOAD witness. The output is typed
        // GfxCell/AABB data, not a byte copy of the original Xbox reference.
        // This probes both retail reference and NEW source-derived FF; no
        // producer manifest is consulted by the OAT loaded witness itself.
        const bool cellArrayAvailable=
            world.dpvsPlanes.cellCount==11 && world.cells!=nullptr;
        bool cellArraysValid=cellArrayAvailable;
        unsigned loadedTreeCount=0,loadedIndexes=0,loadedProbeIndexes=0;
        out << ",\"nativeCellGraph\":{";
        out << "\"schema\":\"WaW360.T4XenonLoadedGfxCellAabbWitness.v1\"";
        out << ",\"cellCount\":" << world.dpvsPlanes.cellCount;
        out << ",\"cellsPresent\":" << (cellArrayAvailable?"true":"false");
        out << ",\"cells\":[";
        if(cellArrayAvailable)
        {
            for(int c=0;c<11;c++)
            {
                if(c)out<<',';
                const auto& cell=world.cells[c];
                const unsigned refl=cell.reflectionProbeCount;
                const bool safe=cell.aabbTreeCount>=0 &&
                    cell.aabbTreeCount<=740 &&
                    cell.portalCount==0 && cell.cullGroupCount==0 &&
                    (cell.aabbTreeCount==0 || cell.aabbTree) &&
                    (refl==0 || cell.reflectionProbes);
                if(!safe)cellArraysValid=false;
                out<<"{\"ordinal\":"<<c<<",\"mins\":";
                WriteTriple(out,cell.mins);
                out<<",\"maxs\":";WriteTriple(out,cell.maxs);
                out<<",\"aabbTreeCount\":"<<cell.aabbTreeCount;
                out<<",\"portalCount\":"<<cell.portalCount;
                out<<",\"cullGroupCount\":"<<cell.cullGroupCount;
                out<<",\"reflectionProbeCount\":"<<refl;
                out<<",\"reflectionProbeIndices\":[";
                if(safe)
                {
                    for(unsigned j=0;j<refl;j++)
                    {
                        if(j)out<<',';
                        out<<static_cast<unsigned>(cell.reflectionProbes[j]);
                        loadedProbeIndexes++;
                    }
                }
                out<<"],\"aabbTrees\":[";
                if(safe)
                {
                    for(int j=0;j<cell.aabbTreeCount;j++)
                    {
                        if(j)out<<',';
                        const auto& tree=cell.aabbTree[j];
                        const unsigned entries=tree.smodelIndexCount;
                        const bool safeIndexes=entries<=1903 &&
                            (entries==0 || tree.smodelIndexes!=nullptr);
                        if(!safeIndexes)cellArraysValid=false;
                        out<<"{\"ordinal\":"<<j<<",\"mins\":";
                        WriteTriple(out,tree.mins);
                        out<<",\"maxs\":";WriteTriple(out,tree.maxs);
                        out<<",\"childCount\":"<<tree.childCount;
                        out<<",\"surfaceCount\":"<<tree.surfaceCount;
                        out<<",\"startSurfIndex\":"<<tree.startSurfIndex;
                        out<<",\"smodelIndexCount\":"<<entries;
                        out<<",\"childrenOffset\":"<<tree.childrenOffset;
                        out<<",\"smodelIndexes\":[";
                        if(safeIndexes)
                        {
                            for(unsigned k=0;k<entries;k++)
                            {
                                if(k)out<<',';
                                out<<tree.smodelIndexes[k];
                                loadedIndexes++;
                            }
                        }
                        out<<"]}";
                        loadedTreeCount++;
                    }
                }
                out<<"]}";
            }
        }
        out<<"]";
        out<<",\"loadedTreeCount\":"<<loadedTreeCount;
        out<<",\"loadedSmodelIndexCount\":"<<loadedIndexes;
        out<<",\"loadedReflectionProbeIndexCount\":"<<loadedProbeIndexes;
        out<<",\"loadedCellArraysValid\":"
           << (cellArraysValid?"true":"false");
        out<<",\"hardwareRuntimeSemanticsProven\":false}";

        // Gate11 independent ORIGINAL-PC-to-XENON loaded-byte semantic check.
        // The x86 OAT loader has already endian-decoded native FF scalars.
        // Hash the exact typed, pointer-free fields AS LOADED in host memory,
        // comparing with the original-PC little-endian source in PowerShell.
        // Do not export any native-reference or source FF payload bytes.
        // This intentionally covers *every byte* of all four leaf arrays.
        const auto fnv64 = [](const void* data, const std::size_t len)
        {
            auto value = std::uint64_t{14695981039346656037ull};
            const auto* bytes = static_cast<const unsigned char*>(data);
            for (std::size_t i = 0; i < len; ++i)
            {
                value ^= bytes[i];
                value *= std::uint64_t{1099511628211ull};
            }
            return value;
        };
        const auto emitDigest = [&](const void* ptr, std::size_t bytes)
        {
            const auto value = fnv64(ptr, bytes);
            out << '"'
                << std::hex << std::setfill('0') << std::setw(16)
                << value << std::dec << std::setfill(' ')
                << '"';
        };
        const bool primitivesAvailable =
            world.skySurfCount == 2 && world.skyStartSurfs != nullptr &&
            world.modelCount == 183 && world.models != nullptr &&
            world.waterBuffers[0].bufferSize == 1048704u &&
            world.waterBuffers[0].buffer != nullptr &&
            world.waterBuffers[1].bufferSize == 1048704u &&
            world.waterBuffers[1].buffer != nullptr;
        out << ",\"nativePrimitiveGraph\":{";
        out << "\"schema\":\"WaW360.T4XenonLoadedGate11PrimitiveDigest.v1\"";
        out << ",\"digest\":\"FNV1A64_LE_HOST_MEMORY_OF_TYPED_LOADED_FIELDS\"";
        out << ",\"skySurfCount\":" << world.skySurfCount;
        out << ",\"brushModelCount\":" << world.modelCount;
        out << ",\"skySourceFieldBytes\":" << (2u * sizeof(*world.skyStartSurfs));
        out << ",\"brushSourceFieldBytes\":" << (183u * sizeof(*world.models));
        out << ",\"water0Bytes\":" << world.waterBuffers[0].bufferSize;
        out << ",\"water1Bytes\":" << world.waterBuffers[1].bufferSize;
        out << ",\"skyPointerPresent\":" << (world.skyStartSurfs ? "true":"false");
        out << ",\"brushPointerPresent\":" << (world.models ? "true":"false");
        out << ",\"water0PointerPresent\":" << (world.waterBuffers[0].buffer ? "true":"false");
        out << ",\"water1PointerPresent\":" << (world.waterBuffers[1].buffer ? "true":"false");
        out << ",\"arraysAvailable\":" << (primitivesAvailable ? "true":"false");
        out << ",\"loadedHostMemoryIsLittleEndian\":true";
        out << ",\"skyDigest\":";
        if (primitivesAvailable)
            emitDigest(world.skyStartSurfs, 2u * sizeof(*world.skyStartSurfs));
        else out << "null";
        out << ",\"brushDigest\":";
        if (primitivesAvailable)
            emitDigest(world.models, 183u * sizeof(*world.models));
        else out << "null";
        out << ",\"water0Digest\":";
        if (primitivesAvailable)
            emitDigest(world.waterBuffers[0].buffer, 1048704u);
        else out << "null";
        out << ",\"water1Digest\":";
        if (primitivesAvailable)
            emitDigest(world.waterBuffers[1].buffer, 1048704u);
        else out << "null";
        out << ",\"sourcePayloadDonorCopied\":false";
        out << ",\"hardwareRuntimeSemanticsProven\":false}";

        out << ",\"materialMemoryCount\":" << world.materialMemoryCount;
        out << ",\"surfaceCount\":" << world.surfaceCount;
        out << ",\"vertexCount\":" << world.vertexCount;
        out << ",\"indexCount\":" << world.indexCount;
        const auto good = world.surfaceCount > 0 &&
            world.surfaceCount <= 100000 &&
            world.materialMemoryCount > 0 &&
            world.materialMemoryCount <= 10000 &&
            world.dpvs.surfaces && world.materialMemory;
        out << ",\"loadedArraysAvailable\":" << (good ? "true" : "false");
        if (!good)
        {
            out << ",\"status\":\"NATIVE_GFXWORLD_ARRAYS_UNAVAILABLE\"";
            out << ",\"materialMemory\":[],\"surfaces\":[]}";
            return;
        }

        out << ",\"materialMemory\":[";
        for (int j = 0; j < world.materialMemoryCount; ++j)
        {
            if (j) out << ',';
            const auto& mem = world.materialMemory[j];
            out << "{\"ordinal\":" << j << ",\"name\":";
            JsonString(out, mem.material ? mem.material->info.name : nullptr);
            out << ",\"memory\":" << mem.memory << '}';
        }
        out << ']';

        int unique = 0, unmatched = 0, ambiguous = 0;
        long long triCount = 0;
        out << ",\"surfaces\":[";
        for (int i = 0; i < world.surfaceCount; ++i)
        {
            if (i) out << ',';
            const auto& surf = world.dpvs.surfaces[i];
            const auto* material = surf.material;
            int row = -1, matches = 0;
            for (int j = 0; j < world.materialMemoryCount; ++j)
            {
                if (material && material == world.materialMemory[j].material)
                {
                    row = j; ++matches;
                }
            }
            if (matches == 1) ++unique;
            else if (!matches) ++unmatched;
            else ++ambiguous;
            triCount += surf.tris.triCount;
            out << "{\"index\":" << i;
            out << ",\"firstVertex\":" << surf.tris.firstVertex;
            out << ",\"vertexCount\":" << surf.tris.vertexCount;
            out << ",\"triangleCount\":" << surf.tris.triCount;
            out << ",\"baseIndex\":" << surf.tris.baseIndex;
            out << ",\"vertexLayerData\":" << surf.tris.vertexLayerData;
            out << ",\"topMipMins\":";
            WriteTriple(out, surf.tris.topMipMins);
            out << ",\"topMipMaxs\":";
            WriteTriple(out, surf.tris.topMipMaxs);
            out << ",\"boundsMins\":";
            WriteTriple(out, surf.bounds[0]);
            out << ",\"boundsMaxs\":";
            WriteTriple(out, surf.bounds[1]);
            out << ",\"loadedMaterialName\":";
            JsonString(out, material ? material->info.name : nullptr);
            out << ",\"materialMemoryMatchingRows\":" << matches;
            out << ",\"materialMemoryOrdinal\":"
                << (matches == 1 ? row : -1);
            out << '}';
        }
        out << ']';
        out << ",\"directMatches\":" << unique;
        out << ",\"unmatched\":" << unmatched;
        out << ",\"ambiguous\":" << ambiguous;
        out << ",\"triangles\":" << triCount;
        out << ",\"status\":\"NATIVE_XENON_GFXWORLD_SURFACE_ABI_LOADER_WITNESS\"";
        out << ",\"note\":\"Actual T4Xenon 76B GfxSurface ABI and loaded Material* names."
               " This is read-only native-reference calibration, not a built or converted Xbox FF.\"}";
    }
}

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
        out << "}";
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
            for(int i=0; i<world.dpvs.smodelCount; ++i)
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

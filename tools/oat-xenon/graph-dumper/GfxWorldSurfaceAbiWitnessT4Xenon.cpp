#include "GfxWorldSurfaceAbiWitnessT4Xenon.h"

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
        // Use finite decimal with enough digits for round-trip comparison.
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

#include "GfxWorldMaterialPointerWitnessT4.h"

#include <algorithm>
#include <cstdint>
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
    std::string SafeStem(const std::string_view value)
    {
        std::ostringstream out;
        for (const unsigned char ch : value)
        {
            if ((ch >= 'a' && ch <= 'z') ||
                (ch >= 'A' && ch <= 'Z') ||
                (ch >= '0' && ch <= '9') ||
                ch == '_' || ch == '-' || ch == '.')
                out << static_cast<char>(ch);
            else
                out << '_' << std::uppercase << std::hex << std::setw(2)
                    << std::setfill('0') << static_cast<unsigned>(ch)
                    << std::nouppercase << std::dec << std::setfill(' ');
        }
        return out.str();
    }

    void JsonString(std::ostream& out, const std::string_view value)
    {
        out << '"';
        for (const unsigned char ch : value)
        {
            switch (ch)
            {
            case '"': out << "\\\""; break;
            case '\\': out << "\\\\"; break;
            case '\b': out << "\\b"; break;
            case '\f': out << "\\f"; break;
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
}

namespace gfxworld
{
    void MaterialPointerWitnessT4::DumpAsset(
        AssetDumpingContext& context,
        const XAssetInfo<GfxWorld>& asset)
    {
        // The pinned OAT PC loader is x86, so 32-bit resolved pointers can
        // be compared directly. This file NEVER writes or repacks an FF.
        static_assert(sizeof(void*) == 4, "PC GfxWorld pointer witness requires x86 OAT.");

        const auto& world = *asset.Asset();
        const auto assetName = world.name
            ? std::string_view(world.name)
            : std::string_view(asset.m_name);
        const auto file = context.OpenAssetFile(
            std::format("pc_gfxworld_material_pointer_witness/{}.json",
                SafeStem(assetName)));
        if (!file)
            return;
        auto& out = *file;

        const auto materialCount = world.materialMemoryCount;
        const auto surfaceCount = world.surfaceCount;
        const auto validCounts = materialCount == 39 && surfaceCount == 735;
        const auto memoryAvailable = validCounts && world.materialMemory != nullptr;
        const auto surfacesAvailable = validCounts && world.dpvs.surfaces != nullptr;

        out << "{\"schema\":\"WaW360.T4PcOatGfxWorldMaterialPointerWitness.v1\"";
        out << ",\"worldName\":";
        JsonString(out, assetName);
        out << ",\"materialMemoryCount\":" << materialCount;
        out << ",\"surfaceCount\":" << surfaceCount;
        out << ",\"countsMatchAztec\":" << (validCounts ? "true" : "false");
        out << ",\"materialMemoryLoaded\":" << (memoryAvailable ? "true" : "false");
        out << ",\"surfaceArrayLoaded\":" << (surfacesAvailable ? "true" : "false");

        if (!memoryAvailable || !surfacesAvailable)
        {
            out << ",\"status\":\"LOADER_GFXWORLD_POINTER_ARRAYS_UNAVAILABLE\"";
            out << ",\"memoryRows\":[],\"surfaces\":[]";
            out << ",\"directUniqueMaterialPointerMatches\":0";
            out << ",\"memoryRecordAddressMatches\":0";
            out << ",\"unmatchedSurfaces\":0";
            out << ",\"ambiguousSurfaces\":0";
            out << ",\"note\":\"No source binding promoted; read-only OAT-loaded PC world.\"}";
            return;
        }

        std::vector<int> rowSurfaceCounts(static_cast<size_t>(materialCount), 0);
        std::vector<int> rowTriangleCounts(static_cast<size_t>(materialCount), 0);
        int uniqueDirect = 0;
        int memoryRowAddress = 0;
        int unmatched = 0;
        int ambiguous = 0;
        int totalTris = 0;

        out << ",\"surfaces\":[";
        for (int i = 0; i < surfaceCount; ++i)
        {
            const auto& surface = world.dpvs.surfaces[i];
            const auto* pointer = surface.material;
            int row = -1;
            int matchingRows = 0;
            int addressRow = -1;
            int addressMatches = 0;
            for (int j = 0; j < materialCount; ++j)
            {
                const auto& memory = world.materialMemory[j];
                if (pointer && memory.material == pointer)
                {
                    row = j;
                    matchingRows++;
                }
                if (pointer &&
                    reinterpret_cast<std::uintptr_t>(pointer) ==
                        reinterpret_cast<std::uintptr_t>(&memory))
                {
                    addressRow = j;
                    addressMatches++;
                }
            }
            if (matchingRows == 1)
            {
                uniqueDirect++;
                rowSurfaceCounts[static_cast<size_t>(row)]++;
                rowTriangleCounts[static_cast<size_t>(row)] +=
                    surface.tris.triCount;
            }
            else if (matchingRows > 1)
                ambiguous++;
            else
                unmatched++;
            if (addressMatches == 1)
                memoryRowAddress++;
            totalTris += surface.tris.triCount;
            if (i)
                out << ',';
            out << "{\"index\":" << i;
            out << ",\"triangleCount\":" << surface.tris.triCount;
            out << ",\"directMatchingMemoryRows\":" << matchingRows;
            out << ",\"directMaterialRow\":"
                << (matchingRows == 1 ? row : -1);
            out << ",\"memoryStructAddressMatchingRows\":" << addressMatches;
            out << ",\"memoryStructAddressRow\":"
                << (addressMatches == 1 ? addressRow : -1);
            out << ",\"materialNameOnDirectMatch\":";
            if (matchingRows == 1)
            {
                const auto* matchedMaterial = world.materialMemory[row].material;
                if (matchedMaterial && matchedMaterial->info.name)
                    JsonString(out, matchedMaterial->info.name);
                else
                    out << "null";
            }
            else
                out << "null";
            out << '}';
        }
        out << ']';

        out << ",\"memoryRows\":[";
        for (int j = 0; j < materialCount; ++j)
        {
            if (j)
                out << ',';
            const auto& memory = world.materialMemory[j];
            out << "{\"ordinal\":" << j;
            out << ",\"memoryValue\":" << memory.memory;
            out << ",\"materialPointerPresent\":"
                << (memory.material ? "true" : "false");
            out << ",\"materialName\":";
            // Only dereference pointers which the OAT loader fixed up into
            // the loaded MaterialMemory material reference graph.
            if (memory.material && memory.material->info.name)
                JsonString(out, memory.material->info.name);
            else
                out << "null";
            out << ",\"directSurfaceMatches\":"
                << rowSurfaceCounts[static_cast<size_t>(j)];
            out << ",\"directTriangleMatches\":"
                << rowTriangleCounts[static_cast<size_t>(j)];
            out << '}';
        }
        out << ']';

        out << ",\"directUniqueMaterialPointerMatches\":" << uniqueDirect;
        out << ",\"memoryRecordAddressMatches\":" << memoryRowAddress;
        out << ",\"unmatchedSurfaces\":" << unmatched;
        out << ",\"ambiguousSurfaces\":" << ambiguous;
        out << ",\"totalTriangles\":" << totalTris;
        out << ",\"status\":\"OAT_PC_LOADED_GFXWORLD_POINTER_WITNESS_RECORDED\"";
        out << ",\"note\":\"Direct equality of post-OAT-PC-loader in-memory "
               "Material* pointers and MaterialMemory[i].material is independent "
               "of the physical source-byte ordinal hypothesis. A pointer "
               "matching a MaterialMemory RECORD ADDRESS is reported separately "
               "and is NOT a typed Material* ownership proof. No Xbox Material "
               "root binding, OAT Xenon serialization, or fastfile emitted.\"}";
    }
}

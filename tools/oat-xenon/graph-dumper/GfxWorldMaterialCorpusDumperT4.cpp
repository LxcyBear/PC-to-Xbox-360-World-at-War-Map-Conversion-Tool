#include "GfxWorldMaterialCorpusDumperT4.h"

#include <cstddef>
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

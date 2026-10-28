#include "MaterialRelocatableGraphDumperT4Xenon.h"

#include "Game/T4Xenon/AssetEndianSwapT4Xenon.h"

#include <cstddef>
#include <cstdint>
#include <format>
#include <iomanip>
#include <limits>
#include <ostream>
#include <sstream>
#include <string>
#include <string_view>
#include <unordered_set>

using namespace T4Xenon;

namespace
{
    constexpr uintptr_t FOLLOWING = 0xFFFFFFFFu;

    template <typename T>
    T* FollowingPtr()
    {
        return reinterpret_cast<T*>(FOLLOWING);
    }

    const char* FollowingString()
    {
        return reinterpret_cast<const char*>(FOLLOWING);
    }

    std::string SafeStem(const std::string_view value)
    {
        std::ostringstream out;
        for (const unsigned char ch : value)
        {
            if ((ch >= 'a' && ch <= 'z') ||
                (ch >= 'A' && ch <= 'Z') ||
                (ch >= '0' && ch <= '9') ||
                ch == '_' || ch == '-' || ch == '.')
            {
                out << static_cast<char>(ch);
            }
            else
            {
                out << '_' << std::uppercase << std::hex << std::setw(2)
                    << std::setfill('0') << static_cast<unsigned>(ch)
                    << std::nouppercase << std::dec << std::setfill(' ');
            }
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
                {
                    out << "\\u00" << std::uppercase << std::hex << std::setw(2)
                        << std::setfill('0') << static_cast<unsigned>(ch)
                        << std::nouppercase << std::dec << std::setfill(' ');
                }
                else
                {
                    out << static_cast<char>(ch);
                }
                break;
            }
        }
        out << '"';
    }

    void JsonNullableString(std::ostream& out, const char* value)
    {
        if (!value)
        {
            out << "null";
            return;
        }
        JsonString(out, value);
    }

    template <typename T>
    void DumpEncodedStruct(
        AssetDumpingContext& context,
        const std::string& relativePath,
        const T& value)
    {
        auto encoded = value;
        EndianSwap(encoded, EndianOperation::Encode);
        const auto file = context.OpenAssetFile(relativePath);
        if (file)
            file->write(
                reinterpret_cast<const char*>(&encoded),
                static_cast<std::streamsize>(sizeof(encoded)));
    }

    template <typename T>
    void DumpEncodedArray(
        AssetDumpingContext& context,
        const std::string& relativePath,
        const T* values,
        const size_t count)
    {
        const auto file = context.OpenAssetFile(relativePath);
        if (!file)
            return;

        for (size_t i = 0; i < count; ++i)
        {
            auto encoded = values[i];
            EndianSwap(encoded, EndianOperation::Encode);
            file->write(
                reinterpret_cast<const char*>(&encoded),
                static_cast<std::streamsize>(sizeof(encoded)));
        }
    }

    bool RegisterReusable(
        std::unordered_set<const void*>& seen,
        const void* value)
    {
        if (!value)
            return true;
        return seen.emplace(value).second;
    }

    size_t WaterCount(const water_t& water, bool& valid)
    {
        if (water.M < 0 || water.N < 0)
        {
            valid = false;
            return 0;
        }

        const auto m = static_cast<size_t>(water.M);
        const auto n = static_cast<size_t>(water.N);
        if (m != 0 && n > std::numeric_limits<size_t>::max() / m)
        {
            valid = false;
            return 0;
        }

        valid = true;
        return m * n;
    }

    void WriteFileField(
        std::ostream& out,
        const char* name,
        const std::string& path,
        const bool present)
    {
        out << ",\"" << name << "\":";
        if (present)
            JsonString(out, path);
        else
            out << "null";
    }
}

namespace material
{
    void RelocatableGraphDumperT4Xenon::DumpAsset(
        AssetDumpingContext& context,
        const XAssetInfo<Material>& asset)
    {
        static_assert(sizeof(void*) == 4, "T4 Xenon material graph dumper must be built x86.");

        const auto& material = *asset.Asset();
        const auto assetName = material.info.name
            ? std::string_view(material.info.name)
            : std::string_view(asset.m_name);
        const auto stem = SafeStem(assetName);

        const auto graphPath = std::format("xenon_material_graphs/{}.json", stem);
        const auto blobRoot = std::format("xenon_material_graph_blobs/{}", stem);
        const auto rootPath = blobRoot + "/root.bin";
        const auto texturePath = blobRoot + "/textures.bin";
        const auto constantPath = blobRoot + "/constants.bin";
        const auto statePath = blobRoot + "/stateBits.bin";

        const auto file = context.OpenAssetFile(graphPath);
        if (!file)
            return;

        std::unordered_set<const void*> reusablePointers;
        unsigned reusableAliasCount = 0;
        const auto noteReusable = [&](const void* p)
        {
            if (p && !RegisterReusable(reusablePointers, p))
                reusableAliasCount++;
        };

        noteReusable(material.textureTable);
        noteReusable(material.constantTable);
        noteReusable(material.stateBitsTable);

        auto root = material;
        root.info.name = material.info.name ? FollowingString() : nullptr;
        root.techniqueSet = nullptr;
        root.textureTable = material.textureTable ? FollowingPtr<MaterialTextureDef>() : nullptr;
        root.constantTable = material.constantTable ? FollowingPtr<MaterialConstantDef>() : nullptr;
        root.stateBitsTable = material.stateBitsTable ? FollowingPtr<GfxStateBits>() : nullptr;
        DumpEncodedStruct(context, rootPath, root);

        if (material.textureTable && material.textureCount)
        {
            const auto textureFile = context.OpenAssetFile(texturePath);
            if (textureFile)
            {
                for (unsigned i = 0; i < material.textureCount; ++i)
                {
                    const auto& source = material.textureTable[i];
                    auto texture = source;

                    if (source.semantic == TS_WATER_MAP)
                    {
                        noteReusable(source.u.water);
                        texture.u.water =
                            source.u.water ? FollowingPtr<water_t>() : nullptr;
                    }
                    else
                    {
                        texture.u.image = nullptr;
                    }

                    EndianSwap(texture, EndianOperation::Encode);
                    textureFile->write(
                        reinterpret_cast<const char*>(&texture),
                        static_cast<std::streamsize>(sizeof(texture)));
                }
            }
        }

        if (material.constantTable && material.constantCount)
            DumpEncodedArray(
                context,
                constantPath,
                material.constantTable,
                material.constantCount);

        if (material.stateBitsTable && material.stateBitsCount)
            DumpEncodedArray(
                context,
                statePath,
                material.stateBitsTable,
                material.stateBitsCount);

        unsigned waterTextures = 0;
        unsigned invalidWaterDimensions = 0;
        size_t waterSamples = 0;

        auto& out = *file;
        out << "{";
        out << "\"format\":\"WaW360.T4Xenon.MaterialRelocatableGraph.v1\"";
        out << ",\"name\":";
        JsonString(out, assetName);

        out << ",\"abi\":{";
        out << "\"material\":" << sizeof(Material);
        out << ",\"textureDef\":" << sizeof(MaterialTextureDef);
        out << ",\"constantDef\":" << sizeof(MaterialConstantDef);
        out << ",\"stateBits\":" << sizeof(GfxStateBits);
        out << ",\"water\":" << sizeof(water_t);
        out << ",\"float\":" << sizeof(float);
        out << "}";

        out << ",\"alignments\":{";
        out << "\"material\":" << alignof(Material);
        out << ",\"textureDef\":" << alignof(MaterialTextureDef);
        out << ",\"constantDef\":16";
        out << ",\"stateBits\":" << alignof(GfxStateBits);
        out << ",\"water\":" << alignof(water_t);
        out << ",\"float\":" << alignof(float);
        out << "}";

        out << ",\"offsets\":{";
        out << "\"techniqueSet\":" << offsetof(Material, techniqueSet);
        out << ",\"textureUnion\":" << offsetof(MaterialTextureDef, u);
        out << ",\"waterImage\":" << offsetof(water_t, image);
        out << "}";

        out << ",\"counts\":{";
        out << "\"textures\":" << static_cast<unsigned>(material.textureCount);
        out << ",\"constants\":" << static_cast<unsigned>(material.constantCount);
        out << ",\"stateBits\":" << static_cast<unsigned>(material.stateBitsCount);
        out << "}";

        out << ",\"presence\":{";
        out << "\"name\":" << (material.info.name ? "true" : "false");
        out << ",\"textureTable\":" << (material.textureTable ? "true" : "false");
        out << ",\"constantTable\":" << (material.constantTable ? "true" : "false");
        out << ",\"stateBitsTable\":" << (material.stateBitsTable ? "true" : "false");
        out << "}";

        out << ",\"reusable\":{";
        out << "\"uniquePointers\":" << reusablePointers.size();
        out << ",\"aliasCount\":" << reusableAliasCount;
        out << "}";

        out << ",\"techniqueSet\":";
        JsonNullableString(
            out,
            material.techniqueSet ? material.techniqueSet->name : nullptr);

        out << ",\"rootFile\":";
        JsonString(out, rootPath);
        WriteFileField(
            out,
            "textureTableFile",
            texturePath,
            material.textureTable && material.textureCount);
        WriteFileField(
            out,
            "constantTableFile",
            constantPath,
            material.constantTable && material.constantCount);
        WriteFileField(
            out,
            "stateBitsTableFile",
            statePath,
            material.stateBitsTable && material.stateBitsCount);

        out << ",\"textures\":[";
        for (unsigned i = 0; material.textureTable && i < material.textureCount; ++i)
        {
            if (i) out << ',';
            const auto& texture = material.textureTable[i];

            out << "{";
            out << "\"index\":" << i;
            out << ",\"semantic\":" << static_cast<unsigned>(texture.semantic);
            out << ",\"image\":";

            if (texture.semantic == TS_WATER_MAP)
            {
                const auto* water = texture.u.water;
                JsonNullableString(
                    out,
                    water && water->image ? water->image->name : nullptr);
                out << ",\"water\":";

                if (!water)
                {
                    out << "null";
                }
                else
                {
                    waterTextures++;
                    bool validWater = true;
                    const auto count = WaterCount(*water, validWater);
                    if (!validWater)
                        invalidWaterDimensions++;
                    else
                        waterSamples += count;

                    const auto waterPrefix =
                        std::format("{}/water_{:03}", blobRoot, i);
                    const auto waterPath = waterPrefix + ".bin";
                    const auto h0xPath = waterPrefix + ".h0x.bin";
                    const auto h0yPath = waterPrefix + ".h0y.bin";
                    const auto wtermPath = waterPrefix + ".wterm.bin";

                    auto waterBody = *water;
                    waterBody.H0X = water->H0X ? FollowingPtr<float>() : nullptr;
                    waterBody.H0Y = water->H0Y ? FollowingPtr<float>() : nullptr;
                    waterBody.wTerm = water->wTerm ? FollowingPtr<float>() : nullptr;
                    waterBody.image = nullptr;
                    DumpEncodedStruct(context, waterPath, waterBody);

                    if (validWater && water->H0X && count)
                        DumpEncodedArray(context, h0xPath, water->H0X, count);
                    if (validWater && water->H0Y && count)
                        DumpEncodedArray(context, h0yPath, water->H0Y, count);
                    if (validWater && water->wTerm && count)
                        DumpEncodedArray(context, wtermPath, water->wTerm, count);

                    out << "{";
                    out << "\"m\":" << water->M;
                    out << ",\"n\":" << water->N;
                    out << ",\"sampleCount\":" << count;
                    out << ",\"validDimensions\":" << (validWater ? "true" : "false");
                    out << ",\"presence\":{";
                    out << "\"h0x\":" << (water->H0X ? "true" : "false");
                    out << ",\"h0y\":" << (water->H0Y ? "true" : "false");
                    out << ",\"wTerm\":" << (water->wTerm ? "true" : "false");
                    out << "}";
                    out << ",\"waterFile\":";
                    JsonString(out, waterPath);
                    WriteFileField(
                        out,
                        "h0xFile",
                        h0xPath,
                        validWater && water->H0X && count);
                    WriteFileField(
                        out,
                        "h0yFile",
                        h0yPath,
                        validWater && water->H0Y && count);
                    WriteFileField(
                        out,
                        "wTermFile",
                        wtermPath,
                        validWater && water->wTerm && count);
                    out << "}";
                }
            }
            else
            {
                JsonNullableString(
                    out,
                    texture.u.image ? texture.u.image->name : nullptr);
                out << ",\"water\":null";
            }

            out << "}";
        }
        out << "]";

        out << ",\"inventory\":{";
        out << "\"waterTextures\":" << waterTextures;
        out << ",\"waterSamples\":" << waterSamples;
        out << ",\"invalidWaterDimensions\":" << invalidWaterDimensions;
        out << "}";

        out << "}";
    }
}

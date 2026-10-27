#include "ImageGraphDumperT4Xenon.h"

#include <cstdint>
#include <cstring>
#include <format>
#include <iomanip>
#include <ostream>
#include <sstream>
#include <string>
#include <string_view>

using namespace T4Xenon;

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

    void DumpBytes(
        AssetDumpingContext& context,
        const std::string& relativePath,
        const void* data,
        const size_t size)
    {
        if (!data || size == 0)
            return;

        const auto file = context.OpenAssetFile(relativePath);
        if (!file)
            return;

        file->write(reinterpret_cast<const char*>(data), static_cast<std::streamsize>(size));
    }
}

namespace image
{
    void ImageGraphDumperT4Xenon::DumpAsset(
        AssetDumpingContext& context,
        const XAssetInfo<GfxImage>& asset)
    {
        const auto& image = *asset.Asset();
        const auto assetName = image.name ? std::string_view(image.name) : std::string_view(asset.m_name);
        const auto stem = SafeStem(assetName);

        const auto jsonPath = std::format("xenon_image_graphs/{}.json", stem);
        const auto pixelPath = std::format("xenon_image_pixels/{}.pixels.bin", stem);
        const auto texturePath = std::format("xenon_image_headers/{}.d3dtexture.bin", stem);

        const auto file = context.OpenAssetFile(jsonPath);
        if (!file)
            return;

        const auto* loadDef = image.texture.loadDef;
        const auto* texture = loadDef ? loadDef->texture.map : nullptr;
        const auto pixelBytes = image.cardMemory.platform[0] > 0
            ? static_cast<size_t>(image.cardMemory.platform[0])
            : 0uz;

        if (image.pixels && pixelBytes != 0)
            DumpBytes(context, pixelPath, image.pixels, pixelBytes);
        if (texture)
            DumpBytes(context, texturePath, texture, sizeof(D3DTexture));

        auto& out = *file;
        out << "{";
        out << "\"format\":\"WaW360.T4Xenon.ImageGraph.v1\"";
        out << ",\"name\":";
        JsonString(out, image.name);
        out << ",\"mapType\":" << static_cast<unsigned>(image.mapType);
        out << ",\"semantic\":" << static_cast<unsigned>(image.semantic);
        out << ",\"cardMemoryBytes\":" << image.cardMemory.platform[0];
        out << ",\"width\":" << static_cast<unsigned>(image.width);
        out << ",\"height\":" << static_cast<unsigned>(image.height);
        out << ",\"depth\":" << static_cast<unsigned>(image.depth);
        out << ",\"category\":" << static_cast<unsigned>(image.category);
        out << ",\"delayLoadPixels\":" << (image.delayLoadPixels ? "true" : "false");
        out << ",\"baseSize\":" << image.baseSize;
        out << ",\"streamSlot\":" << image.streamSlot;
        out << ",\"streaming\":" << (image.streaming ? "true" : "false");
        out << ",\"pixelsFile\":";
        if (image.pixels && pixelBytes != 0)
            JsonString(out, pixelPath.c_str());
        else
            out << "null";
        out << ",\"pixelsBytes\":" << pixelBytes;
        out << ",\"loadDef\":";

        if (!loadDef)
        {
            out << "null";
        }
        else
        {
            out << "{";
            out << "\"levelCount\":" << static_cast<unsigned>(loadDef->levelCount);
            out << ",\"flags\":" << static_cast<unsigned>(loadDef->flags);
            out << ",\"dimensions\":["
                << loadDef->dimensions[0] << ','
                << loadDef->dimensions[1] << ','
                << loadDef->dimensions[2] << ']';
            out << ",\"format\":" << loadDef->format;
            out << ",\"textureHeaderFile\":";
            if (texture)
                JsonString(out, texturePath.c_str());
            else
                out << "null";
            out << ",\"textureHeaderBytes\":" << (texture ? sizeof(D3DTexture) : 0);
            out << "}";
        }

        out << "}";
    }
}

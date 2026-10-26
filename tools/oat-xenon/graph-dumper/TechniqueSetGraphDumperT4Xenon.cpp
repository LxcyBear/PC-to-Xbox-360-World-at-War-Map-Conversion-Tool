#include "TechniqueSetGraphDumperT4Xenon.h"

#include <algorithm>
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
        const unsigned char* data,
        const size_t size)
    {
        if (!data || size == 0)
            return;

        const auto file = context.OpenAssetFile(relativePath);
        if (!file)
            return;

        file->write(reinterpret_cast<const char*>(data), static_cast<std::streamsize>(size));
    }

    std::string ShaderStem(const char* name, const std::string_view stage)
    {
        const std::string safeName = SafeStem(name ? std::string_view(name) : std::string_view("unnamed"));
        return std::format("xenon_graph_shaders/{}.{}", safeName, stage);
    }

    void DumpVertexShaderFiles(AssetDumpingContext& context, const MaterialVertexShader* shader)
    {
        if (!shader)
            return;

        const auto stem = ShaderStem(shader->name, "vs");
        const auto& def = shader->prog.loadDef;
        DumpBytes(context, stem + ".cached.bin", def.cachedPart, def.cachedPartSize);
        DumpBytes(context, stem + ".physical.bin", def.physicalPart, def.physicalPartSize);
    }

    void DumpPixelShaderFiles(AssetDumpingContext& context, const MaterialPixelShader* shader)
    {
        if (!shader)
            return;

        const auto stem = ShaderStem(shader->name, "ps");
        const auto& def = shader->prog.loadDef;
        DumpBytes(context, stem + ".cached.bin", def.cachedPart, def.cachedPartSize);
        DumpBytes(context, stem + ".physical.bin", def.physicalPart, def.physicalPartSize);
    }

    void WriteVertexShaderRef(
        AssetDumpingContext& context,
        std::ostream& out,
        const MaterialVertexShader* shader)
    {
        if (!shader)
        {
            out << "null";
            return;
        }

        DumpVertexShaderFiles(context, shader);
        const auto stem = ShaderStem(shader->name, "vs");
        const auto& def = shader->prog.loadDef;

        out << "{";
        out << "\"name\":";
        JsonString(out, shader->name);
        out << ",\"cachedPartSize\":" << static_cast<unsigned>(def.cachedPartSize);
        out << ",\"physicalPartSize\":" << static_cast<unsigned>(def.physicalPartSize);
        out << ",\"cachedFile\":";
        JsonString(out, (stem + ".cached.bin").c_str());
        out << ",\"physicalFile\":";
        JsonString(out, (stem + ".physical.bin").c_str());
        out << "}";
    }

    void WritePixelShaderRef(
        AssetDumpingContext& context,
        std::ostream& out,
        const MaterialPixelShader* shader)
    {
        if (!shader)
        {
            out << "null";
            return;
        }

        DumpPixelShaderFiles(context, shader);
        const auto stem = ShaderStem(shader->name, "ps");
        const auto& def = shader->prog.loadDef;

        out << "{";
        out << "\"name\":";
        JsonString(out, shader->name);
        out << ",\"cachedPartSize\":" << static_cast<unsigned>(def.cachedPartSize);
        out << ",\"physicalPartSize\":" << static_cast<unsigned>(def.physicalPartSize);
        out << ",\"cachedFile\":";
        JsonString(out, (stem + ".cached.bin").c_str());
        out << ",\"physicalFile\":";
        JsonString(out, (stem + ".physical.bin").c_str());
        out << "}";
    }

    void WriteVertexDecl(std::ostream& out, const MaterialVertexDeclaration* decl)
    {
        if (!decl)
        {
            out << "null";
            return;
        }

        out << "{";
        out << "\"streamCount\":" << static_cast<unsigned>(decl->streamCount);
        out << ",\"hasOptionalSource\":" << (decl->hasOptionalSource ? "true" : "false");
        out << ",\"routing\":[";
        for (size_t i = 0; i < 16; i++)
        {
            if (i) out << ',';
            out << "{\"source\":" << static_cast<unsigned>(decl->routing.data[i].source)
                << ",\"dest\":" << static_cast<unsigned>(decl->routing.data[i].dest) << "}";
        }
        out << "]}";
    }

    void WriteShaderArg(std::ostream& out, const MaterialShaderArgument& arg)
    {
        uint32_t raw = 0;
        static_assert(sizeof(arg.u) == sizeof(raw));
        std::memcpy(&raw, &arg.u, sizeof(raw));

        out << "{";
        out << "\"type\":" << static_cast<unsigned>(arg.type);
        out << ",\"dest\":" << static_cast<unsigned>(arg.dest);
        out << ",\"uRaw\":" << raw;

        if ((arg.type == MTL_ARG_LITERAL_VERTEX_CONST ||
             arg.type == MTL_ARG_LITERAL_PIXEL_CONST) &&
            arg.u.literalConst)
        {
            out << ",\"literalConst\":["
                << (*arg.u.literalConst)[0] << ','
                << (*arg.u.literalConst)[1] << ','
                << (*arg.u.literalConst)[2] << ','
                << (*arg.u.literalConst)[3] << ']';
        }

        if (arg.type == MTL_ARG_CODE_VERTEX_CONST ||
            arg.type == MTL_ARG_CODE_PIXEL_CONST)
        {
            out << ",\"codeConst\":{\"index\":"
                << static_cast<unsigned>(arg.u.codeConst.index)
                << ",\"firstRow\":" << static_cast<unsigned>(arg.u.codeConst.firstRow)
                << ",\"rowCount\":" << static_cast<unsigned>(arg.u.codeConst.rowCount)
                << "}";
        }

        out << "}";
    }

    void WritePass(
        AssetDumpingContext& context,
        std::ostream& out,
        const MaterialPass& pass)
    {
        out << "{";
        out << "\"perPrimArgCount\":" << static_cast<unsigned>(pass.perPrimArgCount);
        out << ",\"perObjArgCount\":" << static_cast<unsigned>(pass.perObjArgCount);
        out << ",\"stableArgCount\":" << static_cast<unsigned>(pass.stableArgCount);
        out << ",\"customSamplerFlags\":" << static_cast<unsigned>(pass.customSamplerFlags);
        out << ",\"precompiledIndex\":" << static_cast<unsigned>(pass.precompiledIndex);

        out << ",\"vertexDecl\":";
        WriteVertexDecl(out, pass.vertexDecl);

        out << ",\"vertexShaderArray\":[";
        for (size_t i = 0; i < 16; i++)
        {
            if (i) out << ',';
            WriteVertexShaderRef(context, out, pass.vertexShaderArray[i]);
        }
        out << "]";

        out << ",\"vertexShader\":";
        WriteVertexShaderRef(context, out, pass.vertexShader);

        out << ",\"pixelShader\":";
        WritePixelShaderRef(context, out, pass.pixelShader);

        const size_t argCount =
            static_cast<size_t>(pass.perPrimArgCount) +
            static_cast<size_t>(pass.perObjArgCount) +
            static_cast<size_t>(pass.stableArgCount);

        out << ",\"args\":[";
        if (pass.args)
        {
            for (size_t i = 0; i < argCount; i++)
            {
                if (i) out << ',';
                WriteShaderArg(out, pass.args[i]);
            }
        }
        out << "]";

        out << "}";
    }

    void WriteTechnique(
        AssetDumpingContext& context,
        std::ostream& out,
        const MaterialTechnique* technique)
    {
        if (!technique)
        {
            out << "null";
            return;
        }

        out << "{";
        out << "\"name\":";
        JsonString(out, technique->name);
        out << ",\"flags\":" << static_cast<unsigned>(technique->flags);
        out << ",\"passCount\":" << static_cast<unsigned>(technique->passCount);
        out << ",\"passes\":[";
        for (size_t i = 0; i < technique->passCount; i++)
        {
            if (i) out << ',';
            WritePass(context, out, technique->passArray[i]);
        }
        out << "]}";
    }
}

namespace techset
{
    void TechniqueSetGraphDumperT4Xenon::DumpAsset(
        AssetDumpingContext& context,
        const XAssetInfo<MaterialTechniqueSet>& asset)
    {
        const auto& techset = *asset.Asset();
        const auto name = techset.name ? std::string_view(techset.name) : std::string_view(asset.m_name);
        const auto outputName = std::format("xenon_techset_graphs/{}.json", SafeStem(name));
        const auto file = context.OpenAssetFile(outputName);
        if (!file)
            return;

        auto& out = *file;
        out << "{";
        out << "\"format\":\"WaW360.T4Xenon.TechniqueSetGraph.v1\"";
        out << ",\"name\":";
        JsonString(out, techset.name);
        out << ",\"worldVertFormat\":" << static_cast<unsigned>(techset.worldVertFormat);
        out << ",\"hasBeenUploaded\":" << (techset.hasBeenUploaded ? "true" : "false");

        out << ",\"slots\":[";
        for (size_t i = 0; i < 51; i++)
        {
            if (i) out << ',';
            out << "{\"slot\":" << i << ",\"technique\":";
            WriteTechnique(context, out, techset.techniques[i]);
            out << "}";
        }
        out << "]";

        out << "}";
    }
}

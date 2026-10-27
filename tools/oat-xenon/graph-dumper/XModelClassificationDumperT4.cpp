#include "XModelClassificationDumperT4.h"

#include <format>
#include <iomanip>
#include <ostream>
#include <sstream>
#include <string>
#include <string_view>

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
}

namespace xmodel
{
    void ClassificationDumperT4::DumpAsset(
        AssetDumpingContext& context,
        const XAssetInfo<XModel>& asset)
    {
        const auto& model = *asset.Asset();
        const auto assetName = !asset.m_name.empty()
            ? std::string_view(asset.m_name)
            : (model.name ? std::string_view(model.name) : std::string_view());

        const auto file = context.OpenAssetFile(
            std::format("xmodel_classification/{}.json", SafeStem(assetName)));
        if (!file)
            return;

        auto& out = *file;
        out << "{";
        out << "\"format\":\"WaW360.T4.XModelClassification.v1\"";
        out << ",\"name\":";
        JsonString(out, assetName);
        out << ",\"numBones\":" << static_cast<unsigned>(model.numBones);
        out << ",\"numRootBones\":" << static_cast<unsigned>(model.numRootBones);
        out << ",\"bones\":[";

        for (unsigned i = 0; i < model.numBones; i++)
        {
            if (i != 0)
                out << ',';

            std::string_view boneName;
            unsigned scriptString = 0;
            if (model.boneNames)
            {
                scriptString = static_cast<unsigned>(model.boneNames[i]);
                if (scriptString < context.m_zone.m_script_strings.Count())
                    boneName = context.m_zone.m_script_strings[scriptString];
            }

            const auto classification = model.partClassification
                ? static_cast<unsigned>(model.partClassification[i])
                : 0u;

            out << "{\"index\":" << i;
            out << ",\"scriptString\":" << scriptString;
            out << ",\"name\":";
            JsonString(out, boneName);
            out << ",\"classification\":" << classification;
            out << "}";
        }

        out << "]}";
    }
}

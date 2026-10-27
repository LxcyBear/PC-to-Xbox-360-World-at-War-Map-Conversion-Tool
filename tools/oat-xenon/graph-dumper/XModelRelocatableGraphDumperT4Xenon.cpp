#include "XModelRelocatableGraphDumperT4Xenon.h"

#include "Game/T4Xenon/AssetEndianSwapT4Xenon.h"

#include <cstddef>
#include <cstdint>
#include <format>
#include <iomanip>
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

    template <typename T>
    void EncodeForZone(T& value)
    {
        EndianSwap(value, EndianOperation::Encode);
    }

    template <>
    void EncodeForZone<float>(float& value)
    {
        EndianSwap(value);
    }

    template <>
    void EncodeForZone<unsigned short>(unsigned short& value)
    {
        EndianSwap(value);
    }

    template <typename T>
    void DumpEncodedStruct(
        AssetDumpingContext& context,
        const std::string& relativePath,
        T value)
    {
        EncodeForZone(value);
        DumpBytes(context, relativePath, &value, sizeof(value));
    }

    template <typename T>
    void DumpEncodedArray(
        AssetDumpingContext& context,
        const std::string& relativePath,
        const T* values,
        const size_t count)
    {
        if (!values || count == 0)
            return;

        const auto file = context.OpenAssetFile(relativePath);
        if (!file)
            return;

        for (size_t i = 0; i < count; ++i)
        {
            auto value = values[i];
            EncodeForZone(value);
            file->write(
                reinterpret_cast<const char*>(&value),
                static_cast<std::streamsize>(sizeof(value)));
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

    size_t BlendCount(const XSurfaceVertexInfo& info)
    {
        return static_cast<size_t>(info.vertCount[0]) +
            3uz * static_cast<size_t>(info.vertCount[1]) +
            5uz * static_cast<size_t>(info.vertCount[2]) +
            7uz * static_cast<size_t>(info.vertCount[3]);
    }

    void WriteFileField(std::ostream& out, const char* name, const std::string& path, const bool present)
    {
        out << ",\"" << name << "\":";
        if (present)
            JsonString(out, path);
        else
            out << "null";
    }
}

namespace xmodel
{
    void RelocatableGraphDumperT4Xenon::DumpAsset(
        AssetDumpingContext& context,
        const XAssetInfo<XModel>& asset)
    {
        static_assert(sizeof(void*) == 4, "T4 Xenon graph dumper must be built x86.");
        static_assert(sizeof(XModel) == 0xD4, "Unexpected T4 Xenon XModel ABI.");

        const auto& model = *asset.Asset();
        const auto assetName = model.name
            ? std::string_view(model.name)
            : std::string_view(asset.m_name);
        const auto stem = SafeStem(assetName);

        const auto graphPath = std::format("xenon_xmodel_graphs/{}.json", stem);
        const auto blobRoot = std::format("xenon_xmodel_graph_blobs/{}", stem);
        const auto rootPath = blobRoot + "/root.bin";

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

        noteReusable(model.boneNames);
        noteReusable(model.parentList);
        noteReusable(model.quats);
        noteReusable(model.trans);
        noteReusable(model.partClassification);
        noteReusable(model.baseMat);

        unsigned unsupportedCount = 0;
        if (model.numCollSurfs != 0 || model.collSurfs)
            unsupportedCount++;
        if (model.physGeoms)
            unsupportedCount++;
        if (model.collmap)
            unsupportedCount++;
        if (model.physConstraints)
            unsupportedCount++;

        // Root body: normalize owned children to FOLLOWING, clear asset references
        // for replay-time rebinding, then encode with OAT's own Xenon endian rules.
        auto root = model;
        root.name = model.name ? FollowingString() : nullptr;
        root.boneNames = model.boneNames ? FollowingPtr<ScriptString>() : nullptr;
        root.parentList = model.parentList ? FollowingPtr<unsigned char>() : nullptr;
        root.quats = model.quats ? FollowingPtr<XModelQuat>() : nullptr;
        root.trans = model.trans ? FollowingPtr<float>() : nullptr;
        root.partClassification = model.partClassification ? FollowingPtr<unsigned char>() : nullptr;
        root.baseMat = model.baseMat ? FollowingPtr<DObjAnimMat>() : nullptr;
        root.surfs = model.surfs ? FollowingPtr<XSurface>() : nullptr;
        root.materialHandles = model.materialHandles ? FollowingPtr<Material*>() : nullptr;
        root.collSurfs = nullptr;
        root.boneInfo = model.boneInfo ? FollowingPtr<XBoneInfo>() : nullptr;
        root.streamInfo.highMipBounds =
            model.streamInfo.highMipBounds ? FollowingPtr<XModelHighMipBounds>() : nullptr;
        root.physPreset = nullptr;
        root.physGeoms = nullptr;
        root.collmap = nullptr;
        root.physConstraints = nullptr;
        DumpEncodedStruct(context, rootPath, root);

        const unsigned nonRootBones =
            static_cast<unsigned>(model.numBones) - static_cast<unsigned>(model.numRootBones);

        const auto parentPath = blobRoot + "/parentList.bin";
        const auto quatsPath = blobRoot + "/quats.bin";
        const auto transPath = blobRoot + "/trans.bin";
        const auto partPath = blobRoot + "/partClassification.bin";
        const auto baseMatPath = blobRoot + "/baseMat.bin";
        const auto surfacesPath = blobRoot + "/surfaces.bin";
        const auto boneInfoPath = blobRoot + "/boneInfo.bin";
        const auto highMipPath = blobRoot + "/highMipBounds.bin";

        if (model.parentList && nonRootBones)
            DumpBytes(context, parentPath, model.parentList, nonRootBones);
        if (model.quats && nonRootBones)
            DumpEncodedArray(context, quatsPath, model.quats, nonRootBones);
        if (model.trans && nonRootBones)
            DumpEncodedArray(context, transPath, model.trans, static_cast<size_t>(nonRootBones) * 4uz);
        if (model.partClassification && model.numBones)
            DumpBytes(context, partPath, model.partClassification, model.numBones);
        if (model.baseMat && model.numBones)
            DumpEncodedArray(context, baseMatPath, model.baseMat, model.numBones);

        // Surface fixed bodies are contiguous. Pointers are normalized to FOLLOWING;
        // the child order is replayed from T4Xenon XModel ZoneCode.
        if (model.surfs && model.numsurfs)
        {
            const auto surfaceFile = context.OpenAssetFile(surfacesPath);
            if (surfaceFile)
            {
                for (unsigned surfaceIndex = 0; surfaceIndex < model.numsurfs; ++surfaceIndex)
                {
                    const auto& source = model.surfs[surfaceIndex];
                    noteReusable(source.vertInfo.vertsBlend);
                    noteReusable(source.verts0);
                    noteReusable(source.vertList);
                    noteReusable(source.triIndices);

                    auto surface = source;
                    surface.vertInfo.vertsBlend =
                        source.vertInfo.vertsBlend ? FollowingPtr<unsigned short>() : nullptr;
                    surface.verts0 =
                        source.verts0 ? FollowingPtr<GfxPackedVertex>() : nullptr;
                    surface.vertList =
                        source.vertList ? FollowingPtr<XRigidVertList>() : nullptr;
                    surface.triIndices =
                        source.triIndices ? FollowingPtr<XSurfaceTri16>() : nullptr;
                    EndianSwap(surface, EndianOperation::Encode);
                    surfaceFile->write(
                        reinterpret_cast<const char*>(&surface),
                        static_cast<std::streamsize>(sizeof(surface)));
                }
            }
        }

        unsigned totalCollisionTrees = 0;
        unsigned totalCollisionNodes = 0;
        unsigned totalCollisionLeafs = 0;

        for (unsigned surfaceIndex = 0; model.surfs && surfaceIndex < model.numsurfs; ++surfaceIndex)
        {
            const auto& surface = model.surfs[surfaceIndex];
            const auto surfacePrefix = std::format("{}/s{:03}", blobRoot, surfaceIndex);

            const auto blendCount = BlendCount(surface.vertInfo);
            const auto blendPath = surfacePrefix + ".vertsBlend.bin";
            const auto vertsPath = surfacePrefix + ".verts0.bin";
            const auto vertListPath = surfacePrefix + ".vertList.bin";
            const auto triPath = surfacePrefix + ".triIndices.bin";

            if (surface.vertInfo.vertsBlend && blendCount)
                DumpEncodedArray(context, blendPath, surface.vertInfo.vertsBlend, blendCount);
            if (surface.verts0 && surface.vertCount)
                DumpEncodedArray(context, vertsPath, surface.verts0, surface.vertCount);

            if (surface.vertList && surface.vertListCount)
            {
                const auto vertListFile = context.OpenAssetFile(vertListPath);
                if (vertListFile)
                {
                    for (unsigned listIndex = 0; listIndex < surface.vertListCount; ++listIndex)
                    {
                        const auto& sourceList = surface.vertList[listIndex];
                        noteReusable(sourceList.collisionTree);

                        auto list = sourceList;
                        list.collisionTree =
                            sourceList.collisionTree ? FollowingPtr<XSurfaceCollisionTree>() : nullptr;
                        EndianSwap(list, EndianOperation::Encode);
                        vertListFile->write(
                            reinterpret_cast<const char*>(&list),
                            static_cast<std::streamsize>(sizeof(list)));
                    }
                }

                for (unsigned listIndex = 0; listIndex < surface.vertListCount; ++listIndex)
                {
                    const auto* tree = surface.vertList[listIndex].collisionTree;
                    if (!tree)
                        continue;

                    totalCollisionTrees++;
                    totalCollisionNodes += tree->nodeCount;
                    totalCollisionLeafs += tree->leafCount;

                    const auto treePrefix =
                        std::format("{}/s{:03}.vl{:03}.tree", blobRoot, surfaceIndex, listIndex);
                    const auto treePath = treePrefix + ".bin";
                    const auto nodePath = treePrefix + ".nodes.bin";
                    const auto leafPath = treePrefix + ".leafs.bin";

                    auto treeBody = *tree;
                    treeBody.nodes = tree->nodes ? FollowingPtr<XSurfaceCollisionNode>() : nullptr;
                    treeBody.leafs = tree->leafs ? FollowingPtr<XSurfaceCollisionLeaf>() : nullptr;
                    DumpEncodedStruct(context, treePath, treeBody);

                    if (tree->nodes && tree->nodeCount)
                        DumpEncodedArray(context, nodePath, tree->nodes, tree->nodeCount);
                    if (tree->leafs && tree->leafCount)
                        DumpEncodedArray(context, leafPath, tree->leafs, tree->leafCount);
                }
            }

            if (surface.triIndices && surface.triCount)
                DumpEncodedArray(context, triPath, surface.triIndices, surface.triCount);
        }

        if (model.boneInfo && model.numBones)
            DumpEncodedArray(context, boneInfoPath, model.boneInfo, model.numBones);

        const auto highMipCount =
            model.lodInfo[0].numsurfs;
        if (model.streamInfo.highMipBounds && highMipCount)
            DumpEncodedArray(context, highMipPath, model.streamInfo.highMipBounds, highMipCount);

        auto& out = *file;
        out << "{";
        out << "\"format\":\"WaW360.T4Xenon.XModelRelocatableGraph.v1\"";
        out << ",\"name\":";
        JsonString(out, assetName);
        out << ",\"abi\":{";
        out << "\"xmodel\":" << sizeof(XModel);
        out << ",\"xsurface\":" << sizeof(XSurface);
        out << ",\"xmodelQuat\":" << sizeof(XModelQuat);
        out << ",\"dobjAnimMat\":" << sizeof(DObjAnimMat);
        out << ",\"packedVertex\":" << sizeof(GfxPackedVertex);
        out << ",\"rigidVertList\":" << sizeof(XRigidVertList);
        out << ",\"collisionTree\":" << sizeof(XSurfaceCollisionTree);
        out << ",\"collisionNode\":" << sizeof(XSurfaceCollisionNode);
        out << ",\"collisionLeaf\":" << sizeof(XSurfaceCollisionLeaf);
        out << ",\"triIndex\":" << sizeof(XSurfaceTri16);
        out << ",\"boneInfo\":" << sizeof(XBoneInfo);
        out << ",\"highMipBounds\":" << sizeof(XModelHighMipBounds);
        out << "}";

        out << ",\"offsets\":{";
        out << "\"physPreset\":" << offsetof(XModel, physPreset);
        out << "}";

        out << ",\"counts\":{";
        out << "\"numBones\":" << static_cast<unsigned>(model.numBones);
        out << ",\"numRootBones\":" << static_cast<unsigned>(model.numRootBones);
        out << ",\"nonRootBones\":" << nonRootBones;
        out << ",\"numsurfs\":" << static_cast<unsigned>(model.numsurfs);
        out << ",\"numLods\":" << model.numLods;
        out << ",\"highMipBounds\":" << highMipCount;
        out << ",\"collisionTrees\":" << totalCollisionTrees;
        out << ",\"collisionNodes\":" << totalCollisionNodes;
        out << ",\"collisionLeafs\":" << totalCollisionLeafs;
        out << "}";

        out << ",\"unsupported\":{";
        out << "\"collSurfs\":" << model.numCollSurfs;
        out << ",\"physGeoms\":" << (model.physGeoms ? "true" : "false");
        out << ",\"collmap\":" << (model.collmap ? "true" : "false");
        out << ",\"physConstraints\":" << (model.physConstraints ? "true" : "false");
        out << ",\"count\":" << unsupportedCount;
        out << "}";

        out << ",\"reusable\":{";
        out << "\"uniquePointers\":" << reusablePointers.size();
        out << ",\"aliasCount\":" << reusableAliasCount;
        out << "}";

        out << ",\"rootFile\":";
        JsonString(out, rootPath);

        out << ",\"boneNames\":[";
        for (unsigned i = 0; i < model.numBones; ++i)
        {
            if (i) out << ',';
            std::string_view boneName;
            unsigned scriptString = 0;
            if (model.boneNames)
            {
                scriptString = static_cast<unsigned>(model.boneNames[i]);
                if (scriptString < context.m_zone.m_script_strings.Count())
                    boneName = context.m_zone.m_script_strings[scriptString];
            }

            out << "{\"index\":" << i;
            out << ",\"sourceScriptString\":" << scriptString;
            out << ",\"name\":";
            JsonString(out, boneName);
            out << "}";
        }
        out << "]";

        out << ",\"materials\":[";
        for (unsigned i = 0; i < model.numsurfs; ++i)
        {
            if (i) out << ',';
            const auto* material = model.materialHandles ? model.materialHandles[i] : nullptr;
            if (material && material->info.name)
                JsonString(out, material->info.name);
            else
                out << "null";
        }
        out << "]";

        out << ",\"physPreset\":";
        JsonNullableString(out, model.physPreset ? model.physPreset->name : nullptr);

        WriteFileField(out, "parentListFile", parentPath, model.parentList && nonRootBones);
        WriteFileField(out, "quatsFile", quatsPath, model.quats && nonRootBones);
        WriteFileField(out, "transFile", transPath, model.trans && nonRootBones);
        WriteFileField(out, "partClassificationFile", partPath, model.partClassification && model.numBones);
        WriteFileField(out, "baseMatFile", baseMatPath, model.baseMat && model.numBones);
        WriteFileField(out, "surfacesFile", surfacesPath, model.surfs && model.numsurfs);
        WriteFileField(out, "boneInfoFile", boneInfoPath, model.boneInfo && model.numBones);
        WriteFileField(out, "highMipBoundsFile", highMipPath, model.streamInfo.highMipBounds && highMipCount);

        out << ",\"surfaces\":[";
        for (unsigned surfaceIndex = 0; model.surfs && surfaceIndex < model.numsurfs; ++surfaceIndex)
        {
            if (surfaceIndex) out << ',';
            const auto& surface = model.surfs[surfaceIndex];
            const auto surfacePrefix = std::format("{}/s{:03}", blobRoot, surfaceIndex);
            const auto blendCount = BlendCount(surface.vertInfo);

            out << "{";
            out << "\"index\":" << surfaceIndex;
            out << ",\"vertCount\":" << surface.vertCount;
            out << ",\"triCount\":" << surface.triCount;
            out << ",\"vertListCount\":" << surface.vertListCount;
            out << ",\"vertsBlendCount\":" << blendCount;

            WriteFileField(
                out, "vertsBlendFile", surfacePrefix + ".vertsBlend.bin",
                surface.vertInfo.vertsBlend && blendCount);
            WriteFileField(
                out, "verts0File", surfacePrefix + ".verts0.bin",
                surface.verts0 && surface.vertCount);
            WriteFileField(
                out, "vertListFile", surfacePrefix + ".vertList.bin",
                surface.vertList && surface.vertListCount);
            WriteFileField(
                out, "triIndicesFile", surfacePrefix + ".triIndices.bin",
                surface.triIndices && surface.triCount);

            out << ",\"collisionTrees\":[";
            bool firstTree = true;
            for (unsigned listIndex = 0; surface.vertList && listIndex < surface.vertListCount; ++listIndex)
            {
                const auto* tree = surface.vertList[listIndex].collisionTree;
                if (!tree)
                    continue;

                if (!firstTree) out << ',';
                firstTree = false;
                const auto treePrefix =
                    std::format("{}/s{:03}.vl{:03}.tree", blobRoot, surfaceIndex, listIndex);

                out << "{";
                out << "\"vertListIndex\":" << listIndex;
                out << ",\"nodeCount\":" << tree->nodeCount;
                out << ",\"leafCount\":" << tree->leafCount;
                out << ",\"treeFile\":";
                JsonString(out, treePrefix + ".bin");
                WriteFileField(out, "nodesFile", treePrefix + ".nodes.bin", tree->nodes && tree->nodeCount);
                WriteFileField(out, "leafsFile", treePrefix + ".leafs.bin", tree->leafs && tree->leafCount);
                out << "}";
            }
            out << "]";
            out << "}";
        }
        out << "]";

        out << "}";
    }
}

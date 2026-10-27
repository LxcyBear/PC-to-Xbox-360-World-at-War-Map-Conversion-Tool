#include "XModelGraphInventoryDumperT4Xenon.h"

#include <cstddef>
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

    template <typename T>
    void Track(std::unordered_set<const void*>& set, const T* ptr)
    {
        if (ptr)
            set.emplace(static_cast<const void*>(ptr));
    }

    size_t BlendCount(const XSurfaceVertexInfo& info)
    {
        return static_cast<size_t>(info.vertCount[0]) +
            3uz * static_cast<size_t>(info.vertCount[1]) +
            5uz * static_cast<size_t>(info.vertCount[2]) +
            7uz * static_cast<size_t>(info.vertCount[3]);
    }

    struct PhysSummary
    {
        unsigned lists{};
        unsigned geoms{};
        unsigned brushes{};
        unsigned sides{};
        unsigned brushVerts{};
        unsigned planes{};
        unsigned adjacentBytes{};
    };

    void AccumulatePhys(
        const PhysGeomList* list,
        PhysSummary& summary,
        std::unordered_set<const void*>& reusablePointers)
    {
        if (!list)
            return;

        summary.lists++;
        Track(reusablePointers, list);
        Track(reusablePointers, list->geoms);

        if (!list->geoms)
            return;

        summary.geoms += list->count;
        for (unsigned i = 0; i < list->count; ++i)
        {
            const auto& geom = list->geoms[i];
            if (!geom.brush)
                continue;

            summary.brushes++;
            Track(reusablePointers, geom.brush);

            const auto& brush = *geom.brush;
            summary.sides += brush.numsides;
            summary.brushVerts += brush.numverts;
            summary.planes += brush.numsides;
            summary.adjacentBytes += static_cast<unsigned>(brush.totalEdgeCount);

            Track(reusablePointers, brush.sides);
            Track(reusablePointers, brush.baseAdjacentSide);
            Track(reusablePointers, brush.verts);
            Track(reusablePointers, brush.planes);

            if (brush.sides)
            {
                for (unsigned sideIndex = 0; sideIndex < brush.numsides; ++sideIndex)
                    Track(reusablePointers, brush.sides[sideIndex].plane);
            }
        }
    }
}

namespace xmodel
{
    void GraphInventoryDumperT4Xenon::DumpAsset(
        AssetDumpingContext& context,
        const XAssetInfo<XModel>& asset)
    {
        const auto& model = *asset.Asset();
        const auto assetName = model.name ? std::string_view(model.name) : std::string_view(asset.m_name);
        const auto file = context.OpenAssetFile(
            std::format("xenon_xmodel_graph_inventory/{}.json", SafeStem(assetName)));
        if (!file)
            return;

        unsigned totalVerts = 0;
        unsigned totalTris = 0;
        unsigned totalVertLists = 0;
        unsigned totalBlendEntries = 0;
        unsigned collisionTrees = 0;
        unsigned collisionNodes = 0;
        unsigned collisionLeafs = 0;

        std::unordered_set<const void*> reusablePointers;
        Track(reusablePointers, model.boneNames);
        Track(reusablePointers, model.parentList);
        Track(reusablePointers, model.quats);
        Track(reusablePointers, model.trans);
        Track(reusablePointers, model.partClassification);
        Track(reusablePointers, model.baseMat);
        Track(reusablePointers, model.physGeoms);
        Track(reusablePointers, model.collmap);

        if (model.surfs)
        {
            for (unsigned surfaceIndex = 0; surfaceIndex < model.numsurfs; ++surfaceIndex)
            {
                const auto& surface = model.surfs[surfaceIndex];
                totalVerts += surface.vertCount;
                totalTris += surface.triCount;
                totalVertLists += surface.vertListCount;
                totalBlendEntries += static_cast<unsigned>(BlendCount(surface.vertInfo));

                Track(reusablePointers, surface.verts0);
                Track(reusablePointers, surface.vertInfo.vertsBlend);
                Track(reusablePointers, surface.vertList);
                Track(reusablePointers, surface.triIndices);

                if (surface.vertList)
                {
                    for (unsigned listIndex = 0; listIndex < surface.vertListCount; ++listIndex)
                    {
                        const auto* tree = surface.vertList[listIndex].collisionTree;
                        if (!tree)
                            continue;

                        collisionTrees++;
                        collisionNodes += tree->nodeCount;
                        collisionLeafs += tree->leafCount;
                        Track(reusablePointers, tree);
                        Track(reusablePointers, tree->nodes);
                        Track(reusablePointers, tree->leafs);
                    }
                }
            }
        }

        PhysSummary physGeoms{};
        PhysSummary collmap{};
        AccumulatePhys(model.physGeoms, physGeoms, reusablePointers);
        AccumulatePhys(model.collmap, collmap, reusablePointers);

        auto& out = *file;
        out << "{";
        out << "\"format\":\"WaW360.T4Xenon.XModelGraphInventory.v1\"";
        out << ",\"name\":";
        JsonString(out, model.name);

        out << ",\"sizes\":{";
        out << "\"xmodel\":" << sizeof(XModel);
        out << ",\"xsurface\":" << sizeof(XSurface);
        out << ",\"xrigidVertList\":" << sizeof(XRigidVertList);
        out << ",\"collisionTree\":" << sizeof(XSurfaceCollisionTree);
        out << ",\"collisionNode\":" << sizeof(XSurfaceCollisionNode);
        out << ",\"collisionLeaf\":" << sizeof(XSurfaceCollisionLeaf);
        out << ",\"collSurf\":" << sizeof(XModelCollSurf_s);
        out << ",\"boneInfo\":" << sizeof(XBoneInfo);
        out << ",\"highMipBounds\":" << sizeof(XModelHighMipBounds);
        out << ",\"physGeomList\":" << sizeof(PhysGeomList);
        out << ",\"physGeomInfo\":" << sizeof(PhysGeomInfo);
        out << ",\"brushWrapper\":" << sizeof(BrushWrapper);
        out << ",\"brushSide\":" << sizeof(cbrushside_t);
        out << ",\"plane\":" << sizeof(cplane_s);
        out << "}";

        out << ",\"root\":{";
        out << "\"numBones\":" << static_cast<unsigned>(model.numBones);
        out << ",\"numRootBones\":" << static_cast<unsigned>(model.numRootBones);
        out << ",\"numsurfs\":" << static_cast<unsigned>(model.numsurfs);
        out << ",\"numLods\":" << model.numLods;
        out << ",\"collLod\":" << model.collLod;
        out << ",\"numCollSurfs\":" << model.numCollSurfs;
        out << ",\"contents\":" << model.contents;
        out << ",\"memUsage\":" << model.memUsage;
        out << ",\"flags\":" << static_cast<unsigned>(model.flags);
        out << ",\"lodRampType\":" << static_cast<unsigned>(model.lodRampType);
        out << ",\"hasBoneNames\":" << (model.boneNames ? "true" : "false");
        out << ",\"hasParentList\":" << (model.parentList ? "true" : "false");
        out << ",\"hasQuats\":" << (model.quats ? "true" : "false");
        out << ",\"hasTrans\":" << (model.trans ? "true" : "false");
        out << ",\"hasPartClassification\":" << (model.partClassification ? "true" : "false");
        out << ",\"hasBaseMat\":" << (model.baseMat ? "true" : "false");
        out << ",\"hasSurfs\":" << (model.surfs ? "true" : "false");
        out << ",\"hasMaterialHandles\":" << (model.materialHandles ? "true" : "false");
        out << ",\"hasCollSurfs\":" << (model.collSurfs ? "true" : "false");
        out << ",\"hasBoneInfo\":" << (model.boneInfo ? "true" : "false");
        out << ",\"hasHighMipBounds\":" << (model.streamInfo.highMipBounds ? "true" : "false");
        out << ",\"hasPhysGeoms\":" << (model.physGeoms ? "true" : "false");
        out << ",\"hasCollmap\":" << (model.collmap ? "true" : "false");
        out << "}";

        out << ",\"lods\":[";
        for (unsigned lod = 0; lod < 4; ++lod)
        {
            if (lod) out << ',';
            const auto& info = model.lodInfo[lod];
            out << "{\"index\":" << lod
                << ",\"dist\":" << info.dist
                << ",\"numsurfs\":" << info.numsurfs
                << ",\"surfIndex\":" << info.surfIndex
                << "}";
        }
        out << "]";

        out << ",\"surfaces\":[";
        if (model.surfs)
        {
            for (unsigned surfaceIndex = 0; surfaceIndex < model.numsurfs; ++surfaceIndex)
            {
                if (surfaceIndex) out << ',';
                const auto& surface = model.surfs[surfaceIndex];
                unsigned surfaceTrees = 0;
                unsigned surfaceNodes = 0;
                unsigned surfaceLeafs = 0;

                if (surface.vertList)
                {
                    for (unsigned listIndex = 0; listIndex < surface.vertListCount; ++listIndex)
                    {
                        const auto* tree = surface.vertList[listIndex].collisionTree;
                        if (!tree)
                            continue;
                        surfaceTrees++;
                        surfaceNodes += tree->nodeCount;
                        surfaceLeafs += tree->leafCount;
                    }
                }

                out << "{\"index\":" << surfaceIndex
                    << ",\"tileMode\":" << static_cast<unsigned>(surface.tileMode)
                    << ",\"deformed\":" << (surface.deformed ? "true" : "false")
                    << ",\"vertCount\":" << surface.vertCount
                    << ",\"triCount\":" << surface.triCount
                    << ",\"vertListCount\":" << surface.vertListCount
                    << ",\"vertsBlendCount\":" << BlendCount(surface.vertInfo)
                    << ",\"collisionTrees\":" << surfaceTrees
                    << ",\"collisionNodes\":" << surfaceNodes
                    << ",\"collisionLeafs\":" << surfaceLeafs
                    << "}";
            }
        }
        out << "]";

        out << ",\"materials\":[";
        if (model.materialHandles)
        {
            for (unsigned i = 0; i < model.numsurfs; ++i)
            {
                if (i) out << ',';
                const auto* material = model.materialHandles[i];
                JsonString(out, material ? material->info.name : nullptr);
            }
        }
        out << "]";

        out << ",\"dependencies\":{";
        out << "\"physPreset\":";
        JsonString(out, model.physPreset ? model.physPreset->name : nullptr);
        out << ",\"physConstraints\":";
        JsonString(out, model.physConstraints ? model.physConstraints->name : nullptr);
        out << "}";

        out << ",\"surfaceTotals\":{";
        out << "\"verts\":" << totalVerts;
        out << ",\"tris\":" << totalTris;
        out << ",\"vertLists\":" << totalVertLists;
        out << ",\"blendEntries\":" << totalBlendEntries;
        out << ",\"collisionTrees\":" << collisionTrees;
        out << ",\"collisionNodes\":" << collisionNodes;
        out << ",\"collisionLeafs\":" << collisionLeafs;
        out << "}";

        const auto writePhys = [&out](const char* name, const PhysSummary& value)
        {
            out << ",\"" << name << "\":{";
            out << "\"lists\":" << value.lists;
            out << ",\"geoms\":" << value.geoms;
            out << ",\"brushes\":" << value.brushes;
            out << ",\"sides\":" << value.sides;
            out << ",\"brushVerts\":" << value.brushVerts;
            out << ",\"planes\":" << value.planes;
            out << ",\"adjacentBytes\":" << value.adjacentBytes;
            out << "}";
        };

        writePhys("physGeoms", physGeoms);
        writePhys("collmap", collmap);

        out << ",\"uniqueReusablePointerTargets\":" << reusablePointers.size();
        out << "}";
    }
}

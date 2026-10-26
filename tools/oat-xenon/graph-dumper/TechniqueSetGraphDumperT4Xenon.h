#pragma once

#include "Dumping/AbstractAssetDumper.h"
#include "Game/T4Xenon/T4Xenon.h"

namespace techset
{
    class TechniqueSetGraphDumperT4Xenon final : public AbstractAssetDumper<T4Xenon::AssetTechniqueSet>
    {
    protected:
        void DumpAsset(
            AssetDumpingContext& context,
            const XAssetInfo<T4Xenon::MaterialTechniqueSet>& asset) override;
    };
}

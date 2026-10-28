#pragma once

#include "Dumping/AbstractAssetDumper.h"
#include "Game/T4Xenon/T4Xenon.h"

namespace material
{
    class RelocatableGraphDumperT4Xenon final : public AbstractAssetDumper<T4Xenon::AssetMaterial>
    {
    protected:
        void DumpAsset(
            AssetDumpingContext& context,
            const XAssetInfo<T4Xenon::Material>& asset) override;
    };
}

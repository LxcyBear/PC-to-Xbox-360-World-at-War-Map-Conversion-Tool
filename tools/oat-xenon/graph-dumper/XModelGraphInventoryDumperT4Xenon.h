#pragma once

#include "Dumping/AbstractAssetDumper.h"
#include "Game/T4Xenon/T4Xenon.h"

namespace xmodel
{
    class GraphInventoryDumperT4Xenon final : public AbstractAssetDumper<T4Xenon::AssetXModel>
    {
    protected:
        void DumpAsset(
            AssetDumpingContext& context,
            const XAssetInfo<T4Xenon::XModel>& asset) override;
    };
}

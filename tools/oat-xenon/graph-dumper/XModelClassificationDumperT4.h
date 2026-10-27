#pragma once

#include "Dumping/AbstractAssetDumper.h"
#include "Game/T4/T4.h"

namespace xmodel
{
    class ClassificationDumperT4 final : public AbstractAssetDumper<T4::AssetXModel>
    {
    protected:
        void DumpAsset(
            AssetDumpingContext& context,
            const XAssetInfo<T4::XModel>& asset) override;
    };
}

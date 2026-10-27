#pragma once

#include "Dumping/AbstractAssetDumper.h"
#include "Game/T4Xenon/T4Xenon.h"

namespace image
{
    class ImageGraphDumperT4Xenon final : public AbstractAssetDumper<T4Xenon::AssetImage>
    {
    protected:
        void DumpAsset(
            AssetDumpingContext& context,
            const XAssetInfo<T4Xenon::GfxImage>& asset) override;
    };
}

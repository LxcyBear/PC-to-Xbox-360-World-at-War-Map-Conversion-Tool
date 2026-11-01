#pragma once

#include "Dumping/AbstractAssetDumper.h"
#include "Game/T4Xenon/T4Xenon.h"

namespace gfxworld
{
    // Native Xbox (T4Xenon) independent ABI + loaded Material* witness.
    // Read-only OAT dump, no FF generation or surface patching.
    class SurfaceAbiWitnessT4Xenon final
        : public AbstractAssetDumper<T4Xenon::AssetGfxWorld>
    {
    protected:
        void DumpAsset(
            AssetDumpingContext& context,
            const XAssetInfo<T4Xenon::GfxWorld>& asset) override;
    };
}

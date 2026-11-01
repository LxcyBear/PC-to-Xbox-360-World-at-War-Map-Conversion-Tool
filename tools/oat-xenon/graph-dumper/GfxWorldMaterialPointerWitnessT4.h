#pragma once

#include "Dumping/AbstractAssetDumper.h"
#include "Game/T4/T4.h"

namespace gfxworld
{
    // Diagnostic only. Compare post-OAT-load PC GfxSurface.material pointers
    // against post-OAT-load GfxWorld.materialMemory[i].material pointers.
    // This is a loader observation, not an Xbox GfxWorld writer or FF graft.
    class MaterialPointerWitnessT4 final
        : public AbstractAssetDumper<T4::AssetGfxWorld>
    {
    protected:
        void DumpAsset(
            AssetDumpingContext& context,
            const XAssetInfo<T4::GfxWorld>& asset) override;
    };
}

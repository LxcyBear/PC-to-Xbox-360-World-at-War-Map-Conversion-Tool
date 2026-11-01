#pragma once

#include "Dumping/AbstractAssetDumper.h"
#include "Game/T4/T4.h"

namespace gfxworld
{
    // Emits the 39 ORIGINAL PC world-owned Material headers/tables from the
    // actual fixed-up T4 OAT loader, never from the Xbox donor.
    // Read-only, little-endian source corpus. NOT yet Xenon Material graphs.
    class MaterialCorpusDumperT4 final
        : public AbstractAssetDumper<T4::AssetGfxWorld>
    {
    protected:
        void DumpAsset(
            AssetDumpingContext& context,
            const XAssetInfo<T4::GfxWorld>& asset) override;
    };
}

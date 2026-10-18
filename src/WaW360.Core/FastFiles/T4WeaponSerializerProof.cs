using System.Buffers.Binary;
using System.Text;

namespace WaW360.Core.FastFiles;

public sealed record T4WeaponProofAsset(
    string Name,
    long SourceOffset,
    int FixedBodyBytes,
    int XAnimSlots,
    int GunModelSlots,
    bool HasBounceSoundTable,
    int AccuracyGraphKnotCount,
    bool HasFirstPersonFlameTable,
    bool HasThirdPersonFlameTable);

public sealed class T4WeaponProofResult
{
    public required string SourceFastFile { get; init; }
    public required string OutputFile { get; init; }
    public required int DeclaredWeaponCount { get; init; }
    public required int ResolvedWeaponCount { get; init; }
    public required int SerializedBodyCount { get; init; }
    public required int AliasReferenceCount { get; init; }
    public required int InferredFixedSize { get; init; }
    public required IReadOnlyList<T4WeaponProofAsset> Assets { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required bool Verified { get; init; }
}

/// <summary>
/// Stage 4 alpha 6f: typed T4 WeaponDef fixed-body/dependency serializer proof.
///
/// T4 WeaponDef is unusually large. The serialized fixed body is followed by
/// variable strings/tables and optional flame-table payloads. Alpha 6f proves the
/// fixed PC layout, all 35 XAnim slots, 16 gun-model slots + hand model, script
/// string arrays, and the key owned-payload pointer/count fields. The fixed body
/// is re-emitted Xbox big-endian and independently read back.
///
/// This is an INTERNAL proof container, not a game fastfile. Variable owned
/// payloads are inventoried here and are deliberately left for the next writer
/// integration step rather than guessed.
/// </summary>
public static class T4WeaponSerializerProof
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("W360A6F\0");
    private const uint Following = 0xFFFFFFFFu;
    private const uint Insert = 0xFFFFFFFEu;
    // 32-bit T4 WeaponDef fixed body (through tagFlash_preparationEffect).
    // Cross-checked against the current T4 asset layout and a real WaW PC zone.
    private const int FixedBodySize = 0x9AC;
    private const string ProofReuseSchema = "T4WeaponFixed-v7-alpha9h-1";

    // Fixed offsets in T4 WeaponDef (32-bit) before the large scalar body.
    private const int InternalName = 0;
    private const int DisplayName = 4;
    private const int OverlayName = 8;
    private const int GunModels = 12;          // 16 pointers
    private const int HandModel = 76;
    private const int XAnims = 80;             // 35 pointers
    private const int ModeName = 220;
    private const int HideTags = 224;          // 8 x uint16
    private const int NoteKeys = 240;          // 20 x uint16
    private const int NoteValues = 280;        // 20 x uint16
    private const int PlayerAnimType = 320;
    private const int WeapType = 324;
    private const int WeapClass = 328;
    private const int PenetrateType = 332;
    private const int ImpactType = 336;
    private const int InventoryType = 340;
    private const int FireType = 344;
    private const int ClipType = 348;
    private const int OverheatWeapon = 352;
    private const int CoolWhileFiring = 368;
    private const int OffhandClass = 372;
    private const int Stance = 376;

    // These offsets are discovered relative to the inferred fixed size by
    // scanning the body for the OAT-defined ownership signature. They are used
    // only for diagnostics; the fixed body endian transform itself does not
    // depend on them.

    public static async Task<T4WeaponProofResult> BuildProofAsync(
        string sourceFastFile,
        string outputFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFastFile))
            throw new FileNotFoundException("Fastfile not found.", sourceFastFile);

        var tempDir = Path.Combine(Path.GetTempPath(), "WaW360Converter", "weapon_a6f", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zonePath = Path.Combine(tempDir, "zone.bin");

        try
        {
            var cachedZone = await FastFileAnalysisCache.GetZoneAsync(sourceFastFile, cancellationToken);
            if (cachedZone.Platform != WaWPlatform.PC || cachedZone.ZoneHeader.Endianness != Endian.Little)
                throw new InvalidDataException("Alpha 6f Weapon proof expects a PC/little-endian WaW source fastfile.");

            var zone = cachedZone.Bytes;
            var document = cachedZone.Document;
            var declared = document.Assets.Count(a => a.TypeName.Equals("weapon", StringComparison.OrdinalIgnoreCase));
            if (declared == 0)
                throw new InvalidDataException("Source fastfile declares no WeaponDef assets.");

            var inferred = FixedBodySize;
            var weaponEntries = document.Assets
                .Where(a => a.TypeName.Equals("weapon", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var nullHeaders = weaponEntries.Where(a => a.ReferenceKind == T4AssetReferenceKind.Null).ToList();
            if (nullHeaders.Count > 0)
                throw new InvalidDataException($"Weapon XAsset table contains {nullHeaders.Count:N0} null header reference(s); Alpha 6f refuses to classify them as valid weapons.");

            var inlineHeaderCount = weaponEntries.Count(a => a.ReferenceKind is T4AssetReferenceKind.Inline or T4AssetReferenceKind.InlineShared);
            var aliasEntries = weaponEntries.Where(a => a.ReferenceKind == T4AssetReferenceKind.OffsetOrAlias).ToList();
            foreach (var entry in aliasEntries)
            {
                if (!IsSerializedPointerWithin(entry.RawHeader, document.Header) || entry.RawHeader == 0 || IsInline(entry.RawHeader))
                    throw new InvalidDataException($"Weapon XAsset #{entry.Index} has invalid alias header 0x{entry.RawHeader:X8}.");
            }

            var rawCandidates = FindFixedBodyCandidates(zone, document.Header, document.ScriptStringCount);
            var inlineNamed = new List<(int Offset, string Name)>();
            var packedNamed = new List<(int Offset, uint NamePointer)>();

            foreach (var o in rawCandidates.Distinct().OrderBy(x => x))
            {
                var namePointer = ReadU32(zone, o + InternalName, Endian.Little);
                if (IsInline(namePointer))
                {
                    if (TryResolveInlineNameAt(zone, o, inferred, out var name))
                        inlineNamed.Add((o, name));
                    continue;
                }

                var decoded = T4ZonePointer.Decode(namePointer);
                if (decoded.Kind == T4ZonePointerKind.Offset && decoded.Block == T4ZoneBlock.Virtual)
                    packedNamed.Add((o, namePointer));
            }

            if (inlineNamed.Count > inlineHeaderCount)
                throw new InvalidDataException(
                    $"Weapon XAsset table requires {inlineHeaderCount:N0} physical inline/shared WeaponDef body(ies), but typed discovery found {inlineNamed.Count:N0} inline-named bodies. " +
                    "Alpha 6f refuses an ambiguous body set.");

            var missingPhysicalBodies = inlineHeaderCount - inlineNamed.Count;

            // A true top-level WeaponDef body cannot physically overlap another proven
            // 0x9AC WeaponDef body. The old byte-wise scanner produced many shifted
            // windows only four bytes apart inside the same real structure. Remove any
            // packed-name candidate whose fixed interval intersects an already-proven
            // inline-name body before making any coverage decision.
            static bool OverlapsFixedBody(int a, int b) =>
                (long)a < (long)b + FixedBodySize && (long)b < (long)a + FixedBodySize;

            var packedNonOverlappingInline = packedNamed
                .Where(p => !inlineNamed.Any(i => OverlapsFixedBody(p.Offset, i.Offset)))
                .OrderBy(p => p.Offset)
                .ToList();

            // Collapse the remaining packed candidates into physical overlap clusters.
            // One real 0x9AC body can contribute multiple shifted scanner hits, but a
            // cluster can represent at most one physical WeaponDef. Alpha 6f never
            // chooses a start inside a multi-hit cluster; only singleton clusters can
            // satisfy the exact XAsset-declared body deficit.
            var packedClusters = new List<List<(int Offset, uint NamePointer)>>();
            foreach (var candidate in packedNonOverlappingInline)
            {
                var cluster = packedClusters.LastOrDefault(c => c.Any(x => OverlapsFixedBody(x.Offset, candidate.Offset)));
                if (cluster is null)
                {
                    cluster = new List<(int Offset, uint NamePointer)>();
                    packedClusters.Add(cluster);
                }
                cluster.Add(candidate);
            }

            var uniquePacked = packedClusters.Where(c => c.Count == 1).Select(c => c[0]).ToList();
            var ambiguousClusters = packedClusters.Where(c => c.Count > 1).ToList();
            var selectedPackedBodies = new List<(int Offset, uint NamePointer)>();
            var quarantinedAmbiguousClusters = new List<List<(int Offset, uint NamePointer)>>();
            if (missingPhysicalBodies > 0)
            {
                // Alpha 6f selection rule:
                // - never choose a start from an ambiguous overlap cluster;
                // - accept the uniquely identified packed bodies only when they EXACTLY
                //   fill the body deficit declared by the weapon XAsset table;
                // - any remaining multi-hit clusters are quarantined as scanner noise.
                //
                // This is stricter than simply taking the first N hits. If the unique set
                // is short OR has surplus singleton bodies, the proof still fails closed.
                if (uniquePacked.Count != missingPhysicalBodies)
                {
                    var diagItems = packedClusters.Take(12).Select(c =>
                        c.Count == 1
                            ? $"0x{c[0].Offset:X}:namePtr=0x{c[0].NamePointer:X8}"
                            : $"cluster[{c.Count}]=" + string.Join("/", c.Take(5).Select(x => $"0x{x.Offset:X}")));
                    var diag = string.Join(", ", diagItems);
                    if (packedClusters.Count > 12) diag += $", ... +{packedClusters.Count - 12:N0} more cluster(s)";
                    throw new InvalidDataException(
                        $"Weapon XAsset table requires {inlineHeaderCount:N0} physical body(ies). Alpha 6f proved {inlineNamed.Count:N0} inline-name body(ies), leaving {missingPhysicalBodies:N0}. " +
                        $"After full fixed-body validation and removal of candidates overlapping proven bodies, {packedNonOverlappingInline.Count:N0} packed hit(s) remain in {packedClusters.Count:N0} physical cluster(s): " +
                        $"{uniquePacked.Count:N0} unique and {ambiguousClusters.Count:N0} ambiguous. The unique set does not exactly fill the XAsset body deficit, so Alpha 6f still refuses to guess. Diagnostics: {(diag.Length == 0 ? "<none>" : diag)}");
                }

                selectedPackedBodies.AddRange(uniquePacked);
                quarantinedAmbiguousClusters.AddRange(ambiguousClusters);
            }

            var resolvedBodies = inlineNamed
                .Select(x => (Offset: x.Offset, Name: x.Name))
                .Concat(selectedPackedBodies.Select(x => (Offset: x.Offset, Name: $"<packed-name@0x{x.Offset:X}:0x{x.NamePointer:X8}>")))
                .OrderBy(x => x.Offset)
                .ToList();

            if (resolvedBodies.Count != inlineHeaderCount)
                throw new InvalidDataException($"Weapon physical body coverage mismatch: {resolvedBodies.Count:N0}/{inlineHeaderCount:N0} required inline/shared bodies.");
            if (resolvedBodies.Count + aliasEntries.Count != declared)
                throw new InvalidDataException($"Weapon top-level coverage mismatch: {resolvedBodies.Count:N0} physical bodies + {aliasEntries.Count:N0} aliases != {declared:N0} declared weapons.");

            var warnings = new List<string>();
            if (selectedPackedBodies.Count > 0)
                warnings.Add($"{selectedPackedBodies.Count:N0} physical WeaponDef body(ies) use a packed internal-name pointer; their proof labels are synthetic because Alpha 6f does not resolve block-local name aliases to strings.");
            if (quarantinedAmbiguousClusters.Count > 0)
            {
                var refs = string.Join(", ", quarantinedAmbiguousClusters.Take(8).Select(c =>
                    "[" + string.Join("/", c.Take(6).Select(x => $"0x{x.Offset:X}")) + "]"));
                if (quarantinedAmbiguousClusters.Count > 8)
                    refs += $", ... +{quarantinedAmbiguousClusters.Count - 8:N0} more";
                warnings.Add(
                    $"Alpha 6f quarantined {quarantinedAmbiguousClusters.Count:N0} surplus ambiguous scanner cluster(s) instead of choosing a start inside them. " +
                    $"The {selectedPackedBodies.Count:N0} uniquely identified packed body(ies) already exactly filled the XAsset deficit of {missingPhysicalBodies:N0}. Quarantined: {refs}");
            }
            if (aliasEntries.Count > 0)
            {
                var refs = string.Join(", ", aliasEntries.Take(12).Select(a => $"#{a.Index}=0x{a.RawHeader:X8}"));
                if (aliasEntries.Count > 12) refs += $", ... +{aliasEntries.Count - 12:N0} more";
                warnings.Add($"{aliasEntries.Count:N0} top-level weapon XAsset(s) are packed offset/alias references and correctly require no fresh WeaponDef body in this FF. Headers: {refs}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);
            var assets = new List<T4WeaponProofAsset>(resolvedBodies.Count);

            await using (var fs = File.Create(outputFile))
            {
                await fs.WriteAsync(Magic, cancellationToken);
                WriteU32(fs, 7);
                WriteU32(fs, checked((uint)resolvedBodies.Count));
                WriteU32(fs, checked((uint)inferred));

                foreach (var item in resolvedBodies)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = zone.AsSpan(item.Offset, inferred).ToArray();
                    var xbox = TransformFixedBodyToBigEndian(source);
                    VerifyBigEndianFixedBody(xbox, item.Name);

                    WriteU32(fs, checked((uint)item.Offset));
                    WriteU32(fs, checked((uint)xbox.Length));
                    WriteU32(fs, checked((uint)Encoding.UTF8.GetByteCount(item.Name)));
                    await fs.WriteAsync(Encoding.UTF8.GetBytes(item.Name), cancellationToken);
                    await fs.WriteAsync(xbox, cancellationToken);

                    var diag = InspectVariableOwnershipHints(source, Endian.Little);
                    assets.Add(new T4WeaponProofAsset(
                        item.Name,
                        item.Offset,
                        inferred,
                        35,
                        16,
                        diag.HasBounceSoundTable,
                        diag.AccuracyKnots,
                        diag.HasFpFlame,
                        diag.HasTpFlame));
                }
            }

            if (!VerifyContainer(outputFile, assets.Count, inferred))
                throw new InvalidDataException("Stage 4 alpha 6f Weapon proof container failed read-back verification.");

            // Alpha 9h: stamp the verified fixed proof with the exact compressed-source
            // SHA-256 and an explicit proof schema. Alpha 7b may reuse the proof only
            // when both match, so a stale proof can never silently cross a source or
            // algorithm change. The proof itself remains independently verified above.
            var sourceHash = await FastFileAnalysisCache.GetSourceSha256Async(sourceFastFile, cancellationToken);
            var proofHash = await FastFileAnalysisCache.GetSourceSha256Async(outputFile, cancellationToken);
            await File.WriteAllTextAsync(
                outputFile + ".source.sha256",
                $"{ProofReuseSchema}|{sourceHash}|{proofHash}",
                Encoding.ASCII,
                cancellationToken);

            return new T4WeaponProofResult
            {
                SourceFastFile = sourceFastFile,
                OutputFile = outputFile,
                DeclaredWeaponCount = declared,
                ResolvedWeaponCount = assets.Count + aliasEntries.Count,
                SerializedBodyCount = assets.Count,
                AliasReferenceCount = aliasEntries.Count,
                InferredFixedSize = inferred,
                Assets = assets,
                Warnings = warnings,
                Verified = true
            };
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Alpha 9h fast-iteration bridge. Reuses an already VERIFIED Alpha 6f proof
    /// only when its proof schema and exact compressed-source SHA-256 match.
    /// Returns null on any mismatch/corruption so callers safely rebuild instead.
    /// </summary>
    public static async Task<T4WeaponProofResult?> TryLoadVerifiedProofAsync(
        string sourceFastFile,
        string proofFile,
        int declaredWeaponCount,
        int aliasReferenceCount,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(sourceFastFile) || !File.Exists(proofFile))
                return null;

            var stampPath = proofFile + ".source.sha256";
            if (!File.Exists(stampPath))
                return null;

            var expectedHash = await FastFileAnalysisCache.GetSourceSha256Async(sourceFastFile, cancellationToken);
            var proofHash = await FastFileAnalysisCache.GetSourceSha256Async(proofFile, cancellationToken);
            var expectedStamp = $"{ProofReuseSchema}|{expectedHash}|{proofHash}";
            var actualStamp = (await File.ReadAllTextAsync(stampPath, cancellationToken)).Trim();
            if (!actualStamp.Equals(expectedStamp, StringComparison.Ordinal))
                return null;

            var data = await File.ReadAllBytesAsync(proofFile, cancellationToken);
            if (data.Length < 20 || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
                return null;

            var p = Magic.Length;
            if (ReadBe32(data, ref p) != 7) return null;
            var physicalCount = checked((int)ReadBe32(data, ref p));
            var fixedSize = checked((int)ReadBe32(data, ref p));
            if (fixedSize != FixedBodySize) return null;
            if (physicalCount + aliasReferenceCount != declaredWeaponCount) return null;

            var assets = new List<T4WeaponProofAsset>(physicalCount);
            for (var i = 0; i < physicalCount; i++)
            {
                var sourceOffset = checked((long)ReadBe32(data, ref p));
                var bodyLen = checked((int)ReadBe32(data, ref p));
                var nameLen = checked((int)ReadBe32(data, ref p));
                if (bodyLen != FixedBodySize || nameLen < 0 || p + nameLen + bodyLen > data.Length)
                    return null;

                var name = Encoding.UTF8.GetString(data, p, nameLen);
                p += nameLen;
                // Body bytes were already independently verified when Alpha 6f wrote
                // this container. Consume them here; Alpha 7b independently validates
                // its own full owned-payload output after reuse.
                p += bodyLen;

                assets.Add(new T4WeaponProofAsset(
                    name, sourceOffset, FixedBodySize, 35, 16, false, 0, false, false));
            }

            if (p != data.Length || !VerifyContainer(proofFile, physicalCount, fixedSize))
                return null;

            return new T4WeaponProofResult
            {
                SourceFastFile = sourceFastFile,
                OutputFile = proofFile,
                DeclaredWeaponCount = declaredWeaponCount,
                ResolvedWeaponCount = physicalCount + aliasReferenceCount,
                SerializedBodyCount = physicalCount,
                AliasReferenceCount = aliasReferenceCount,
                InferredFixedSize = fixedSize,
                Assets = assets,
                Warnings = new[]
                {
                    "Alpha 9h reused a SHA-256/schema-matched, read-back-verified Alpha 6f fixed proof; no WeaponDef body rediscovery was performed."
                },
                Verified = true
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Reuse is an optimization only. Any malformed/stale cache artifact falls
            // back to the normal Alpha 6f proof builder instead of weakening safety.
            return null;
        }
    }

    private static List<int> FindFixedBodyCandidates(byte[] zone, ZoneHeaderSummary header, int scriptStringCount)
    {
        var list = new List<int>();
        for (var o = 0; o <= zone.Length - FixedBodySize; o++)
        {
            // Check constrained scalar fields first. This keeps a byte-wise scan of a
            // large map fast and rejects random geometry/texture data before touching
            // the large dependency arrays.
            if (!InRange(ReadI32(zone, o + WeapType, Endian.Little), 0, 6) ||
                !InRange(ReadI32(zone, o + WeapClass, Endian.Little), 0, 10) ||
                !InRange(ReadI32(zone, o + PenetrateType, Endian.Little), 0, 3) ||
                !InRange(ReadI32(zone, o + ImpactType, Endian.Little), 0, 11) ||
                !InRange(ReadI32(zone, o + InventoryType, Endian.Little), 0, 3) ||
                !InRange(ReadI32(zone, o + FireType, Endian.Little), 0, 4) ||
                !InRange(ReadI32(zone, o + ClipType, Endian.Little), 0, 5) ||
                !InRange(ReadI32(zone, o + OffhandClass, Endian.Little), 0, 3) ||
                !InRange(ReadI32(zone, o + Stance, Endian.Little), 0, 2))
                continue;

            if (!IsZeroOne(ReadI32(zone, o + OverheatWeapon, Endian.Little)) ||
                !IsZeroOne(ReadI32(zone, o + CoolWhileFiring, Endian.Little)))
                continue;

            // scr_string_t is a 16-bit index into this zone's script-string table.
            // Shifted false-positive windows often look pointer-plausible but expose
            // impossible 16-bit values here. Validate all three fixed arrays against
            // the exact XAssetList script-string count before accepting the body.
            if (!ScriptStringArrayWithin(zone, o + HideTags, 8, scriptStringCount) ||
                !ScriptStringArrayWithin(zone, o + NoteKeys, 20, scriptStringCount) ||
                !ScriptStringArrayWithin(zone, o + NoteValues, 20, scriptStringCount))
                continue;

            // Late T4 WeaponDef invariants. These offsets are from the same current
            // 32-bit T4 layout whose final field ends at 0x9AC.
            if (!InRange(ReadI32(zone, o + 688, Endian.Little), 0, 2) ||       // activeReticleType
                !InRange(ReadI32(zone, o + 988, Endian.Little), 0, 2) ||       // hudIconRatio
                !InRange(ReadI32(zone, o + 996, Endian.Little), 0, 2) ||       // ammoCounterIconRatio
                !InRange(ReadI32(zone, o + 1000, Endian.Little), 0, 6) ||      // ammoCounterClip
                !InRange(ReadI32(zone, o + 1316, Endian.Little), 0, 1) ||      // overlayReticle
                !InRange(ReadI32(zone, o + 1320, Endian.Little), 0, 2) ||      // overlayInterface
                !InRange(ReadI32(zone, o + 1556, Endian.Little), 0, 2) ||      // killIconRatio
                !InRange(ReadI32(zone, o + 1568, Endian.Little), 0, 2) ||      // dpadIconRatio
                !InRange(ReadI32(zone, o + 1668, Endian.Little), 0, 8) ||      // projExplosion
                !InRange(ReadI32(zone, o + 1704, Endian.Little), 0, 3) ||      // stickiness
                !InRange(ReadI32(zone, o + 1996, Endian.Little), 0, 4))        // guidedMissileType
                continue;

            var boolOffsets = new[]
            {
                1244, 1248, 1252, 1268, 1280,
                1484, 1488, 1492, 1496, 1500, 1512, 1516, 1520, 1524,
                1528, 1532, 1536, 1540, 1544, 1560, 1572, 1576, 1580,
                1608, 1612, 1676, 1700, 1708, 1712, 1716, 1720, 1724
            };
            var scalarsOk = true;
            foreach (var rel in boolOffsets)
            {
                if (!IsZeroOne(ReadI32(zone, o + rel, Endian.Little)))
                {
                    scalarsOk = false;
                    break;
                }
            }
            if (!scalarsOk) continue;

            for (var rel = 2200; rel <= 2212; rel += 4)
            {
                if (!InRange(ReadI32(zone, o + rel, Endian.Little), 0, 4096))
                {
                    scalarsOk = false;
                    break;
                }
            }
            if (!scalarsOk) continue;

            var internalName = ReadU32(zone, o + InternalName, Endian.Little);
            if (internalName == 0 || !IsWeaponInternalNamePointer(internalName, header)) continue;
            if (!IsSerializedPointerWithin(ReadU32(zone, o + DisplayName, Endian.Little), header)) continue;
            if (!IsSerializedPointerWithin(ReadU32(zone, o + OverlayName, Endian.Little), header)) continue;

            var depsOk = true;
            for (var i = 0; i < 16 && depsOk; i++)
                depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + GunModels + i * 4, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + HandModel, Endian.Little), header);

            var nonNullXAnims = 0;
            for (var i = 0; i < 35 && depsOk; i++)
            {
                var ptr = ReadU32(zone, o + XAnims + i * 4, Endian.Little);
                depsOk &= IsSerializedPointerWithin(ptr, header);
                if (ptr != 0) nonNullXAnims++;
            }
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + ModeName, Endian.Little), header);
            if (!depsOk) continue;

            // String fields are the decisive body-start invariant for packed-name weapons.
            // T4 strings live in the VIRTUAL arena (or serialize inline/null). A shifted
            // +4/+8/+12 window can look pointer-plausible at the front of WeaponDef, but
            // later string slots would land on ordinary integer/float fields. Validate
            // string ownership across the whole 0x9AC body before accepting a start.
            var stringOffsets = new[]
            {
                DisplayName, OverlayName, ModeName,
                628, 632, 636,       // stand/crouch/proneMountedWeapdef
                1008, 1016, 1040,    // ammo, clip, shared-ammo-cap names
                1592,                // alt weapon name
                2176, 2180,          // accuracy graph names
                2288, 2292, 2312,    // use hint, drop hint, script
                2428, 2432,          // rumble names
                2452, 2456           // flame-table names
            };
            foreach (var rel in stringOffsets)
            {
                if (!IsOptionalVirtualStringPointer(ReadU32(zone, o + rel, Endian.Little), header))
                {
                    depsOk = false;
                    break;
                }
            }
            if (!depsOk) continue;

            // The pointer-dense sound/effect/model region is also fixed-layout. Validate
            // the pointer fields around the mounted-weapon boundary and major later
            // dependencies so shifted windows cannot survive merely because their first
            // words happen to decode as legal packed values.
            for (var rel = 380; rel <= 636 && depsOk; rel += 4)
                depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + rel, Endian.Little), header);
            for (var rel = 652; rel <= 672 && depsOk; rel += 4)
                depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + rel, Endian.Little), header);
            for (var rel = 900; rel <= 984 && depsOk; rel += 4)
                depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + rel, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 992, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 1308, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 1312, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 1552, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 1564, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 1664, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 1672, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 1680, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 1976, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 2008, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 2184, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 2188, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 2192, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 2196, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 2460, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 2464, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 2468, Endian.Little), header);
            depsOk &= IsSerializedPointerWithin(ReadU32(zone, o + 2472, Endian.Little), header);
            if (!depsOk) continue;

            // Real T4 WeaponDefs populate the animation table densely (the slots are
            // part of the fixed definition even when several reuse the same alias).
            // Requiring a substantial populated table is a strong discriminator from
            // coincidental all-zero/FFFFFFFF binary regions, while still allowing
            // custom weapons to omit a subset of animations.
            if (nonNullXAnims < 20) continue;

            list.Add(o);
        }
        return list;
    }

    private static bool TryResolveInlineNameAt(byte[] zone, int start, int fixedSize, out string name)
    {
        name = string.Empty;
        if (!IsInline(ReadU32(zone, start + InternalName, Endian.Little))) return false;
        var nameOffset = start + fixedSize;
        if (start < 0 || nameOffset >= zone.Length) return false;
        if (nameOffset > 0 && IsWeaponNameChar((char)zone[nameOffset - 1])) return false;
        if (!TryReadCString(zone, nameOffset, 96, out var n, out _)) return false;
        if (!LooksLikeWeaponName(n)) return false;
        name = n;
        return true;
    }

    private static bool IsWeaponInternalNamePointer(uint raw, ZoneHeaderSummary header)
    {
        if (IsInline(raw)) return true;
        var decoded = T4ZonePointer.Decode(raw);
        return decoded.Kind == T4ZonePointerKind.Offset &&
               decoded.Block == T4ZoneBlock.Virtual &&
               decoded.Offset < header.VirtualBlockSize;
    }

    private static bool IsOptionalVirtualStringPointer(uint raw, ZoneHeaderSummary header)
    {
        if (raw == 0 || IsInline(raw)) return true;
        var decoded = T4ZonePointer.Decode(raw);
        return decoded.Kind == T4ZonePointerKind.Offset &&
               decoded.Block == T4ZoneBlock.Virtual &&
               decoded.Offset < header.VirtualBlockSize;
    }

    private static bool IsSerializedPointerWithin(uint raw, ZoneHeaderSummary header)
    {
        if (raw == 0 || IsInline(raw)) return true;
        var decoded = T4ZonePointer.Decode(raw);
        if (decoded.Kind != T4ZonePointerKind.Offset || decoded.Block is null) return false;
        var block = (int)decoded.Block.Value;
        return block >= 0 && block < header.BlockSizes.Count && decoded.Offset < header.BlockSizes[block];
    }

    private static bool ScriptStringArrayWithin(byte[] zone, int offset, int count, int scriptStringCount)
    {
        if (scriptStringCount < 0) return false;
        for (var i = 0; i < count; i++)
        {
            var o = offset + i * 2;
            if (o < 0 || o + 2 > zone.Length) return false;
            var value = BinaryPrimitives.ReadUInt16LittleEndian(zone.AsSpan(o, 2));
            if (value > scriptStringCount) return false;
        }
        return true;
    }

    private static bool InRange(int value, int min, int max) => value >= min && value <= max;
    private static bool IsZeroOne(int value) => value is 0 or 1;

    private static byte[] TransformFixedBodyToBigEndian(byte[] src)
    {
        if ((src.Length & 3) != 0) throw new InvalidDataException("WeaponDef fixed body is not 4-byte aligned.");
        var dst = src.ToArray();

        // Everything in WeaponDef is 32-bit aligned/scalar except the three
        // ScriptString arrays. Swap all 32-bit words, then repair those arrays
        // as 16-bit values from the original source.
        for (var o = 0; o < dst.Length; o += 4)
        {
            dst[o] = src[o + 3]; dst[o + 1] = src[o + 2]; dst[o + 2] = src[o + 1]; dst[o + 3] = src[o];
        }

        SwapU16Range(src, dst, HideTags, 8);
        SwapU16Range(src, dst, NoteKeys, 20);
        SwapU16Range(src, dst, NoteValues, 20);
        return dst;
    }

    private static void SwapU16Range(byte[] src, byte[] dst, int offset, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var o = offset + i * 2;
            dst[o] = src[o + 1];
            dst[o + 1] = src[o];
        }
    }

    private static void VerifyBigEndianFixedBody(byte[] body, string name)
    {
        if (!IsSerializedPointer(ReadU32(body, InternalName, Endian.Big)) || ReadU32(body, InternalName, Endian.Big) == 0)
            throw new InvalidDataException($"Weapon '{name}' big-endian read-back lost its internal-name pointer.");
        if (ReadI32(body, WeapType, Endian.Big) is < 0 or > 6)
            throw new InvalidDataException($"Weapon '{name}' big-endian read-back has an invalid weapType.");
        if (ReadI32(body, WeapClass, Endian.Big) is < 0 or > 10)
            throw new InvalidDataException($"Weapon '{name}' big-endian read-back has an invalid weapClass.");
        for (var i = 0; i < 35; i++)
            if (!IsSerializedPointer(ReadU32(body, XAnims + i * 4, Endian.Big)))
                throw new InvalidDataException($"Weapon '{name}' big-endian read-back has an invalid XAnim pointer slot {i}.");
    }

    private sealed record OwnershipHints(bool HasBounceSoundTable, int AccuracyKnots, bool HasFpFlame, bool HasTpFlame);

    private static OwnershipHints InspectVariableOwnershipHints(byte[] fixedBody, Endian endian)
    {
        var bounce = IsInline(ReadU32(fixedBody, 624, endian));
        var knots = 0;
        var knotPointers = new[] { 2184, 2188, 2192, 2196 };
        var knotCounts = new[] { 2200, 2204, 2208, 2212 };
        for (var i = 0; i < knotPointers.Length; i++)
        {
            if (IsInline(ReadU32(fixedBody, knotPointers[i], endian)))
                knots += Math.Max(0, ReadI32(fixedBody, knotCounts[i], endian));
        }
        var fp = IsInline(ReadU32(fixedBody, 2460, endian));
        var tp = IsInline(ReadU32(fixedBody, 2464, endian));
        return new OwnershipHints(bounce, knots, fp, tp);
    }

    private static bool VerifyContainer(string path, int expectedCount, int expectedFixed)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 20 || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return false;
        var p = Magic.Length;
        if (ReadBe32(data, ref p) != 7) return false;
        if (ReadBe32(data, ref p) != (uint)expectedCount) return false;
        if (ReadBe32(data, ref p) != (uint)expectedFixed) return false;
        for (var i = 0; i < expectedCount; i++)
        {
            _ = ReadBe32(data, ref p);
            var len = checked((int)ReadBe32(data, ref p));
            var nameLen = checked((int)ReadBe32(data, ref p));
            if (p + nameLen + len > data.Length) return false;
            p += nameLen + len;
        }
        return p == data.Length;
    }

    private static uint ReadBe32(byte[] b, ref int p)
    {
        if (p + 4 > b.Length) throw new InvalidDataException("Unexpected end of proof container.");
        var v = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p, 4)); p += 4; return v;
    }

    private static void WriteU32(Stream s, uint value)
    {
        Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, value); s.Write(b);
    }

    private static uint ReadU32(byte[] b, int o, Endian e) =>
        e == Endian.Big ? BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o, 4)) : BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o, 4));
    private static int ReadI32(byte[] b, int o, Endian e) => unchecked((int)ReadU32(b, o, e));
    private static bool IsInline(uint p) => p is Following or Insert;
    private static bool IsSerializedPointer(uint p)
    {
        if (p == 0 || IsInline(p)) return true;
        var decoded = T4ZonePointer.Decode(p);
        return decoded.Kind == T4ZonePointerKind.Offset && decoded.Block is not null;
    }

    private static bool TryReadCString(byte[] data, int start, int maxLen, out string value, out int end)
    {
        value = string.Empty; end = start;
        if (start < 0 || start >= data.Length) return false;
        var limit = Math.Min(data.Length, start + maxLen);
        var i = start;
        for (; i < limit && data[i] != 0; i++)
        {
            var c = data[i];
            if (c < 0x20 || c > 0x7E) return false;
        }
        if (i >= limit || data[i] != 0 || i == start) return false;
        value = Encoding.ASCII.GetString(data, start, i - start);
        end = i + 1;
        return true;
    }

    private static bool LooksLikeWeaponName(string s)
    {
        if (s.Length is < 3 or > 80) return false;
        if (!char.IsLetterOrDigit(s[0]) && s[0] != '_') return false;
        var hasLetter = false;
        foreach (var ch in s)
        {
            if (char.IsUpper(ch)) return false; // WaW internal weapon keys are lowercase; fail closed on a new form.
            if (char.IsLetter(ch)) hasLetter = true;
            if (!IsWeaponNameChar(ch)) return false;
        }
        return hasLetter && !s.Contains(' ');
    }

    private static bool IsWeaponNameChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or '$';
}

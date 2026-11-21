using System.Security.Cryptography;
using Trisat.Core.Models;
using Trisat.Core.Parsers;
using Trisat.Core.Services;

namespace Trisat.Core.Engines;

/// <summary>
/// Live Galileo OSNMA Subframe Assembler & Cryptographic Verifier (clean-room implementation per
/// Galileo OSNMA SIS ICD v1.1 & RX Guidelines v1.3).
///
/// Capabilities:
///   1. Assembles 30-second OSNMA subframes (15 x 2s I/NAV pages -> 120-bit HKROOT + 480-bit MACK) per satellite.
///   2. Parses NMA Header (NMAS, CID, CPKS) and reassembles multi-block DSM-KROOT messages over the air.
///   3. Supports fast cold-start via official EUSPA XML keys or open-source TRISAT-Assist bootstrap material.
///   4. Verifies DSM-KROOT ECDSA P-256 signatures, SHA-256 padding P_DK, TESLA key chains (guarded by GstClockService),
///      and MACK HMAC-SHA-256 tags (ADKD=0 & ADKD=4).
///   5. Incorporates 2-second Bitwise cross-check verdicts from TRISAT-Assist while preserving strict provenance honesty.
/// </summary>
public sealed class OsnmaLiveVerifier
{
    private readonly object _sync = new();
    private readonly GstClockService _gstClock;

    // Trusted cryptographic anchors
    private string? _merkleRootHex;
    private int? _publicKeyId;
    private string? _publicKeyCompressedHex;

    // Verified active TESLA chain parameters
    private bool _hasVerifiedKroot;
    private bool _isAssistedBootstrap;
    private string? _krootHex;
    private string? _alphaHex;
    private int _wnKroot;
    private int _tow0Kroot;
    private int _keySizeBits = 128;
    private int _tagSizeBits = 40;
    private int _macltId = 34;
    private string? _dsmKrootHex;
    private string? _latestTeslaKeyHex;
    private OsnmaNmaHeader? _latestNmaHeader;

    // Subframe assembly buffers keyed by (Svid, SubframeStartTotalSeconds)
    private readonly Dictionary<(int Svid, long SubframeGstStart), SubframeBuffer> _subframes = new();

    // DSM-KROOT multi-block reassembly keyed by DSM_ID (12..15)
    private readonly Dictionary<int, Dictionary<int, byte[]>> _dsmKrootBlocks = new();

    // Per-satellite live telemetry & verification counters (E01..E36)
    private readonly Dictionary<int, SatelliteTracker> _satellites = new();

    // Aggregate counters
    private int _assembledSubframes;
    private int _verifiedTeslaKeys;
    private int _verifiedTags;
    private int _failedTags;
    private int _bitwiseMatches;
    private int _bitwiseMismatches;

    public OsnmaLiveVerifier(GstClockService? gstClock = null)
    {
        _gstClock = gstClock ?? new GstClockService();
    }

    public GstClockService Clock => _gstClock;

    /// <summary>
    /// Loads official EUSPA Merkle Tree / Public Key XML files to arm live ECDSA P-256 verification.
    /// </summary>
    public OsnmaAssistMaterial LoadEuspaXmlKeys(string? merkleTreeXmlPath, string? publicKeyXmlPath)
    {
        var material = EuspaOsnmaXmlLoader.LoadAssistMaterialFromXmlFiles(merkleTreeXmlPath, publicKeyXmlPath);
        ApplyAssistMaterial(material, isAssistedSource: false);
        return material;
    }

    /// <summary>
    /// Applies trusted bootstrap material (either from local EUSPA XML or from TRISAT-Assist).
    /// If a raw DSM-KROOT is provided alongside a trusted Public Key, its ECDSA P-256 signature and P_DK padding
    /// are cryptographically verified before activating KROOT.
    /// </summary>
    public bool ApplyAssistMaterial(OsnmaAssistMaterial material, bool isAssistedSource = true)
    {
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(material.MerkleRootHex))
                _merkleRootHex = material.MerkleRootHex.Trim().ToUpperInvariant();

            if (material.PublicKeyId.HasValue)
                _publicKeyId = material.PublicKeyId.Value;

            if (!string.IsNullOrWhiteSpace(material.PublicKeyCompressedHex))
                _publicKeyCompressedHex = material.PublicKeyCompressedHex.Trim().ToUpperInvariant();

            if (material.NmaHeaderByte.HasValue)
                _latestNmaHeader = OsnmaNmaHeader.Parse(material.NmaHeaderByte.Value);

            if (!string.IsNullOrWhiteSpace(material.DsmKrootHex))
            {
                var dsmBytes = Convert.FromHexString(material.DsmKrootHex.Trim());
                if (TryVerifyAndActivateDsmKrootLocked(dsmBytes, isAssistedSource))
                    return true;
                return false;
            }

            // Direct pre-verified KROOT parameters (only accepted when explicitly supplied by trusted local/assisted config)
            if (!string.IsNullOrWhiteSpace(material.KrootHex) &&
                !string.IsNullOrWhiteSpace(material.AlphaHex) &&
                material.WnKroot.HasValue &&
                material.Tow0Kroot.HasValue)
            {
                _krootHex = material.KrootHex.Trim().ToUpperInvariant();
                _alphaHex = material.AlphaHex.Trim().ToUpperInvariant();
                _wnKroot = material.WnKroot.Value;
                _tow0Kroot = material.Tow0Kroot.Value;
                _keySizeBits = material.KeySizeBits > 0 ? material.KeySizeBits : 128;
                _tagSizeBits = material.TagSizeBits > 0 ? material.TagSizeBits : 40;
                _macltId = material.MacltId > 0 ? material.MacltId : 34;
                _hasVerifiedKroot = true;
                _isAssistedBootstrap = isAssistedSource;
                return true;
            }

            return !string.IsNullOrWhiteSpace(_publicKeyCompressedHex) || !string.IsNullOrWhiteSpace(_merkleRootHex);
        }
    }

    /// <summary>
    /// Exports current trusted/assembled material so TRISAT-Assist Server can bootstrap remote clients immediately.
    /// </summary>
    public OsnmaAssistMaterial ExportAssistMaterial()
    {
        lock (_sync)
        {
            return new OsnmaAssistMaterial(
                MerkleRootHex: _merkleRootHex,
                PublicKeyId: _publicKeyId,
                PublicKeyType: _publicKeyCompressedHex is not null ? "ECDSA P-256/SHA-256" : null,
                PublicKeyCompressedHex: _publicKeyCompressedHex,
                DsmKrootHex: _dsmKrootHex,
                KrootHex: _hasVerifiedKroot ? _krootHex : null,
                AlphaHex: _hasVerifiedKroot ? _alphaHex : null,
                WnKroot: _hasVerifiedKroot ? _wnKroot : null,
                Tow0Kroot: _hasVerifiedKroot ? _tow0Kroot : null,
                KeySizeBits: _keySizeBits,
                TagSizeBits: _tagSizeBits,
                MacltId: _macltId,
                NmaHeaderByte: _latestNmaHeader?.RawByte,
                Source: "Assisted OSNMA");
        }
    }

    /// <summary>
    /// Ingests a decoded Galileo I/NAV nominal page into the 30-second OSNMA subframe assembler.
    /// If pageGst is omitted, uses decoded WT0/WT5 GST or current network GST aligned to 2-second boundaries.
    /// </summary>
    public void IngestPage(ParsedGalileoINavPage page, GstTime? pageGst = null, DateTimeOffset? nowUtcOverride = null)
    {
        if (page.Svid < 1 || page.Svid > 36)
            return;

        if (page.DecodedGst.HasValue)
            _gstClock.ReportReceiverGst(page.DecodedGst.Value, nowUtcOverride);

        GstTime effectiveGst = pageGst ?? page.DecodedGst ?? AlignTo2Sec(_gstClock.GetCurrentNetworkGst());

        lock (_sync)
        {
            var tracker = GetOrCreateTrackerLocked(page.Svid);
            tracker.LastSeenUtc = nowUtcOverride ?? DateTimeOffset.UtcNow;
            tracker.LastGstSeconds = effectiveGst.TotalSeconds;

            if (page.HasNonZeroOsnma)
            {
                tracker.HasOsnmaSignal = true;
                tracker.LastHkrootHex = page.HkrootHex;
                tracker.LastMackHex = page.MackHex;
            }

            if (!page.CrcValid || !page.HasNonZeroOsnma)
                return;

            long sfStart = (long)effectiveGst.WeekNumber * GstTime.SecondsPerWeek + effectiveGst.SubframeStartTow;
            int pageIdx = Math.Clamp(effectiveGst.PageIndexInSubframe, 0, 14);

            var key = (page.Svid, sfStart);
            if (!_subframes.TryGetValue(key, out var buf))
            {
                buf = new SubframeBuffer(page.Svid, new GstTime(effectiveGst.WeekNumber, effectiveGst.SubframeStartTow));
                _subframes[key] = buf;
            }

            buf.SetPage(pageIdx, page);

            if (buf.IsComplete && !buf.Processed)
            {
                buf.Processed = true;
                _assembledSubframes++;
                ProcessCompletedSubframeLocked(buf, nowUtcOverride);
            }

            PruneOldSubframesLocked(sfStart);
        }
    }

    /// <summary>
    /// Records the outcome of a 2-second Bitwise cross-check against a trusted TRISAT-Assist reference station.
    /// </summary>
    public void RecordBitwiseResult(BitwiseCheckResult result)
    {
        if (!result.HasReference)
            return;

        lock (_sync)
        {
            var tracker = GetOrCreateTrackerLocked(result.Svid);
            tracker.HasOsnmaSignal = true;
            tracker.LastGstSeconds = result.GstSeconds;
            tracker.LastSeenUtc = DateTimeOffset.UtcNow;

            if (result.Matched)
            {
                _bitwiseMatches++;
                tracker.BitwiseMatches++;
            }
            else
            {
                _bitwiseMismatches++;
                tracker.BitwiseMismatches++;
            }
        }
    }

    /// <summary>
    /// Verifies a standalone TESLA key + HMAC-SHA-256 tag over a padded OSNMA message M against the active KROOT
    /// (guarded by GstClockService time-sync verification).
    /// </summary>
    public bool VerifyLiveTagWithTeslaKey(
        int svid,
        string teslaKeyHex,
        GstTime keyGst,
        byte[] paddedMessage,
        string expectedTagHex,
        DateTimeOffset? nowUtcOverride = null)
    {
        if (!_gstClock.IsSubframeTimeAcceptable(keyGst, GstClockService.OsnmaMaxTeslaSyncOffsetSeconds, nowUtcOverride))
        {
            lock (_sync)
            {
                _failedTags++;
                GetOrCreateTrackerLocked(svid).FailedTags++;
            }
            return false;
        }

        lock (_sync)
        {
            if (!_hasVerifiedKroot || _krootHex is null || _alphaHex is null)
                return false;

            bool keyOk = OsnmaEngine.VerifyTeslaKeyAgainstKroot(
                teslaKeyHex,
                keyGst.WeekNumber,
                keyGst.TowSeconds,
                _krootHex,
                _wnKroot,
                _tow0Kroot,
                _alphaHex,
                _keySizeBits);

            if (!keyOk)
            {
                _failedTags++;
                GetOrCreateTrackerLocked(svid).FailedTags++;
                return false;
            }

            _verifiedTeslaKeys++;
            _latestTeslaKeyHex = teslaKeyHex.ToUpperInvariant();

            string computedTag = OsnmaEngine.ComputeTruncatedTag(teslaKeyHex, paddedMessage, _tagSizeBits);
            bool tagOk = string.Equals(computedTag, expectedTagHex, StringComparison.OrdinalIgnoreCase);

            var tracker = GetOrCreateTrackerLocked(svid);
            tracker.HasOsnmaSignal = true;
            tracker.LastGstSeconds = keyGst.TotalSeconds;
            tracker.LastSeenUtc = nowUtcOverride ?? DateTimeOffset.UtcNow;

            if (tagOk)
            {
                _verifiedTags++;
                tracker.AuthenticatedTags++;
            }
            else
            {
                _failedTags++;
                tracker.FailedTags++;
            }

            return tagOk;
        }
    }

    /// <summary>
    /// Returns a thread-safe snapshot of the live OSNMA verification session.
    /// </summary>
    public OsnmaLiveSummary GetSummary(bool hasAnyInavMessages)
    {
        lock (_sync)
        {
            var statuses = _satellites.Values
                .OrderBy(t => t.Svid)
                .Select(t => new OsnmaSatelliteStatus(
                    t.Svid,
                    t.HasOsnmaSignal,
                    t.AuthenticatedTags,
                    t.FailedTags,
                    t.BitwiseMatches,
                    t.BitwiseMismatches,
                    t.LastHkrootHex,
                    t.LastMackHex,
                    t.LastGstSeconds,
                    t.LastSeenUtc))
                .ToList();

            var authSvids = statuses
                .Where(s => s.AuthenticatedTags > 0)
                .Select(s => s.Svid)
                .ToList();

            int activeOsnmaSats = statuses.Count(s => s.HasOsnmaSignal);

            OsnmaReceptionStatus status;
            string details;

            if (_failedTags > 0 || _bitwiseMismatches > 0)
            {
                status = OsnmaReceptionStatus.TamperDetected;
                details = $"OSNMA integrity alert: {_failedTags} crypto tag failure(s), {_bitwiseMismatches} bitwise mismatch(es) across {activeOsnmaSats} Galileo SV(s).";
            }
            else if (_verifiedTags > 0)
            {
                status = OsnmaReceptionStatus.Authenticated;
                var modeLabel = _isAssistedBootstrap ? "Assisted ECDSA+TESLA" : "Autonomous ECDSA+TESLA";
                details = $"Live OSNMA authenticated ({modeLabel}): {_verifiedTags} tag(s) & {_verifiedTeslaKeys} TESLA key(s) verified across {authSvids.Count} Galileo SV(s).";
            }
            else if (_bitwiseMatches > 0)
            {
                status = OsnmaReceptionStatus.BitwiseVerified;
                details = $"Bitwise reference cross-check active: {_bitwiseMatches} / {_bitwiseMatches + _bitwiseMismatches} 40-bit OSNMA frames matched Assisted OSNMA reference.";
            }
            else if (hasAnyInavMessages || activeOsnmaSats > 0)
            {
                status = OsnmaReceptionStatus.Unverified;
                var anchorInfo = _hasVerifiedKroot
                    ? "KROOT verified · awaiting next 30s TESLA key reveal"
                    : (_publicKeyCompressedHex is not null
                        ? "EUSPA Public Key loaded · assembling DSM-KROOT"
                        : "Load EUSPA XML keys or connect Assisted OSNMA for live authentication");
                details = $"Galileo I/NAV received ({activeOsnmaSats} SV(s) with OSNMA bits, {_assembledSubframes} subframe(s) assembled). {anchorInfo}.";
            }
            else
            {
                status = OsnmaReceptionStatus.NotAvailable;
                details = "Awaiting Galileo I/NAV telemetry from USB serial, BLE, or UDP bridge.";
            }

            return new OsnmaLiveSummary(
                Status: status,
                NmaHeader: _latestNmaHeader,
                HasTrustedPublicKey: !string.IsNullOrWhiteSpace(_publicKeyCompressedHex),
                HasVerifiedKroot: _hasVerifiedKroot,
                IsAssistedBootstrap: _isAssistedBootstrap,
                ActiveKrootHex: _hasVerifiedKroot ? _krootHex : null,
                LatestTeslaKeyHex: _latestTeslaKeyHex,
                AssembledSubframes: _assembledSubframes,
                VerifiedTeslaKeys: _verifiedTeslaKeys,
                VerifiedTags: _verifiedTags,
                FailedTags: _failedTags,
                ActiveOsnmaSatellites: activeOsnmaSats,
                AuthenticatedSvids: authSvids,
                SatelliteStatuses: statuses,
                DetailText: details);
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _subframes.Clear();
            _dsmKrootBlocks.Clear();
            _satellites.Clear();
            _assembledSubframes = 0;
            _verifiedTeslaKeys = 0;
            _verifiedTags = 0;
            _failedTags = 0;
            _bitwiseMatches = 0;
            _bitwiseMismatches = 0;
            _latestTeslaKeyHex = null;
        }
    }

    private void ProcessCompletedSubframeLocked(SubframeBuffer sf, DateTimeOffset? nowUtcOverride)
    {
        // 1. Assemble 120-bit (15-byte) HKROOT and 480-bit (60-byte) MACK
        var hkroot = new byte[15];
        var mack = new byte[60];
        for (int i = 0; i < 15; i++)
        {
            var p = sf.Pages[i]!;
            hkroot[i] = p.HkrootByte;
            Buffer.BlockCopy(p.MackBytes, 0, mack, i * 4, 4);
        }

        // Byte 0 of HKROOT = 8-bit NMA Header; Byte 1 = DSM Header (DSM_ID 4b | DSM_BID 4b); Bytes 2..14 = 104-bit DSM block
        _latestNmaHeader = OsnmaNmaHeader.Parse(hkroot[0]);

        int dsmId = (hkroot[1] >> 4) & 0x0F;
        int dsmBid = hkroot[1] & 0x0F;
        var dsmBlock = new byte[13];
        Buffer.BlockCopy(hkroot, 2, dsmBlock, 0, 13);

        // DSM_ID 12..15 carry DSM-KROOT blocks (OSNMA SIS ICD Table 6)
        if (dsmId >= 12)
        {
            if (!_dsmKrootBlocks.TryGetValue(dsmId, out var blocks))
            {
                blocks = new Dictionary<int, byte[]>();
                _dsmKrootBlocks[dsmId] = blocks;
            }
            blocks[dsmBid] = dsmBlock;

            // NB_DK is encoded in the top 4 bits of block 0 (values 7..14 -> block count)
            if (blocks.TryGetValue(0, out var b0))
            {
                int nbDkCode = (b0[0] >> 4) & 0x0F;
                int expectedBlocks = DecodeNbDk(nbDkCode);
                if (expectedBlocks > 0 && blocks.Count >= expectedBlocks)
                {
                    bool allPresent = true;
                    for (int b = 0; b < expectedBlocks; b++)
                    {
                        if (!blocks.ContainsKey(b))
                        {
                            allPresent = false;
                            break;
                        }
                    }

                    if (allPresent)
                    {
                        var assembled = new byte[expectedBlocks * 13];
                        for (int b = 0; b < expectedBlocks; b++)
                            Buffer.BlockCopy(blocks[b], 0, assembled, b * 13, 13);

                        TryVerifyAndActivateDsmKrootLocked(assembled, isAssistedSource: false);
                    }
                }
            }
        }

        // 2. Extract delayed TESLA key from tail of 480-bit MACK (last l_K/8 bytes) and verify against KROOT
        if (_hasVerifiedKroot && _krootHex is not null && _alphaHex is not null)
        {
            int keyBytesLen = _keySizeBits / 8;
            if (keyBytesLen is 16 or 24 or 32 && mack.Length >= keyBytesLen)
            {
                var keySlice = mack.AsSpan(mack.Length - keyBytesLen, keyBytesLen);
                if (keySlice.ToArray().Any(b => b != 0))
                {
                    // Guard: reject TESLA keys if GST clock drift exceeds OSNMA loose synchronization bound
                    if (_gstClock.IsSubframeTimeAcceptable(sf.SubframeStartGst, GstClockService.OsnmaMaxTeslaSyncOffsetSeconds, nowUtcOverride))
                    {
                        // The key transmitted in subframe I authenticates tags from subframe I-1 (GST_SF,I - 30s)
                        var prevGst = GstTime.FromTotalSeconds(sf.SubframeStartGst.TotalSeconds - 30);
                        string candidateKeyHex = Convert.ToHexString(keySlice);

                        if (OsnmaEngine.VerifyTeslaKeyAgainstKroot(
                                candidateKeyHex,
                                prevGst.WeekNumber,
                                prevGst.TowSeconds,
                                _krootHex,
                                _wnKroot,
                                _tow0Kroot,
                                _alphaHex,
                                _keySizeBits))
                        {
                            _verifiedTeslaKeys++;
                            _latestTeslaKeyHex = candidateKeyHex;

                            // If we buffered the previous subframe for this SV, verify its Tag0 (ADKD=0)
                            long prevSfKey = prevGst.TotalSeconds;
                            if (_subframes.TryGetValue((sf.Svid, prevSfKey), out var prevSf) &&
                                prevSf.Tag0Hex is not null &&
                                prevSf.Adkd0NavDataBytes is not null)
                            {
                                VerifyTag0ForSubframeLocked(prevSf, candidateKeyHex, nowUtcOverride);
                            }
                        }
                    }
                }
            }
        }

        // 3. Cache Tag0 (first l_T bits of MACK) & ADKD=0 nav data (WT 1..5 if captured) for verification on next subframe
        int tagBytesLen = _tagSizeBits / 8;
        if (tagBytesLen > 0 && tagBytesLen <= 8)
            sf.Tag0Hex = Convert.ToHexString(mack.AsSpan(0, tagBytesLen));
    }

    private void VerifyTag0ForSubframeLocked(SubframeBuffer prevSf, string verifiedKeyHex, DateTimeOffset? nowUtcOverride)
    {
        if (prevSf.Tag0Hex is null || prevSf.Adkd0NavDataBytes is null)
            return;

        // Tag0 message: PRN_A (8b) || GST_SF (32b) || CTR=1 (8b) || NMAS (2b) || NavData (549b) + zero pad
        var computed = OsnmaEngine.ComputeTruncatedTag(verifiedKeyHex, prevSf.Adkd0NavDataBytes, _tagSizeBits);
        bool ok = string.Equals(computed, prevSf.Tag0Hex, StringComparison.OrdinalIgnoreCase);

        var tracker = GetOrCreateTrackerLocked(prevSf.Svid);
        tracker.LastSeenUtc = nowUtcOverride ?? DateTimeOffset.UtcNow;

        if (ok)
        {
            _verifiedTags++;
            tracker.AuthenticatedTags++;
        }
        else
        {
            _failedTags++;
            tracker.FailedTags++;
        }
    }

    /// <summary>
    /// Parses and cryptographically verifies a complete DSM-KROOT message (ECDSA P-256 signature DS + P_DK SHA-256 padding)
    /// per Galileo OSNMA SIS ICD v1.1 Section 6.3.2 & RX Guidelines Annex A.
    /// </summary>
    private bool TryVerifyAndActivateDsmKrootLocked(byte[] dsmBytes, bool isAssistedSource)
    {
        if (dsmBytes.Length < 104 || string.IsNullOrWhiteSpace(_publicKeyCompressedHex))
            return false;

        // Standard ECDSA P-256 / 128-bit KROOT layout (8 blocks = 104 bytes = 832 bits, as in Annex A):
        //   Byte 0:   NB_DK (4b) | PKID (4b)
        //   Byte 1:   CIDKR (2b) | reserved (2b) | HF (2b) | MF (2b)
        //   Byte 2:   KS (4b) | TS (4b)
        //   Byte 3:   MACLT (8b)
        //   Byte 4:   reserved (4b) | WN_k high (4b)
        //   Byte 5:   WN_k low (8b)
        //   Byte 6:   TOWH_k (8b)
        //   Bytes 7..12:  alpha (48b = 6 bytes)
        //   Bytes 13..28: KROOT (128b = 16 bytes for KS=0)
        //   => Signed message M = NMA_Header (1 byte) || DSM-KROOT[1..28] (28 bytes)  [or DSM-KROOT[0..28] depending on header]
        //   Bytes 29..92: DS (ECDSA P-256 r||s = 64 bytes)
        //   Bytes 93..103: P_DK (11 bytes = trunc(88, SHA256(M || DS)))
        int ksCode = (dsmBytes[2] >> 4) & 0x0F;
        int tsCode = dsmBytes[2] & 0x0F;
        int keyBits = DecodeKeySizeBits(ksCode);
        int tagBits = DecodeTagSizeBits(tsCode);
        int keyBytesLen = keyBits / 8;
        int maclt = dsmBytes[3];
        int wnK = ((dsmBytes[4] & 0x0F) << 8) | dsmBytes[5];
        int towhK = dsmBytes[6];
        int alphaOffset = 7;
        int krootOffset = alphaOffset + 6;
        int dsOffset = krootOffset + keyBytesLen;
        int pdkOffset = dsOffset + 64;

        if (pdkOffset > dsmBytes.Length)
            return false;

        var alphaHex = Convert.ToHexString(dsmBytes.AsSpan(alphaOffset, 6));
        var krootHex = Convert.ToHexString(dsmBytes.AsSpan(krootOffset, keyBytesLen));
        var dsHex = Convert.ToHexString(dsmBytes.AsSpan(dsOffset, 64));

        // Reconstruct signed message M = NMAHeader (1 byte) || dsmBytes[1 .. dsOffset-1]
        byte nmaHdr = _latestNmaHeader?.RawByte ?? 0x82; // Default Operational CID=0 CPKS=1 if NMA header not yet seen
        var signedM = new byte[dsOffset];
        signedM[0] = nmaHdr;
        Buffer.BlockCopy(dsmBytes, 1, signedM, 1, dsOffset - 1);

        bool sigOk = OsnmaEngine.VerifyEcdsaP256Sha256(_publicKeyCompressedHex, signedM, dsHex);
        if (!sigOk)
        {
            // Also test candidate NMA header values when DSM-KROOT is provided standalone without a preceding HKROOT byte
            byte[] fallbackHeaders = [0x82, 0x42, 0x80, 0x84];
            foreach (byte candidate in fallbackHeaders)
            {
                if (candidate == nmaHdr) continue;
                signedM[0] = candidate;
                if (OsnmaEngine.VerifyEcdsaP256Sha256(_publicKeyCompressedHex, signedM, dsHex))
                {
                    sigOk = true;
                    _latestNmaHeader = OsnmaNmaHeader.Parse(candidate);
                    break;
                }
            }
        }

        if (!sigOk)
            return false;

        // Verify P_DK padding if trailing padding bytes are present
        int pdkLen = dsmBytes.Length - pdkOffset;
        if (pdkLen > 0)
        {
            var mPlusDs = new byte[signedM.Length + 64];
            Buffer.BlockCopy(signedM, 0, mPlusDs, 0, signedM.Length);
            Buffer.BlockCopy(dsmBytes, dsOffset, mPlusDs, signedM.Length, 64);
            var expectedPdk = SHA256.HashData(mPlusDs);
            if (!expectedPdk.AsSpan(0, Math.Min(pdkLen, expectedPdk.Length))
                .SequenceEqual(dsmBytes.AsSpan(pdkOffset, Math.Min(pdkLen, expectedPdk.Length))))
            {
                return false;
            }
        }

        _dsmKrootHex = Convert.ToHexString(dsmBytes);
        _krootHex = krootHex;
        _alphaHex = alphaHex;
        _wnKroot = wnK;
        _tow0Kroot = towhK * 3600;
        _keySizeBits = keyBits;
        _tagSizeBits = tagBits;
        _macltId = maclt;
        _hasVerifiedKroot = true;
        _isAssistedBootstrap = isAssistedSource;
        return true;
    }

    private SatelliteTracker GetOrCreateTrackerLocked(int svid)
    {
        if (!_satellites.TryGetValue(svid, out var tracker))
        {
            tracker = new SatelliteTracker(svid);
            _satellites[svid] = tracker;
        }
        return tracker;
    }

    private void PruneOldSubframesLocked(long currentSfStart)
    {
        long threshold = currentSfStart - 300; // Keep last 10 subframes (5 minutes)
        var oldKeys = _subframes.Keys.Where(k => k.SubframeGstStart < threshold).ToList();
        foreach (var k in oldKeys)
            _subframes.Remove(k);
    }

    private static GstTime AlignTo2Sec(GstTime gst)
        => new(gst.WeekNumber, (gst.TowSeconds / 2) * 2);

    private static int DecodeNbDk(int code) => code switch
    {
        1 => 7,
        2 => 8,
        3 => 9,
        4 => 10,
        5 => 11,
        6 => 12,
        7 => 13,
        8 => 14,
        _ => 8
    };

    private static int DecodeKeySizeBits(int code) => code switch
    {
        0 => 96,
        1 => 104,
        2 => 112,
        3 => 120,
        4 => 128,
        5 => 192,
        6 => 256,
        _ => 128
    };

    private static int DecodeTagSizeBits(int code) => code switch
    {
        5 => 20,
        6 => 24,
        7 => 28,
        8 => 32,
        9 => 40,
        _ => 40
    };

    private sealed class SubframeBuffer
    {
        public int Svid { get; }
        public GstTime SubframeStartGst { get; }
        public ParsedGalileoINavPage?[] Pages { get; } = new ParsedGalileoINavPage?[15];
        public bool Processed { get; set; }
        public string? Tag0Hex { get; set; }
        public byte[]? Adkd0NavDataBytes { get; set; }
        private int _receivedCount;

        public SubframeBuffer(int svid, GstTime subframeStartGst)
        {
            Svid = svid;
            SubframeStartGst = subframeStartGst;
        }

        public bool IsComplete => _receivedCount == 15;

        public void SetPage(int index, ParsedGalileoINavPage page)
        {
            if (Pages[index] is null)
                _receivedCount++;
            Pages[index] = page;
        }
    }

    private sealed class SatelliteTracker
    {
        public int Svid { get; }
        public bool HasOsnmaSignal { get; set; }
        public int AuthenticatedTags { get; set; }
        public int FailedTags { get; set; }
        public int BitwiseMatches { get; set; }
        public int BitwiseMismatches { get; set; }
        public string? LastHkrootHex { get; set; }
        public string? LastMackHex { get; set; }
        public long? LastGstSeconds { get; set; }
        public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;

        public SatelliteTracker(int svid) => Svid = svid;
    }
}

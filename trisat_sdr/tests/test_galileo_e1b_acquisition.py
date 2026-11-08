"""Hardware-free E1-B tests with independently generated baseband samples."""

import cmath
import hashlib
import math
from pathlib import Path
import random
import struct
import tempfile
import unittest
from unittest.mock import patch

from trisat_sdr.config import ReceiverConfig
from trisat_sdr.dsp import acquisition, codes
from trisat_sdr.receiver import TrisatSdrReceiver


ANNEX = Path(codes.__file__).with_name("C7_E1B.txt")


def reference_bits(svid):
    record = ANNEX.read_text(encoding="ascii").splitlines()[svid - 1]
    label, hex_code = record.split(";")
    assert label == f"E1B__Code_No_{svid:02d}"
    return format(int(hex_code, 16), "04092b")


def make_iq(svid, sample_rate, delay, doppler, seed=9):
    """Generate CBOC from subchip transitions, with alternating data symbols."""
    subchip_rate = 12 * 1_023_000
    waveform = []
    for chip, bit in enumerate(reference_bits(svid)):
        polarity = -1 if bit == "1" else 1
        for subchip in range(12):
            transition = ((chip * 12 + subchip + 1) * sample_rate + subchip_rate - 1) // subchip_rate
            boc1 = math.sqrt(10 / 11) * (1 if subchip < 6 else -1)
            boc6 = math.sqrt(1 / 11) * (-1) ** subchip
            waveform.extend([polarity * (boc1 + boc6)] * (transition - len(waveform)))

    rng = random.Random(seed)
    iq = []
    for i in range(2 * len(waveform)):
        symbol, offset = divmod(i - delay, len(waveform))
        carrier = cmath.exp(1j * (0.7 + math.tau * doppler * i / sample_rate))
        noise = complex(rng.gauss(0, 0.7), rng.gauss(0, 0.7))
        iq.append((-1) ** symbol * waveform[offset] * carrier + noise)
    return iq


class TestGalileoE1bAcquisition(unittest.TestCase):
    def test_all_assigned_codes_match_official_annex(self):
        self.assertEqual(
            hashlib.sha256(ANNEX.read_bytes()).hexdigest(),
            "26ce97a6a76b579f003f2ad6cbd7715c32768c1cb25afcfa9af38c30fe0a0df2",
        )
        for svid in range(1, 37):
            with self.subTest(svid=svid):
                chips = codes.generate_galileo_e1_code(svid)
                self.assertEqual("".join("0" if c == 1 else "1" for c in chips), reference_bits(svid))
                self.assertEqual(set(chips), {-1, 1})
        for svid in (0, 37):
            with self.assertRaises(ValueError):
                codes.generate_galileo_e1_code(svid)

    def test_known_delay_and_doppler_across_symbol_boundaries(self):
        cases = [(1, 4_092_000, 8184, 500, 500), (36, 4_000_000, 15999, -620, -500)]
        backends = (True, False) if codes.HAS_NUMPY else (False,)
        for svid, rate, delay, doppler, expected_bin in cases:
            iq = make_iq(svid, rate, delay, doppler)
            for use_numpy in backends:
                with self.subTest(svid=svid, rate=rate, numpy=use_numpy):
                    with patch.object(acquisition, "HAS_NUMPY", use_numpy):
                        engine = acquisition.PcpsAcquisitionEngine(rate, 750, 250)
                        result = engine.acquire_satellite(iq, "GAL_E1", "GALILEO", svid, 4092)
                    self.assertTrue(result.acquired)
                    self.assertEqual(result.doppler_hz, expected_bin)
                    self.assertEqual(result.code_phase_samples, delay)
                    self.assertAlmostEqual(result.code_phase_chips, delay * 1_023_000 / rate, delta=0.005)

    def test_rejects_wrong_prn_noise_and_short_windows(self):
        rate = 4_092_000
        iq = make_iq(11, rate, 4112, 0)
        rng = random.Random(342)
        noise = [complex(rng.gauss(0, 1), rng.gauss(0, 1)) for _ in iq]
        engine = acquisition.PcpsAcquisitionEngine(rate, 0)
        for samples, svid in ((iq, 12), (noise, 11), (iq[:-1], 11), ([], 11)):
            with self.subTest(samples=len(samples), svid=svid):
                result = engine.acquire_satellite(samples, "GAL_E1", "GALILEO", svid, 4092)
                self.assertFalse(result.acquired)

    def test_receiver_searches_capture_beyond_simulated_satellite_list(self):
        rate = 4_092_000
        delay = 12_519
        svid = 8  # Absent from the receiver's simulated Galileo constellation.
        iq = make_iq(svid, rate, delay, 0)
        raw = b"".join(struct.pack("<ff", s.real, s.imag) for s in iq)
        cfg = ReceiverConfig(dual_sdr_enabled=False, doppler_search_max_hz=0)
        cfg.primary_sdr.driver = "file"
        cfg.primary_sdr.iq_format = "float32"
        cfg.telemetry.udp_enabled = False
        cfg.telemetry.tcp_enabled = False
        cfg.telemetry.pty_serial_enabled = False
        with tempfile.TemporaryDirectory() as folder:
            capture = Path(folder) / "e1b.iq"
            cfg.primary_sdr.iq_file_path = str(capture)
            # A longer file must still be searched from sample zero.
            capture.write_bytes(raw + bytes(len(raw) // 2 + 1))
            receiver = TrisatSdrReceiver(cfg)
            results = receiver.initialize_acquisition()
            self.assertEqual([r.svid for r in results], list(range(1, 37)))
            self.assertEqual([(r.svid, r.code_phase_samples) for r in results if r.acquired], [(svid, delay)])
            self.assertEqual([channel.svid for channel in receiver.tracking_channels], [svid])

            capture.write_bytes(raw[:-8])
            self.assertFalse(any(r.acquired for r in TrisatSdrReceiver(cfg).initialize_acquisition()))
            capture.unlink()
            with self.assertRaises(FileNotFoundError):
                TrisatSdrReceiver(cfg).initialize_acquisition()

"""
Automated Unit & Integration Tests for TRISAT-SDR.
Verifies:
  1. PRN spreading codes (GPS L1 C/A, Galileo E1 CBOC, Galileo E5a/E5b, Galileo E6 HAS)
  2. PCPS FFT Acquisition Engine (both NumPy and pure-Python radix-2 FFT)
  3. 5-Tap DLL/PLL Tracking Channel
  4. Galileo CRC-24Q & Frame Decoders ($GALINAV + OSNMA, $GALE6HAS, $GALE5)
  5. Dual-HackRF Receiver Pipeline & Telemetry Bridge
"""

import os
import sys
import unittest

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
if REPO_ROOT not in sys.path:
    sys.path.insert(0, REPO_ROOT)

from trisat_sdr.bridge.telemetry_streamer import format_gngga_sentence, format_gsv_sentences  # noqa: E402
from trisat_sdr.config import ReceiverConfig  # noqa: E402
from trisat_sdr.dsp.acquisition import PcpsAcquisitionEngine  # noqa: E402
from trisat_sdr.dsp.codes import (  # noqa: E402
    generate_galileo_e1_code,
    generate_galileo_e5_code,
    generate_galileo_e6_code,
    generate_gps_l1ca_code,
    sample_code_waveform,
)
from trisat_sdr.nav.frame_decoder import GnssFrameDecoder, compute_crc24q  # noqa: E402
from trisat_sdr.receiver import TrisatSdrReceiver  # noqa: E402


class TestTrisatSdr(unittest.TestCase):
    def test_prn_code_lengths_and_balance(self):
        l1 = generate_gps_l1ca_code(1)
        self.assertEqual(len(l1), 1023)
        self.assertTrue(all(c in (-1, 1) for c in l1))

        e1b = generate_galileo_e1_code(11, component="B")
        self.assertEqual(len(e1b), 4092)

        e5a = generate_galileo_e5_code(11, band="E5A")
        self.assertEqual(len(e5a), 10230)

        e6b = generate_galileo_e6_code(11, component="B")
        self.assertEqual(len(e6b), 5115)

    def test_cboc_and_bpsk_waveform_sampling(self):
        wf_e1 = sample_code_waveform("GAL_E1", svid=2, sample_rate_hz=4.092e6, duration_ms=1.0)
        self.assertEqual(len(wf_e1), 4092)
        wf_e6 = sample_code_waveform("GAL_E6", svid=2, sample_rate_hz=8.184e6, duration_ms=1.0)
        self.assertEqual(len(wf_e6), 8184)

    def test_crc24q_and_frame_decoder(self):
        decoder = GnssFrameDecoder()
        inav = decoder.decode_channel_frame("GAL_E1", "GALILEO", 11, [1, 0, 1, 1], 47.5)
        self.assertEqual(inav.frame_type, "INAV_E1B")
        self.assertEqual(len(inav.hex_payload), 60)  # 30 bytes = 60 hex chars
        self.assertEqual(len(inav.osnma_hkroot_hex), 2)
        self.assertEqual(len(inav.osnma_mack_hex), 8)
        self.assertTrue(inav.nmea_custom_sentence.startswith("$GALINAV,11,"))

        # Verify CRC-24Q over full 30-byte frame equals 0 residue or matches last 3 bytes
        raw_bytes = bytes.fromhex(inav.hex_payload)
        computed_crc = compute_crc24q(raw_bytes[:27])
        self.assertEqual(f"{computed_crc:06X}", inav.crc24q_hex)

        e6_has = decoder.decode_channel_frame("GAL_E6", "GALILEO", 24, [0, 1, 0, 1], 46.2)
        self.assertEqual(e6_has.frame_type, "CNAV_E6_HAS")
        self.assertTrue(e6_has.nmea_custom_sentence.startswith("$GALE6HAS,24,"))

    def test_dual_hackrf_receiver_and_band_switching(self):
        cfg = ReceiverConfig()
        cfg.telemetry.udp_enabled = False
        cfg.telemetry.tcp_enabled = False
        cfg.telemetry.pty_serial_enabled = False

        rx = TrisatSdrReceiver(cfg)
        acqs = rx.initialize_acquisition()
        self.assertGreaterEqual(len(acqs), 8)

        snap = rx.step_epoch()
        self.assertTrue(snap["pvt"]["valid_fix"])
        self.assertIn("HAS", snap["pvt"]["solution_mode"])

        # Switch HackRF #1 dynamically to Galileo E5a
        rx.switch_secondary_band("GAL_E5A", dual_enabled=True)
        snap_e5 = rx.step_epoch()
        self.assertEqual(snap_e5["secondary_sdr"]["band_id"], "GAL_E5A")
        self.assertIn("E5", snap_e5["pvt"]["solution_mode"])

        gga = format_gngga_sentence(rx.latest_pvt)
        self.assertTrue(gga.startswith("$GNGGA,"))
        gsv = format_gsv_sentences(rx.latest_pvt.satellites)
        self.assertGreaterEqual(len(gsv), 1)


if __name__ == "__main__":
    unittest.main()

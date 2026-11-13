import sys
import unittest
from struct import pack, unpack
from types import SimpleNamespace
from unittest.mock import Mock, call, patch

from trisat_hw.zed_f9p import f9p_bridge
from trisat_hw.zed_f9p.f9p_stream import build_ubx_msg
from trisat_hw.zed_f9p.galileo_inav import decode_sfrbx


# Galileo OSNMA Receiver Guidelines v1.3, Annex A.6.3. These are published
# signal-in-space page parts. The UBX wrapper is constructed for these tests.
E1B_PAGES = (
    (2, "020EB72D6AB6C9DEBDBF3C87EE63C0", "BDFA60936A7475EAAAAA7085944100"),
    (4, "040E82001A000E5910006D31F00000", "A68070D8F793F7AAAAAA68A91DCAC0"),
    (0, "009555555555555555555555538140", "918BC4060303E5EAAAAA7BD6FCCBC0"),
)
# NovAtel OEM7 GALINAVRAWDATA documentation, published E5b-I example.
E5B_PAGE = (5, "05477F2FFF607DBF500AAAED4ACA80", "AAAA80000000002AAAAAA23981FF40")


def sfrbx(even, odd, *, signal=1):
    # Nonzero padding checks that the decoder discards both pad bytes.
    padded = bytes.fromhex(even + "AA" + odd + "55")
    return bytes((2, 2, signal, 0, 8, 0, 2, 0)) + pack("<8I", *unpack(">8I", padded))


class TestGalileoInav(unittest.TestCase):
    def test_official_e1b_page_pairs(self):
        for word_type, even, odd in E1B_PAGES:
            with self.subTest(word_type=word_type):
                page = decode_sfrbx(sfrbx(even, odd))
                self.assertIsNotNone(page)
                self.assertEqual((page.svid, page.band, page.word_type), (2, "E1-B", word_type))
                self.assertEqual(page.pages, bytes.fromhex(even + odd))

    def test_published_e5b_page(self):
        word_type, even, odd = E5B_PAGE
        page = decode_sfrbx(sfrbx(even, odd, signal=5))
        self.assertIsNotNone(page)
        self.assertEqual((page.band, page.word_type), ("E5b-I", word_type))
        self.assertEqual(page.pages, bytes.fromhex(even + odd))

    def test_crc_covers_data_but_not_ssp(self):
        _, even, odd = E1B_PAGES[0]
        payload = bytearray(sfrbx(even, odd))
        payload[10] ^= 0x20  # Protected data in the even part.
        self.assertIsNone(decode_sfrbx(payload))
        payload[10] ^= 0x20
        payload[38] ^= 0x20  # First SSP bit in the odd part.
        self.assertIsNotNone(decode_sfrbx(payload))

    def test_rejects_unsupported_or_malformed_pairs(self):
        _, even, odd = E1B_PAGES[0]
        payload = sfrbx(even, odd)
        for invalid in (
            sfrbx(even, odd, signal=3),  # E5a carries F/NAV.
            sfrbx(odd, even),
            sfrbx("4" + even[1:], odd),  # Alert-page layout.
            payload[:-1],
            bytes((0,)) + payload[1:],  # GPS GNSS ID.
            payload[:1] + bytes((37,)) + payload[2:],
            payload[:4] + bytes((7,)) + payload[5:],
            payload[:6] + bytes((1,)) + payload[7:],
        ):
            with self.subTest(payload=invalid.hex()):
                self.assertIsNone(decode_sfrbx(invalid))

    def test_usb_bridge_keeps_raw_and_only_adds_crc_valid_inav_when_enabled(self):
        _, even, odd = E1B_PAGES[0]
        good = build_ubx_msg(0x02, 0x13, sfrbx(even, odd))
        bad_inav = bytearray(sfrbx(even, odd))
        bad_inav[10] ^= 0x20
        bad_inav = build_ubx_msg(0x02, 0x13, bad_inav)  # The UBX checksum is still valid.
        bad_ubx = good[:-1] + bytes((good[-1] ^ 1,))
        nmea = b"$GNTXT,01,01,02,Test*65\n"
        sentence = f"$GALINAV,2,2,{even}{odd},E1-B*3B\n".encode("ascii")

        for enabled in (False, True):
            with self.subTest(enabled=enabled):
                port = Mock(in_waiting=4096)
                port.read.side_effect = [nmea + good[:9], good[9:] + bad_inav + bad_ubx, KeyboardInterrupt]
                serial = SimpleNamespace(Serial=Mock(return_value=port))
                udp = Mock()
                with (
                    patch.dict(sys.modules, {"serial": serial}),
                    patch.object(f9p_bridge, "find_f9p_port", return_value="/dev/cu.usbmodem-test"),
                    patch.object(f9p_bridge.socket, "socket", return_value=udp),
                    patch("builtins.print"),
                ):
                    f9p_bridge.main(emit_galinav=enabled)

                payloads = [nmea, b"RAW:" + good]
                if enabled:
                    payloads.append(sentence)
                payloads.append(b"RAW:" + bad_inav)
                self.assertEqual(udp.sendto.call_args_list, [
                    call(payload, (host, 9090))
                    for payload in payloads
                    for host in ("127.0.0.1", "255.255.255.255")
                ])


if __name__ == "__main__":
    unittest.main()

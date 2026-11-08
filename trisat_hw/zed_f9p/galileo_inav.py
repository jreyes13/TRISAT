"""Decode nominal Galileo I/NAV page pairs from UBX-RXM-SFRBX v2."""

from dataclasses import dataclass
from functools import reduce
from operator import xor
from struct import pack, unpack


GALILEO = 2
GALILEO_INAV_SIGNALS = {1: "E1-B", 5: "E5b-I"}


@dataclass(frozen=True)
class InavPage:
    svid: int
    band: str
    word_type: int
    pages: bytes

    def sentence(self) -> bytes:
        body = f"GALINAV,{self.svid},{self.word_type},{self.pages.hex().upper()},{self.band}".encode("ascii")
        return b"$" + body + f"*{reduce(xor, body, 0):02X}\n".encode("ascii")


def decode_sfrbx(payload: bytes) -> InavPage | None:
    """Return a CRC-checked nominal E1-B or E5b-I pair from an SFRBX payload."""
    if len(payload) != 40:
        return None

    gnss_id, svid, sig_id, _, num_words, _, version, _ = payload[:8]
    if gnss_id != GALILEO or not 1 <= svid <= 36 or num_words != 8 or version != 2:
        return None
    band = GALILEO_INAV_SIGNALS.get(sig_id)
    if band is None:
        return None

    # Convert each little-endian UBX word to transmission order and remove
    # the eight padding bits that follow each 120-bit page part.
    words = pack(">8I", *unpack("<8I", payload[8:]))
    even, odd = words[:15], words[16:31]
    if even[0] >> 6 != 0 or odd[0] >> 6 != 2:
        return None  # Nominal, even followed by nominal, odd.

    # OS SIS ICD 2.2, Table 38: 114 even bits, then 82 odd bits.
    # The CRC starts at odd bit 82, followed by 8 SSP/reserved bits and 6 tail bits.
    odd_bits = int.from_bytes(odd, "big")
    protected = ((int.from_bytes(even, "big") >> 6) << 82) | (odd_bits >> 38)
    crc = 0
    for index in range(195, -1, -1):
        feedback = (crc >> 23) ^ ((protected >> index) & 1)
        crc = (crc << 1) & 0xFFFFFF
        if feedback:
            crc ^= 0x864CFB
    if crc != ((odd_bits >> 14) & 0xFFFFFF):
        return None

    return InavPage(svid, band, even[0] & 0x3F, even + odd)

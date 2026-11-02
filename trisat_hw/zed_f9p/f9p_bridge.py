#!/usr/bin/env python3
"""
f9p_bridge.py
-------------
High-performance telemetry bridge for u-blox ZED-F9P receiver.
Auto-detects USB ports (/dev/cu.usbmodem*) and broadcasts NMEA + UBX frames
via UDP (:9090) across localhost and local LAN (for iPhone/iPad on Wi-Fi).
"""

import sys
import glob
import time
import socket
import serial

UDP_PORT = 9090

def find_f9p_port():
    candidates = glob.glob("/dev/cu.usbmodem*")
    if candidates:
        return candidates[0]
    return None

def main():
    port = find_f9p_port()
    if not port:
        print("[-] Error: No u-blox ZED-F9P detected on USB (/dev/cu.usbmodem*).")
        print("    Please connect the USB-C cable directly to the red ZED-F9P board.")
        sys.exit(1)

    print("=" * 64)
    print("  TRISAT - u-blox ZED-F9P USB Native Telemetry Bridge")
    print(f"  Target UDP: Broadcast on port :{UDP_PORT}")
    print(f"  Serial Port: {port}")
    print("=" * 64)

    # Setup UDP socket with broadcast enabled so iPhone/iPad on local Wi-Fi can receive
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)

    try:
        ser = serial.Serial(port, 115200, timeout=1.0)
        ser.dtr = True
        ser.rts = True
        print(f"[+] Connected to {port} at 115200 bps")
    except Exception as e:
        print(f"[-] Error opening {port}: {e}")
        sys.exit(1)

    # 1. Poll hardware version
    ser.write(bytes([0xB5, 0x62, 0x0A, 0x04, 0x00, 0x00, 0x0E, 0x34]))

    # 2. Enable NMEA sentences at 1 Hz on USB:
    # GSV (Satellites in view with C/N0 for Galileo, GPS, BeiDou, GLONASS)
    ser.write(bytes([0xB5, 0x62, 0x06, 0x01, 0x08, 0x00, 0xF0, 0x03, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x06, 0x4C]))
    # GGA (Position, fix mode, HDOP)
    ser.write(bytes([0xB5, 0x62, 0x06, 0x01, 0x08, 0x00, 0xF0, 0x00, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x03, 0x39]))
    # RMC (PVT, date/time, speed)
    ser.write(bytes([0xB5, 0x62, 0x06, 0x01, 0x08, 0x00, 0xF0, 0x04, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x07, 0x51]))
    # GSA (DOP and active satellites)
    ser.write(bytes([0xB5, 0x62, 0x06, 0x01, 0x08, 0x00, 0xF0, 0x02, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x05, 0x45]))
    # GST (Pseudorange error and precision)
    ser.write(bytes([0xB5, 0x62, 0x06, 0x01, 0x08, 0x00, 0xF0, 0x07, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x0A, 0x5E]))

    # 3. Enable raw Galileo navigation subframe messages UBX-RXM-SFRBX (1 Hz)
    enable_sfrbx = bytes([0xB5, 0x62, 0x06, 0x01, 0x08, 0x00, 0x02, 0x13, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x25, 0x4B])
    ser.write(enable_sfrbx)
    ser.flush()

    print(f"[+] Live streaming active (Localhost & Wi-Fi Broadcast :9090). Press Ctrl+C to stop.\n")

    try:
        while True:
            line = ser.readline()
            if not line:
                continue

            # NMEA ASCII Sentences
            if line.startswith(b"$"):
                ascii_str = line.decode("ascii", errors="ignore").strip()
                if ascii_str:
                    print(f"[NMEA] {ascii_str}")
                    payload = (ascii_str + "\n").encode("utf-8")
                    # Send to localhost (Mac app)
                    sock.sendto(payload, ("127.0.0.1", UDP_PORT))
                    # Broadcast to Wi-Fi subnet (iPhone app)
                    sock.sendto(payload, ("255.255.255.255", UDP_PORT))

            # UBX Binary Frames
            elif line.startswith(b"\xb5\x62"):
                hex_str = " ".join(f"{b:02X}" for b in line[:16])
                print(f"[UBX-RAW] {hex_str}...")
                raw_payload = b"RAW:" + line
                sock.sendto(raw_payload, ("127.0.0.1", UDP_PORT))
                sock.sendto(raw_payload, ("255.255.255.255", UDP_PORT))

    except KeyboardInterrupt:
        print("\n[+] Bridge stopped by user.")
    finally:
        ser.close()

if __name__ == "__main__":
    main()

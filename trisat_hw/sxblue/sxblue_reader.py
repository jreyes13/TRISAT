#!/usr/bin/env python3
"""
sxblue_reader.py
----------------
Continuous USB telemetry streamer for Geneq SXblue GNSS receiver.
Reads NMEA sentences and raw binary ($BIN) messages via low-level pyusb/libusb,
handles macOS FTDI kernel configuration safeguards, and broadcasts incoming frames
via a local UDP socket (127.0.0.1:9090) to the TRISAT Mac application.
"""

import os
import sys
import socket
import usb.core
import usb.util
import usb.backend.libusb1

# Hardware IDs for Geneq SXblue (FTDI chip)
VENDOR_ID = 0x0403
PRODUCT_ID = 0xB5A0

# FTDI UART parameters
FTDI_BAUD_19200 = 0x009C
FTDI_SET_BAUDRATE_REQ = 0x03
FTDI_RESET_BUFFERS_REQ = 0x00

# Local UDP Bridge configuration
UDP_HOST = "127.0.0.1"
UDP_PORT = 9090

def load_backend():
    """Locate and return a functional libusb backend on macOS."""
    candidate_paths = [
        "/opt/homebrew/lib/libusb-1.0.dylib",
        "/opt/homebrew/opt/libusb/lib/libusb-1.0.dylib",
        "/usr/local/lib/libusb-1.0.dylib",
        "/usr/local/opt/libusb/lib/libusb-1.0.dylib"
    ]
    for lib_path in candidate_paths:
        if os.path.exists(lib_path):
            try:
                b = usb.backend.libusb1.get_backend(find_library=lambda x, p=lib_path: p)
                if b is not None:
                    print(f"[+] Loaded libusb backend from: {lib_path}")
                    return b
            except Exception as err:
                print(f"[-] Warning loading {lib_path}: {err}")

    # Fallback to default dynamic linker resolution
    return usb.backend.libusb1.get_backend()

def main():
    print("=" * 54)
    print(" SXblue Continuous USB Telemetry Daemon (UDP 127.0.0.1:9090)")
    print("=" * 54)

    # Initialize UDP socket broadcaster
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    print(f"[+] UDP Socket Active -> {UDP_HOST}:{UDP_PORT}")

    backend = load_backend()
    if backend is None:
        print("[-] Fatal: No functional libusb backend found.")
        sys.exit(1)

    # Attach to SXblue peripheral
    dev = usb.core.find(idVendor=VENDOR_ID, idProduct=PRODUCT_ID, backend=backend)
    if dev is None:
        print("[-] Error: SXblue receiver not detected on USB bus.")
        sys.exit(1)

    print(f"[+] Attached to SXblue Receiver (VID: {hex(VENDOR_ID)}, PID: {hex(PRODUCT_ID)})")

    # Detach kernel driver if macOS captured the device
    try:
        if dev.is_kernel_driver_active(0):
            dev.detach_kernel_driver(0)
    except Exception:
        pass

    # Safe configuration handling to avoid Errno 19 on macOS
    try:
        cfg = dev.get_active_configuration()
        if cfg is None or cfg.bConfigurationValue != 1:
            dev.set_configuration()
    except usb.core.USBError:
        try:
            dev.set_configuration()
        except Exception as e:
            print(f"[!] Configuration check bypassed: {e}")

    # Set UART line baud rate to 19200 bps
    try:
        dev.ctrl_transfer(0x40, FTDI_SET_BAUDRATE_REQ, FTDI_BAUD_19200, 0, None)
        dev.ctrl_transfer(0x40, FTDI_RESET_BUFFERS_REQ, 0, 0, None)
        print("[+] FTDI UART configured at 19200 bps.")
    except Exception as err:
        print(f"[-] Warning setting UART baud rate: {err}")

    cfg = dev.get_active_configuration()
    intf = cfg[(0, 0)]
    ep_in = usb.util.find_descriptor(
        intf,
        custom_match=lambda e: usb.util.endpoint_direction(e.bEndpointAddress) == usb.util.ENDPOINT_IN
    )

    if ep_in is None:
        print("[-] Fatal: USB IN endpoint not discovered.")
        sys.exit(1)

    print("\n[+] Streaming telemetry (NMEA sentences & raw hex)... Press Ctrl+C to stop.\n")
    stream_buffer = b""

    while True:
        try:
            # FTDI packets include 2 modem status bytes at the beginning
            raw_data = dev.read(ep_in.bEndpointAddress, 64, timeout=1000)
            payload = raw_data[2:] if len(raw_data) > 2 else raw_data

            if not payload:
                continue

            stream_buffer += bytes(payload)

            # Process full newline-terminated messages
            if b"\n" in stream_buffer:
                lines = stream_buffer.split(b"\n")
                for line in lines[:-1]:
                    ascii_str = "".join([chr(b) for b in line if 32 <= b <= 126 or b == 13]).strip()

                    if ascii_str.startswith("$"):
                        print(f"[NMEA]: {ascii_str}")
                        # Broadcast NMEA string via UDP
                        sock.sendto((ascii_str + "\n").encode("utf-8"), (UDP_HOST, UDP_PORT))
                    elif len(line) > 0:
                        hex_str = " ".join(f"{b:02X}" for b in line[:24])
                        print(f"[BIN-RAW]: {hex_str}")
                        # Broadcast binary data with header prefix via UDP
                        sock.sendto(b"RAW:" + bytes(line), (UDP_HOST, UDP_PORT))

                stream_buffer = lines[-1]

        except usb.core.USBTimeoutError:
            pass
        except KeyboardInterrupt:
            print("\n[+] Streaming stopped by user.")
            break

if __name__ == "__main__":
    main()
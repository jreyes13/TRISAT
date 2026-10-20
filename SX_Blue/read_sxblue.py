#!/usr/bin/env python3
"""
SXblue GNSS USB Reader & Binary Configuration Tool
--------------------------------------------------
Low-level USB interface script designed to read NMEA 0183 sentences,
configure the internal FTDI UART line (19200 bps), and enable raw
binary message output ($JBIN,94 / $JBIN,95 / $JSAVE) for Galileo I/NAV inspection.
"""

import os
import sys
import usb.core
import usb.util
import usb.backend.libusb1

# Hardware Vendor and Product IDs identified via macOS System Report
VENDOR_ID = 0x0403
PRODUCT_ID = 0xB5A0

# FTDI UART Baud Rate Divisor for 19200 bps (0x009C = 156)
FTDI_BAUD_19200 = 0x009C
FTDI_SET_BAUDRATE_REQ = 0x03
FTDI_RESET_BUFFERS_REQ = 0x00

def load_libusb_backend():
    """Attempts to locate and load the libusb dynamic library on macOS."""
    candidate_paths = [
        "/opt/homebrew/lib/libusb-1.0.dylib",
        "/opt/homebrew/opt/libusb/lib/libusb-1.0.dylib",
        "/usr/local/lib/libusb-1.0.dylib",
        "/usr/local/opt/libusb/lib/libusb-1.0.dylib"
    ]
    
    for lib_path in candidate_paths:
        if os.path.exists(lib_path):
            try:
                backend = usb.backend.libusb1.get_backend(find_library=lambda x, p=lib_path: p)
                if backend is not None:
                    print(f"[+] Loaded libusb backend from: {lib_path}")
                    return backend
            except Exception as err:
                print(f"[-] Warning: Failed to load {lib_path}: {err}")
                
    # Fallback to standard system library resolution
    return usb.backend.libusb1.get_backend()

def main():
    print("[*] Initializing SXblue USB Interface...")

    backend = load_libusb_backend()
    if backend is None:
        print("[-] Fatal: No functional libusb backend found. Please install libusb.")
        sys.exit(1)

    # Locate the SXblue USB peripheral
    dev = usb.core.find(idVendor=VENDOR_ID, idProduct=PRODUCT_ID, backend=backend)
    if dev is None:
        print("[-] Fatal: SXblue receiver not detected on USB bus. Check cable and power.")
        sys.exit(1)

    print(f"[+] Successfully attached to device (VID: {hex(VENDOR_ID)}, PID: {hex(PRODUCT_ID)})")

    # Detach native kernel driver if active
    try:
        if dev.is_kernel_driver_active(0):
            dev.detach_kernel_driver(0)
    except Exception:
        pass

    # Apply configuration
    dev.set_configuration()

    # Configure FTDI line parameters (19200 baud rate & reset RX/TX FIFO buffers)
    try:
        dev.ctrl_transfer(0x40, FTDI_SET_BAUDRATE_REQ, FTDI_BAUD_19200, 0, None)
        dev.ctrl_transfer(0x40, FTDI_RESET_BUFFERS_REQ, 0, 0, None)
        print("[+] FTDI UART configured at 19200 bps.")
    except Exception as err:
        print(f"[-] Warning configuring UART baud rate: {err}")

    cfg = dev.get_active_configuration()
    intf = cfg[(0, 0)]

    # Locate IN and OUT bulk endpoints
    ep_in = usb.util.find_descriptor(
        intf,
        custom_match=lambda e: usb.util.endpoint_direction(e.bEndpointAddress) == usb.util.ENDPOINT_IN
    )
    ep_out = usb.util.find_descriptor(
        intf,
        custom_match=lambda e: usb.util.endpoint_direction(e.bEndpointAddress) == usb.util.ENDPOINT_OUT
    )

    if ep_in is None:
        print("[-] Fatal: IN endpoint not discovered on interface 0.")
        sys.exit(1)

    # Transmit binary activation and polling commands
    if ep_out is not None:
        try:
            setup_commands = [
                b"$JBIN,94,1\r\n",  # Enable ephemeris and navigation subframe streaming (1 Hz)
                b"$JBIN,95,1\r\n",  # Enable raw pseudorange and carrier phase observations (1 Hz)
                b"$JSAVE\r\n",      # Persist configuration to internal non-volatile EEPROM
                b"$JSHOW\r\n"       # Query complete port and message configuration status
            ]
            for cmd in setup_commands:
                ep_out.write(cmd)
                print(f"[+] Sent command: {cmd.strip().decode(errors='replace')}")
        except Exception as err:
            print(f"[-] Failed to transmit setup commands: {err}")

    print("\n[+] Streaming incoming telemetry (NMEA sentences & raw hex)... Press Ctrl+C to stop.\n")
    
    stream_buffer = b""

    while True:
        try:
            # FTDI packets include 2 modem status bytes at the beginning
            raw_packet = dev.read(ep_in.bEndpointAddress, 64, timeout=1000)
            payload = raw_packet[2:] if len(raw_packet) > 2 else raw_packet

            if not payload:
                continue

            stream_buffer += bytes(payload)

            # Process full newline-terminated messages
            if b"\n" in stream_buffer:
                lines = stream_buffer.split(b"\n")
                for line in lines[:-1]:
                    ascii_repr = "".join([chr(b) for b in line if 32 <= b <= 126 or b == 13]).strip()
                    if ascii_repr.startswith("$"):
                        print(f"[NMEA]: {ascii_repr}")
                    elif len(line) > 0:
                        hex_repr = " ".join(f"{b:02X}" for b in line[:24])
                        print(f"[RAW-HEX]: {hex_repr}")
                stream_buffer = lines[-1]

        except usb.core.USBTimeoutError:
            pass
        except KeyboardInterrupt:
            print("\n[+] Telemetry capture terminated by user.")
            break

if __name__ == "__main__":
    main()
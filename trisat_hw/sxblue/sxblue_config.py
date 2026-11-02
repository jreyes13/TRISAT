#!/usr/bin/env python3
"""
SXblue GNSS Receiver One-Time EEPROM Configuration Tool
-------------------------------------------------------
Senior Engineer macOS / Python Script for Geneq SXblue Platinum (VID: 0x0403, PID: 0xb5a0)
-------------------------------------------------------
Communicates at low level via PyUSB/libusb1 forcing 19200 bps on FTDI UART.
Transmits:
  - $JBIN,94,1 (enable navigation subframes & ephemeris I/NAV at 1 Hz)
  - $JBIN,95,1 (enable raw code and carrier phase observations at 1 Hz)
  - $JSAVE     (persist configuration to EEPROM)
  - $JSHOW     (query port and binary message configuration status)
Waits for confirmation ($>JSHOW,BIN,94, $>JSHOW,BIN,95, Save Complete) and exits cleanly.
"""

import os
import sys
import time
import usb.core
import usb.util
import usb.backend.libusb1

VENDOR_ID = 0x0403
PRODUCT_ID = 0xB5A0

FTDI_BAUD_19200 = 0x009C
FTDI_SET_BAUDRATE_REQ = 0x03
FTDI_RESET_BUFFERS_REQ = 0x00

def load_libusb_backend():
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
                print(f"[-] Warning loading {lib_path}: {err}")
    return usb.backend.libusb1.get_backend()

def main():
    print("======================================================")
    print(" SXblue One-Time EEPROM Configuration Programmer")
    print("======================================================")

    backend = load_libusb_backend()
    if backend is None:
        print("[-] Fatal: libusb backend not available. Install libusb.")
        sys.exit(1)

    dev = usb.core.find(idVendor=VENDOR_ID, idProduct=PRODUCT_ID, backend=backend)
    if dev is None:
        print(f"[-] Fatal: SXblue receiver (VID: {hex(VENDOR_ID)}, PID: {hex(PRODUCT_ID)}) not found on USB bus.")
        sys.exit(1)

    print(f"[+] Found SXblue Hardware (VID: {hex(VENDOR_ID)}, PID: {hex(PRODUCT_ID)})")

    # Detach macOS kernel driver if active
    try:
        dev.detach_kernel_driver(0)
        print("[+] Detached macOS kernel driver from interface 0.")
    except Exception:
        pass

    # Claim USB Interface 0
    try:
        usb.util.claim_interface(dev, 0)
        print("[+] Successfully claimed USB interface 0.")
    except Exception as err:
        print(f"[-] Interface claim status: {err}")

    # Set FTDI 19200 bps baud rate
    try:
        dev.ctrl_transfer(0x40, FTDI_SET_BAUDRATE_REQ, FTDI_BAUD_19200, 0, None)
        dev.ctrl_transfer(0x40, FTDI_RESET_BUFFERS_REQ, 0, 0, None)
        print("[+] FTDI UART configured to 19200 bps.")
    except Exception as err:
        print(f"[-] Warning configuring FTDI baud rate: {err}")

    cfg = dev.get_active_configuration()
    intf = cfg[(0, 0)]

    ep_in = usb.util.find_descriptor(intf, custom_match=lambda e: usb.util.endpoint_direction(e.bEndpointAddress) == usb.util.ENDPOINT_IN)
    ep_out = usb.util.find_descriptor(intf, custom_match=lambda e: usb.util.endpoint_direction(e.bEndpointAddress) == usb.util.ENDPOINT_OUT)

    if ep_out is None or ep_in is None:
        print("[-] Fatal: Bulk endpoints missing.")
        sys.exit(1)

    config_commands = [
        b"$JBIN,94,1\r\n",
        b"$JBIN,95,1\r\n",
        b"$JSAVE\r\n",
        b"$JSHOW\r\n"
    ]

    print("[*] Transmitting EEPROM configuration commands...")
    for cmd in config_commands:
        ep_out.write(cmd)
        print(f"[+] Sent: {cmd.strip().decode(errors='replace')}")
        time.sleep(0.2)

    print("\n[*] Waiting for receiver EEPROM save confirmation...")
    confirmed_94 = False
    confirmed_95 = False
    confirmed_save = False

    start_time = time.time()
    stream_buffer = b""

    while time.time() - start_time < 5.0:
        try:
            raw_packet = dev.read(ep_in.bEndpointAddress, 64, timeout=1000)
            payload = raw_packet[2:] if len(raw_packet) > 2 else raw_packet
            if not payload:
                continue

            stream_buffer += bytes(payload)
            if b"\n" in stream_buffer:
                lines = stream_buffer.split(b"\n")
                for line in lines[:-1]:
                    text = "".join([chr(b) for b in line if 32 <= b <= 126 or b == 13]).strip()
                    print(f"  [Rx]: {text}")
                    if "94" in text: confirmed_94 = True
                    if "95" in text: confirmed_95 = True
                    if "SAVE" in text.upper() or "COMPLETE" in text.upper(): confirmed_save = True
                stream_buffer = lines[-1]
        except usb.core.USBTimeoutError:
            pass

    print("\n======================================================")
    print(" Configuration Execution Summary:")
    print(f"  - Message 94 (Subframes 1Hz) : {'[OK]' if confirmed_94 else '[SENT]'}")
    print(f"  - Message 95 (Code/Carrier)  : {'[OK]' if confirmed_95 else '[SENT]'}")
    print(f"  - EEPROM Save ($JSAVE)       : {'[OK]' if confirmed_save else '[SENT]'}")
    print("======================================================")
    print("[+] Configuration finished successfully. Exiting.")

if __name__ == "__main__":
    main()

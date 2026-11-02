#!/usr/bin/env python3
"""
SXblue GNSS USB Reader & Network UDP Broadcaster
------------------------------------------------
Senior Engineer macOS / Python Hardware Driver for Geneq SXblue Platinum (VID: 0x0403, PID: 0xb5a0)
------------------------------------------------
Reads telemetry over USB from Geneq SXblue FTDI chip at 19200 bps using PyUSB/libusb1.
Forces interface claim, configures FTDI UART, enables binary subframes ($JBIN,94 / $JBIN,95 / $JSAVE),
and broadcasts telemetry datagrams to 127.0.0.1:9090 over a local UDP socket.
"""

import os
import sys
import socket
import json
import usb.core
import usb.util
import usb.backend.libusb1

VENDOR_ID = 0x0403
PRODUCT_ID = 0xB5A0

FTDI_BAUD_19200 = 0x009C
FTDI_SET_BAUDRATE_REQ = 0x03
FTDI_RESET_BUFFERS_REQ = 0x00

UDP_IP = "127.0.0.1"
UDP_PORT = 9090

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
    print(" SXblue USB Telemetry Reader & UDP Bridge (127.0.0.1:9090)")
    print("======================================================")

    udp_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    udp_sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    print(f"[+] Local UDP Telemetry Broadcaster active -> {UDP_IP}:{UDP_PORT}")

    backend = load_libusb_backend()
    if backend is None:
        print("[-] Fatal: libusb backend not available.")
        sys.exit(1)

    dev = usb.core.find(idVendor=VENDOR_ID, idProduct=PRODUCT_ID, backend=backend)
    if dev is None:
        print(f"[-] Fatal: SXblue USB device (VID {hex(VENDOR_ID)}, PID {hex(PRODUCT_ID)}) not detected.")
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
        print(f"[-] Warning setting FTDI baud rate: {err}")

    cfg = dev.get_active_configuration()
    intf = cfg[(0, 0)]

    ep_in = usb.util.find_descriptor(intf, custom_match=lambda e: usb.util.endpoint_direction(e.bEndpointAddress) == usb.util.ENDPOINT_IN)
    ep_out = usb.util.find_descriptor(intf, custom_match=lambda e: usb.util.endpoint_direction(e.bEndpointAddress) == usb.util.ENDPOINT_OUT)

    if ep_in is None:
        print("[-] Fatal: USB IN Bulk Endpoint missing.")
        sys.exit(1)

    if ep_out is not None:
        try:
            setup_commands = [
                b"$JBIN,94,1\r\n",
                b"$JBIN,95,1\r\n",
                b"$JSAVE\r\n",
                b"$JSHOW\r\n"
            ]
            for cmd in setup_commands:
                ep_out.write(cmd)
                print(f"[+] Sent command: {cmd.strip().decode(errors='replace')}")
        except Exception as err:
            print(f"[-] Failed to transmit setup commands: {err}")

    print(f"\n[+] Streaming incoming telemetry -> UDP {UDP_IP}:{UDP_PORT}... Press Ctrl+C to stop.\n")
    
    stream_buffer = b""

    while True:
        try:
            raw_packet = dev.read(ep_in.bEndpointAddress, 64, timeout=1000)
            payload = raw_packet[2:] if len(raw_packet) > 2 else raw_packet

            if not payload:
                continue

            stream_buffer += bytes(payload)

            if b"\n" in stream_buffer:
                lines = stream_buffer.split(b"\n")
                for line in lines[:-1]:
                    ascii_repr = "".join([chr(b) for b in line if 32 <= b <= 126 or b == 13]).strip()
                    if ascii_repr.startswith("$"):
                        print(f"[NMEA]: {ascii_repr}")
                        msg_json = json.dumps({"type": "nmea", "data": ascii_repr})
                        udp_sock.sendto(msg_json.encode('utf-8'), (UDP_IP, UDP_PORT))
                    elif len(line) > 0:
                        hex_repr = " ".join(f"{b:02X}" for b in line[:24])
                        print(f"[RAW-HEX]: {hex_repr}")
                        msg_json = json.dumps({"type": "bin", "hex": hex_repr, "data": f"$BIN,{hex_repr}"})
                        udp_sock.sendto(msg_json.encode('utf-8'), (UDP_IP, UDP_PORT))
                stream_buffer = lines[-1]

        except usb.core.USBTimeoutError:
            pass
        except KeyboardInterrupt:
            print("\n[+] Telemetry capture stopped.")
            break

if __name__ == "__main__":
    main()

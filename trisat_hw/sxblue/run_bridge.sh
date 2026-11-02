#!/bin/bash
# TRISAT SXblue USB Telemetry Daemon Launcher (x86_64 Rosetta & Root USB Privileges)
echo "======================================================"
echo " TRISAT SXblue USB Telemetry Daemon (Localhost UDP 9090)"
echo "======================================================"
echo "[*] Elevating privileges for PyUSB/libusb raw USB access..."
sudo arch -x86_64 python3 "$(dirname "$0")/sxblue_reader.py" "$@"

# TRISAT Hardware Bridges & Adapters (`trisat_hw`)

Este directorio centraliza los controladores, scripts puente de telemetría y firmwares de hardware de todos los receptores GNSS físicos de la suite **TRISAT** (macOS, iOS, Android).

---

## 📁 Estructura del Directorio

```
trisat_hw/
├── sxblue/                        # Receptor profesional Geneq SXblue (USB FTDI)
│   ├── sxblue_reader.py           # Daemon lector USB y emisor UDP 127.0.0.1:9090
│   ├── sxblue_config.py           # Programador EEPROM para activar tramas I/NAV ($JBIN)
│   ├── run_bridge.sh              # Script lanzador del daemon con privilegios
│   └── PDFs/ & SDK/               # Documentación técnica, manuales y SDK original
│
├── zed_f9p/                       # Receptor de alta precisión u-blox ZED-F9P (USB Nativo)
│   ├── f9p_bridge.py              # Puente USB CDC/ACM (/dev/cu.usbmodem*) hacia UDP :9090
│   └── f9p_factory_reset.py       # Restauración completa y optimización permanente en Flash
│
└── esp32/                         # Módulos de firmware para ESP32
    ├── esp32_hybrid_bridge/       # Firmware Arduino (BLE Nordic UART NUS + WiFi AP UDP :9090)
    │   └── esp32_hybrid_bridge.ino
    └── esp32_sniffer/             # Diagnóstico de líneas lógicas, SPI y baud rates
        └── esp32_sniffer.ino
```

---

## 1. Geneq SXblue (`trisat_hw/sxblue`)
* **Hardware:** Receptor sub-métrico Geneq SXblue II/II+ con chip USB FTDI (`VID: 0x0403, PID: 0xb5a0`).
* **Configuración EEPROM (Una vez):**
  ```bash
  sudo python3 trisat_hw/sxblue/sxblue_config.py
  ```
* **Lanzar el daemon hacia TRISAT:**
  ```bash
  sudo ./trisat_hw/sxblue/run_bridge.sh
  # O bien:
  sudo python3 trisat_hw/sxblue/sxblue_reader.py
  ```

---

## 2. u-blox ZED-F9P (`trisat_hw/zed_f9p`)
* **Hardware:** Placa SparkFun GPS-RTK-SMA con u-blox ZED-F9P (Firmware HPG 1.13, multiconstelación Galileo E1/E5b, GPS, GLONASS, BeiDou).
* **Conexión:** Cable USB-C directo a la placa roja ZED-F9P (`/dev/cu.usbmodem*`).
* **Lanzar el daemon hacia TRISAT:**
  ```bash
  python3 trisat_hw/zed_f9p/f9p_bridge.py
  ```
  Habilita automáticamente tramas NMEA y paquetes binarios de navegación cruda `UBX-RXM-SFRBX` (Galileo OSNMA).

---

## 3. Módulo ESP32 Híbrido (`trisat_hw/esp32/esp32_hybrid_bridge`)
* **Hardware:** Placa ESP32-D0WDQ6 con puerto USB CH340 (`/dev/cu.usbserial*`).
* **Conexión física:** Conectada internamente a la UART del ZED-F9P (GPIO 19 RX / GPIO 18 TX).
* **Capacidades:**
  * **Bluetooth Low Energy (BLE):** Publica el servicio estándar *Nordic UART Service (NUS)* como `TRISAT-GNSS-BLE`. Conexión nativa con iPhone, iPad y Mac sin requerir licencias Apple MFi.
  * **WiFi Access Point:** Creates WiFi network `TRISAT-GNSS` (WPA2: `trisat4ever`) and broadcasts NMEA sentences via **UDP Broadcast to port `9090`**.
  * **Consola Serie USB:** Salida en paralelo a 115200 bps para depuración en Mac.

### Instrucciones de carga (Arduino IDE)
1. Abrir `esp32_hybrid_bridge/esp32_hybrid_bridge.ino` en **Arduino IDE**.
2. Seleccionar placa: **ESP32 Dev Module**.
3. Seleccionar puerto: `/dev/cu.usbserial-130` (con el cable USB blanco conectado al ESP32).
4. Subir el sketch.

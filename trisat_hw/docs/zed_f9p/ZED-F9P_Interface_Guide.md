# Guía Rápida de Protocolo e Interfaces: u-blox ZED-F9P

Esta guía resume las especificaciones de interfaz, formatos de mensajes binarios (UBX), sentencias NMEA y configuración de puertos del receptor **u-blox ZED-F9P** utilizado en TRISAT.

---

## 1. Puertos de Comunicación del ZED-F9P

El módulo ZED-F9P dispone de múltiples interfaces de comunicación simultáneas:
1. **USB Nativo (CDC/ACM):** Puerto directo Full-Speed (12 Mbit/s) expuesto como `/dev/cu.usbmodem*` en macOS.
2. **UART1:** Puerto serie primario (TTL 3.3V) configurado de fábrica a 38400 bps o 115200 bps.
3. **UART2:** Puerto serie secundario (TTL 3.3V) utilizado habitualmente para la entrada/salida de correcciones RTCM o conexión a microcontroladores auxiliares (conectado a los pines GPIO 16/17 del ESP32).
4. **I2C / SPI:** Interfaces síncronas para microcontroladores.

---

## 2. Formato de Paquete Binario UBX

Todos los paquetes UBX siguen la siguiente estructura en bytes:

| Campo | Longitud (Bytes) | Valor / Descripción |
| :--- | :--- | :--- |
| **Sync Char 1** | 1 | `0xB5` (carácter constante 'µ') |
| **Sync Char 2** | 1 | `0x62` (carácter constante 'b') |
| **Class** | 1 | Clase del mensaje (ej: `0x01` NAV, `0x02` RXM, `0x06` CFG, `0x0A` MON) |
| **ID** | 1 | Identificador dentro de la clase |
| **Length** | 2 | Longitud del payload en little-endian (sin contar cabecera ni checksum) |
| **Payload** | *Length* | Datos específicos del mensaje |
| **CK_A / CK_B** | 2 | Checksum de 8 bits (algoritmo Fletcher de 8 bits) calculado sobre Class, ID, Length y Payload |

---

## 3. Mensajes Clave para TRISAT y Galileo OSNMA

### A. `UBX-RXM-SFRBX` (Class: `0x02`, ID: `0x13`) — Tramas de Navegación Crudas
* **Propósito:** Entrega las páginas completas de la señal de navegación en el espacio (**Signal-In-Space**).
* **Para Galileo:** Contiene las palabras de **I/NAV (E1-B / E5b)** de 250 bits, incluyendo los **40 bits reservados de OSNMA** (HKROOT, claves y firmas criptográficas TESLA/MACK).
* **Comando para activarlo a 1 Hz en el puerto actual:**
  ```hex
  B5 62 06 01 08 00 02 13 00 01 00 00 00 00 25 4B
  ```

### B. `UBX-RXM-RAWX` (Class: `0x02`, ID: `0x15`) — Mediciones de Pseudorango y Fase de Portadora
* **Propósito:** Mediciones crudas multi-frecuencia (L1/L2/E1/E5b) para cálculo RTK, PPK y análisis de relación señal-ruido ($C/N_0$).

### C. `UBX-NAV-PVT` (Class: `0x01`, ID: `0x07`) — Posición, Velocidad y Tiempo Completo
* **Propósito:** Fix de navegación integrado de alta precisión a frecuencias de hasta 10 Hz / 20 Hz, con desglose de validez y precisión milimétrica.

### D. `UBX-MON-VER` (Class: `0x0A`, ID: `0x04`) — Petición de Versión de Hardware/Firmware
* **Sondeo (Poll):**
  ```hex
  B5 62 0A 04 00 00 0E 34
  ```

---

## 4. Sentencias NMEA Estándar Emitidas

Por defecto en firmware HPG, el ZED-F9P emite sentencias con prefijos multiconstelación:
* **`$GA...`**: Satélites de la constelación europea **Galileo** (`$GAGGA`, `$GARMC`, `$GAGSV`, `$GAGSA`).
* **`$GP...`**: Satélites de la constelación estadounidense **GPS** (`$GPGGA`, `$GPGSV`).
* **`$GL...`**: Satélites de la constelación rusa **GLONASS**.
* **`$BD...` / `$GB...`**: Satélites de la constelación china **BeiDou**.
* **`$GN...`**: Soluciones combinadas multi-constelación (`$GNGGA`, `$GNRMC`).

---

## 5. Referencias y Descarga de Documentación Oficial
* [Datasheet oficial del ZED-F9P (PDF)](file:///Users/jreyes13/GitHub/TRISAT/trisat_hw/docs/zed_f9p/ZED-F9P_DataSheet.pdf) (incluido en esta carpeta).
* Portal oficial de u-blox: [u-blox ZED-F9P Product Resources](https://www.u-blox.com/en/product/zed-f9p-module).

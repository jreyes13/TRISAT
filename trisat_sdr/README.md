# TRISAT-SDR — Multi-Band Software-Defined GNSS Receiver

`trisat_sdr/` es el receptor GNSS definido por software (SDR) multiplataforma (**macOS, GNU/Linux y Windows**) del ecosistema **TRISAT**, diseñado específicamente para operar con **uno o dos HackRF One sincronizados** (además de RTL-SDR, PlutoSDR, USRP vía SoapySDR o archivos IQ) en las bandas:

* **Galileo E1B/C ($1575.42\text{ MHz}$, $\text{CBOC}(6,1,1/11)$):** Adquisición PCPS, seguimiento DLL/PLL de 5 correladores ($VE, E, P, L, VL$), demodulación de páginas **I/NAV** ($250\text{ bps}$), verificación **CRC-24Q** y extracción de los **40 bits criptográficos OSNMA (`HKROOT` + `MACK`)**.
* **Galileo E6B/C ($1278.75\text{ MHz}$, $\text{BPSK}(5)$):** Demodulación de páginas **C/NAV** ($1000\text{ sps}$) y extracción de correcciones **Galileo HAS (High Accuracy Service PPP)** de órbita y reloj a nivel centimétrico.
* **Galileo E5a ($1176.45\text{ MHz}$), E5b ($1207.14\text{ MHz}$) y E5 Full AltBOC(15,10) ($1191.795\text{ MHz}$):** Combinación ionosféricamente libre ($\text{Iono-Free}$) con E1 y demodulación F/NAV e I/NAV.
* **GPS L1 C/A ($1575.42\text{ MHz}$, $\text{BPSK}(1)$):** Adquisición y seguimiento simultáneo en el mismo front-end RF que Galileo E1.

---

## 🔗 Conexión Hardware Dual-HackRF (E1 + E6 / E5)

Con **dos HackRF One**, puedes recibir simultáneamente **Galileo E1 (OSNMA)** en el primer HackRF y **Galileo E6 (HAS)** o **Galileo E5** en el segundo HackRF manteniendo coherencia de fase y reloj:

1. **Sincronización de Reloj (`10 MHz`):** Conecta un cable coaxial SMA corto desde el puerto **`CLKOUT`** del **HackRF #0 (Master)** al puerto **`CLKIN`** del **HackRF #1 (Slave)**.
2. **Alimentación de Antena Activa (`Bias-T 3.3V`):** Conecta tu antena GNSS activa multibanda (L1/E1 + L5/E5 + E6) a través de un splitter RF (con bloqueo DC en una rama) o dos antenas activas. `TRISAT-SDR` activa automáticamente el **Bias-T (`-b 1`)** en ambos HackRF.

---

## 🚀 Cómo Ejecutar `TRISAT-SDR` (macOS / Linux / Windows)

No requiere dependencias obligatorias para arrancar (incluye motor FFT radix-2 puro en Python y acelera automáticamente con `NumPy` / `SoapySDR` / `hackrf_transfer` si están instalados):

```bash
# 1. Arrancar el receptor Dual-SDR + Dashboard Web Interactivo (http://127.0.0.1:8080)
python3 trisat_sdr/main.py

# 2. Elegir la banda del segundo HackRF (GAL_E6, GAL_E5A, GAL_E5B, GAL_E5_ALTBOC)
python3 trisat_sdr/main.py --band2 GAL_E5_ALTBOC

# 3. Modo consola (Headless) enviando tramas por UDP/TCP/Puerto Serie Virtual
python3 trisat_sdr/main.py --headless
```

---

## 📡 Puente de Telemetría hacia TRISAT (`trisat_ios` / macOS / `trisat_android`)

`TRISAT-SDR` emite en tiempo real todas las tramas demoduladas por tres canales simultáneos:
1. **Servidor UDP (`127.0.0.1:5005`)** y **Servidor TCP (`0.0.0.0:5006`)**
2. **Puerto Serie Virtual POSIX (`PTY`, ej. `/dev/ttys004`) en macOS/Linux**, directamente seleccionable desde `MacSerialPortManager` en la app nativa de macOS/iOS.
3. **Formatos de Trama Emitidos:**
   * `$GALINAV,<svid>,<wordType>,<hex30Bytes>,<osnmaHkroot><osnmaMack>*CS`
   * `$GALE6HAS,<svid>,<pageId>,<orbitCorrCm>,<hexPayload>*CS`
   * `$GALE5,<band>,<svid>,<pageType>,<hexPayload>*CS`
   * `$GNGGA`, `$GAGSV`, `$GPGSV` y objetos JSON compatibles con el puente BLE de TRISAT.

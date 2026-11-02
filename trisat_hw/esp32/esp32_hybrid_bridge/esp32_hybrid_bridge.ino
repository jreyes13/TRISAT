#include <WiFi.h>
#include <WiFiUdp.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLE2902.h>

// =========================================================================
// TRISAT ESP32 Hybrid Telemetry Bridge
// Single-Frequency NMEA + Paced Galileo I/NAV (OSNMA) Telemetry
// =========================================================================
// Verified hardware wiring:
// PIN 19: Orange Wire (u-blox TX -> ESP32 RX)
// PIN 18: White Wire  (u-blox RX <- ESP32 TX)
// Baud rate: 115200 bps
// =========================================================================
#define RXD2 19
#define TXD2 18
#define GNSS_BAUD 115200

// WiFi Configuration
const char* AP_SSID = "TRISAT-GNSS";
const char* AP_PASS = "trisat4ever";

WiFiUDP udp;
const int UDP_PORT = 9090;
IPAddress broadcastIp(192, 168, 4, 255);

// BLE Nordic UART Service (NUS)
#define SERVICE_UUID           "6E400001-B5A3-F393-E0A9-E50E24DCCA9E"
#define CHARACTERISTIC_UUID_RX "6E400002-B5A3-F393-E0A9-E50E24DCCA9E"
#define CHARACTERISTIC_UUID_TX "6E400003-B5A3-F393-E0A9-E50E24DCCA9E"

BLEServer *pServer = NULL;
BLECharacteristic *pTxCharacteristic = NULL;
bool deviceConnected = false;
bool oldDeviceConnected = false;

class MyServerCallbacks: public BLEServerCallbacks {
    void onConnect(BLEServer* pServer) {
      deviceConnected = true;
      Serial.println("\n[BLE] Client connected (iPhone / iPad / macOS)!");
    };
    void onDisconnect(BLEServer* pServer) {
      deviceConnected = false;
      Serial.println("\n[BLE] Client disconnected.");
    }
};

// =========================================================================
// UBX-RXM-SFRBX Binary Decoder (Galileo I/NAV 250-bit subframe extractor)
// =========================================================================
enum UbxState {
  UBX_WAIT_SYNC1,
  UBX_WAIT_SYNC2,
  UBX_CLASS,
  UBX_ID,
  UBX_LEN_LSB,
  UBX_LEN_MSB,
  UBX_PAYLOAD,
  UBX_CK_A,
  UBX_CK_B
};

UbxState ubxState = UBX_WAIT_SYNC1;
uint8_t ubxClass = 0;
uint8_t ubxId = 0;
uint16_t ubxLen = 0;
uint16_t ubxPayloadIdx = 0;
uint8_t ubxPayload[256];
uint8_t ubxCkA = 0;
uint8_t ubxCkB = 0;
uint8_t calcCkA = 0;
uint8_t calcCkB = 0;

unsigned long lastInavBleMs = 0;

void handleUbxPacket(uint8_t mClass, uint8_t mId, uint16_t len, const uint8_t* payload) {
  // UBX-RXM-SFRBX (Class 0x02, ID 0x13): Raw Navigation Subframes
  if (mClass == 0x02 && mId == 0x13 && len >= 8) {
    uint8_t gnssId = payload[0];
    uint8_t svId   = payload[1];
    uint8_t numWords = payload[4];

    // Galileo (gnssId == 2) with 8 words (250 bits nominal)
    if (gnssId == 2 && numWords >= 8) {
      int dataBytes = min((int)(numWords * 4), 32);
      const uint8_t* frameBytes = payload + 8;

      // Extract Galileo Word Type from Word 0 (bits 2..7)
      uint32_t w0 = (uint32_t)frameBytes[0] |
                    ((uint32_t)frameBytes[1] << 8) |
                    ((uint32_t)frameBytes[2] << 16) |
                    ((uint32_t)frameBytes[3] << 24);
      uint8_t wordType = (w0 >> 2) & 0x3F;
      if (wordType == 0) wordType = 1;

      // Convert payload bytes to hexadecimal string
      char hexBuf[70];
      for (int i = 0; i < dataBytes; i++) {
        sprintf(hexBuf + (i * 2), "%02X", frameBytes[i]);
      }
      hexBuf[dataBytes * 2] = '\0';

      // Format standard $GALINAV sentence for TRISAT
      String inavLine = "$GALINAV," + String(svId) + "," + String(wordType) + "," + String(hexBuf);
      uint8_t chk = 0;
      for (size_t i = 1; i < inavLine.length(); i++) {
        chk ^= (uint8_t)inavLine[i];
      }
      char chkStr[10];
      sprintf(chkStr, "*%02X\n", chk);
      inavLine += chkStr;

      // Broadcast to WiFi UDP
      udp.beginPacket(broadcastIp, UDP_PORT);
      udp.write((const uint8_t*)inavLine.c_str(), inavLine.length());
      udp.endPacket();

      // Log to Serial Monitor
      Serial.printf("[GALILEO I/NAV] E%02d (WT %02d): %s\n", svId, wordType, hexBuf);

      // Pacing: Stream at most 1 I/NAV frame every 1200ms to BLE to prevent buffer congestion
      if (deviceConnected && (millis() - lastInavBleMs >= 1200)) {
        lastInavBleMs = millis();
        int inavLen = inavLine.length();
        int offset = 0;
        while (offset < inavLen) {
          int chunk = min(20, inavLen - offset);
          pTxCharacteristic->setValue((uint8_t*)(inavLine.c_str() + offset), chunk);
          pTxCharacteristic->notify();
          offset += chunk;
          delay(4);
        }
      }
    }
  }
}

void processUbxByte(uint8_t b) {
  switch (ubxState) {
    case UBX_WAIT_SYNC1:
      if (b == 0xB5) ubxState = UBX_WAIT_SYNC2;
      break;
    case UBX_WAIT_SYNC2:
      if (b == 0x62) {
        ubxState = UBX_CLASS;
        calcCkA = 0;
        calcCkB = 0;
      } else {
        ubxState = (b == 0xB5) ? UBX_WAIT_SYNC2 : UBX_WAIT_SYNC1;
      }
      break;
    case UBX_CLASS:
      ubxClass = b;
      calcCkA += b; calcCkB += calcCkA;
      ubxState = UBX_ID;
      break;
    case UBX_ID:
      ubxId = b;
      calcCkA += b; calcCkB += calcCkA;
      ubxState = UBX_LEN_LSB;
      break;
    case UBX_LEN_LSB:
      ubxLen = b;
      calcCkA += b; calcCkB += calcCkA;
      ubxState = UBX_LEN_MSB;
      break;
    case UBX_LEN_MSB:
      ubxLen |= ((uint16_t)b << 8);
      calcCkA += b; calcCkB += calcCkA;
      ubxPayloadIdx = 0;
      if (ubxLen > 256) {
        ubxState = UBX_WAIT_SYNC1;
      } else if (ubxLen == 0) {
        ubxState = UBX_CK_A;
      } else {
        ubxState = UBX_PAYLOAD;
      }
      break;
    case UBX_PAYLOAD:
      if (ubxPayloadIdx < sizeof(ubxPayload)) {
        ubxPayload[ubxPayloadIdx] = b;
      }
      ubxPayloadIdx++;
      calcCkA += b; calcCkB += calcCkA;
      if (ubxPayloadIdx >= ubxLen) {
        ubxState = UBX_CK_A;
      }
      break;
    case UBX_CK_A:
      ubxCkA = b;
      ubxState = UBX_CK_B;
      break;
    case UBX_CK_B:
      ubxCkB = b;
      if (calcCkA == ubxCkA && calcCkB == ubxCkB) {
        handleUbxPacket(ubxClass, ubxId, ubxLen, ubxPayload);
      }
      ubxState = UBX_WAIT_SYNC1;
      break;
  }
}

// =========================================================================
// Setup
// =========================================================================
void setup() {
  Serial.begin(115200);
  delay(500);
  Serial.println("\n==========================================");
  Serial.println("  TRISAT ESP32 Bridge (NMEA + I/NAV Paced) ");
  Serial.println("==========================================");

  // 1. Hardware UART2 towards u-blox at 115200 bps with expanded 4KB buffer
  Serial2.setRxBufferSize(4096);
  Serial2.begin(GNSS_BAUD, SERIAL_8N1, RXD2, TXD2);

  // 2. Initialize WiFi Access Point
  WiFi.mode(WIFI_AP);
  WiFi.softAP(AP_SSID, AP_PASS);
  udp.begin(UDP_PORT);
  Serial.printf("[+] UDP Streaming on port :%d\n", UDP_PORT);

  // 3. Initialize BLE (Nordic UART Service)
  BLEDevice::init("TRISAT-GNSS-BLE");
  pServer = BLEDevice::createServer();
  pServer->setCallbacks(new MyServerCallbacks());

  BLEService *pService = pServer->createService(SERVICE_UUID);
  pTxCharacteristic = pService->createCharacteristic(
                        CHARACTERISTIC_UUID_TX,
                        BLECharacteristic::PROPERTY_NOTIFY
                      );
  pTxCharacteristic->addDescriptor(new BLE2902());

  BLECharacteristic *pRxCharacteristic = pService->createCharacteristic(
                        CHARACTERISTIC_UUID_RX,
                        BLECharacteristic::PROPERTY_WRITE
                      );

  pService->start();
  BLEAdvertising *pAdvertising = BLEDevice::getAdvertising();
  pAdvertising->addServiceUUID(SERVICE_UUID);
  pAdvertising->setScanResponse(true);
  pAdvertising->setMinPreferred(0x06);
  pAdvertising->setMinPreferred(0x12);
  BLEDevice::startAdvertising();
  Serial.println("[+] BLE Advertising active as: TRISAT-GNSS-BLE");
  Serial.println("[+] System ready. Streaming NMEA + paced Galileo I/NAV...\n");
}

// =========================================================================
// Main Loop
// =========================================================================
String nmeaBuffer = "";
bool isNmea = false;

void loop() {
  while (Serial2.available()) {
    uint8_t c = (uint8_t)Serial2.read();

    // 1. Feed UBX binary decoder
    processUbxByte(c);

    // 2. Buffer NMEA sentences when not inside a UBX packet
    if (ubxState == UBX_WAIT_SYNC1) {
      if (c == '$') {
        nmeaBuffer = "$";
        isNmea = true;
      } else if (isNmea) {
        if (c == '\n' || c == '\r') {
          if (nmeaBuffer.length() > 6 && (nmeaBuffer.startsWith("$GP") || nmeaBuffer.startsWith("$GA") || nmeaBuffer.startsWith("$GL") || nmeaBuffer.startsWith("$GB") || nmeaBuffer.startsWith("$GN"))) {
            nmeaBuffer += "\n";
            // Echo to USB Serial
            Serial.print(nmeaBuffer);

            // Broadcast over WiFi UDP (:9090)
            udp.beginPacket(broadcastIp, UDP_PORT);
            udp.write((const uint8_t*)nmeaBuffer.c_str(), nmeaBuffer.length());
            udp.endPacket();

            // Stream essential NMEA over BLE (GGA, RMC, GSV)
            if (deviceConnected && (nmeaBuffer.startsWith("$GNGGA") || nmeaBuffer.startsWith("$GPGGA") ||
                nmeaBuffer.startsWith("$GNRMC") || nmeaBuffer.startsWith("$GAGSV") ||
                nmeaBuffer.startsWith("$GPGSV"))) {
              int len = nmeaBuffer.length();
              int offset = 0;
              while (offset < len) {
                int chunk = min(20, len - offset);
                pTxCharacteristic->setValue((uint8_t*)(nmeaBuffer.c_str() + offset), chunk);
                pTxCharacteristic->notify();
                offset += chunk;
                delay(3);
              }
            }
          }
          nmeaBuffer = "";
          isNmea = false;
        } else if (c >= 0x20 && c <= 0x7E && nmeaBuffer.length() < 120) {
          nmeaBuffer += (char)c;
        } else {
          nmeaBuffer = "";
          isNmea = false;
        }
      }
    }
  }

  // Handle BLE reconnection
  if (!deviceConnected && oldDeviceConnected) {
    delay(500);
    pServer->startAdvertising();
    Serial.println("\n[BLE] Restarting advertising...");
    oldDeviceConnected = deviceConnected;
  }
  if (deviceConnected && !oldDeviceConnected) {
    oldDeviceConnected = deviceConnected;
  }
}

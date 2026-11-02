#include <SPI.h>

// =========================================================================
// TRISAT - u-blox Live Sniffer & Bus Diagnostic Tool
// =========================================================================
// Verified hardware wiring from PCB inspection:
// PIN 5:  Pink Wire   --> u-blox PPS / Chip Select (CS)
// PIN 18: White Wire  --> u-blox SPI Clock (SCK) / UART TX
// PIN 19: Orange Wire --> u-blox SPI Data (MISO) / UART RX
// =========================================================================
#define PIN_CS   5
#define PIN_SCK  18
#define PIN_MISO 19

void setup() {
  Serial.begin(115200);
  delay(1000);
  Serial.println("\n=============================================");
  Serial.println("  TRISAT - u-blox Live Sniffer & Bus Diagnostic");
  Serial.println("=============================================");

  // 1. DIGITAL LOGIC LEVEL VOLTAGE CHECK
  pinMode(PIN_MISO, INPUT);
  pinMode(PIN_SCK, INPUT);
  pinMode(PIN_CS, INPUT);

  Serial.println("\n--- 1. DIGITAL LOGIC LEVEL CHECK (3.3V vs 0V) ---");
  Serial.printf("Orange Wire (GPIO 19): %s [Level: %d]\n", 
                digitalRead(PIN_MISO) ? "HIGH (3.3V detected - Line is alive!)" : "LOW (0V - Line unpowered or floating)", 
                digitalRead(PIN_MISO));
  Serial.printf("White Wire  (GPIO 18): %s [Level: %d]\n", 
                digitalRead(PIN_SCK)  ? "HIGH (3.3V detected - Line is alive!)" : "LOW (0V - Line unpowered or floating)", 
                digitalRead(PIN_SCK));
  Serial.printf("Pink Wire   (GPIO 5) : %s [Level: %d]\n", 
                digitalRead(PIN_CS)   ? "HIGH (3.3V detected - Line is alive!)" : "LOW (0V - Line unpowered or floating)", 
                digitalRead(PIN_CS));

  // 2. SYNCHRONOUS SPI BUS PROBE
  Serial.println("\n--- 2. SYNCHRONOUS SPI BUS PROBE ---");
  pinMode(PIN_CS, OUTPUT);
  digitalWrite(PIN_CS, HIGH);
  SPI.begin(PIN_SCK, PIN_MISO, 23, PIN_CS); // SCK=18, MISO=19, MOSI=23, CS=5

  digitalWrite(PIN_CS, LOW);
  delayMicroseconds(50);
  
  Serial.print("First 32 bytes read over SPI: ");
  bool foundData = false;
  for (int i = 0; i < 32; i++) {
    uint8_t b = SPI.transfer(0xFF);
    Serial.printf("%02X ", b);
    if (b != 0xFF && b != 0x00) foundData = true;
  }
  digitalWrite(PIN_CS, HIGH);
  Serial.println();

  if (foundData) {
    Serial.println("\n[*** SUCCESS ***] u-blox is actively answering over the SPI bus!");
  } else {
    Serial.println("\n[-] SPI bus idle or operating in UART mode.");
  }

  Serial.println("\n--- 3. LIVE TRANSITION PULSE MONITOR (GPIO 19) ---");
}

unsigned long lastPrint = 0;
unsigned long pulseCount19 = 0;

void loop() {
  // Count fast digital state transitions on GPIO 19 (Orange wire)
  static int lastState = LOW;
  int currentState = digitalRead(PIN_MISO);
  if (currentState != lastState) {
    pulseCount19++;
    lastState = currentState;
  }

  if (millis() - lastPrint > 2000) {
    lastPrint = millis();
    Serial.printf("[Monitor GPIO 19] Current Level: %d | Transitions/Activity in 2s: %lu\n", 
                  digitalRead(PIN_MISO), pulseCount19);
    pulseCount19 = 0;
  }
}

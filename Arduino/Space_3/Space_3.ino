#include <Adafruit_NeoPixel.h>
#include <Arduino.h>
#include <stdio.h>

// ===== 옵션: ACK 로그 출력 여부 (0=끄기, 1=켜기) =====
#define SERIAL_ACK 1

#define RELAY_1 7
#define RELAY_2 6
#define RELAY_3 5

#define BUTTON_1 12
#define BUTTON_2 9
#define BUTTON_3 8 // 10번 인식 x

#define LEDTABLE 4
#define NUMPIXELS 10
Adafruit_NeoPixel pixels(NUMPIXELS, LEDTABLE, NEO_RGB + NEO_KHZ800);

// ===== 스로틀 =====
#define THROTTLE_PIN A5
static const unsigned long THROTTLE_INTERVAL_MS = 10; // THROTTLE ON 시 송신 주기

// 스로틀 모드 -> Silent: 기본 침묵 / Stream: 연속 송신 / OncePending: 1회 송신 예약
enum ThrottleMode { Silent, Stream, OncePending };
volatile ThrottleMode g_thrMode = Silent;

unsigned long g_lastThrottleMs = 0;

// 릴레이 Active Low 여부
const bool RELAY_ACTIVE_LOW = false;

inline void relayOn(int pin)  { digitalWrite(pin, RELAY_ACTIVE_LOW ? LOW : HIGH); }
inline void relayOff(int pin) { digitalWrite(pin, RELAY_ACTIVE_LOW ? HIGH : LOW); }

// 버튼 상태 저장
int prevB1 = HIGH;
int prevB2 = HIGH;
int prevB3 = HIGH;

// NeoPixel 전역 밝기(0~255)
uint8_t g_brightness = 64;

void handleCommand(String cmd); // 직렬 명령 처리

// 안전 범위 클램프
static inline uint8_t clamp255(int v) 
{
  if (v < 0) return 0;
  if (v > 255) return 255;
  return (uint8_t)v;
}

// 간단한 이동 평균(노이즈 완화용)
int readThrottleAveraged(uint8_t samples = 5)
 {
  long sum = 0;
  for (uint8_t i = 0; i < samples; ++i) {
    sum += analogRead(THROTTLE_PIN);
    delayMicroseconds(200); // 아주 짧은 간격
  }
  return (int)(sum / samples);
}

// 스로틀 값 전송(프로토콜: "THROTTLE <값>")
inline void sendThrottle(int value) 
{
  Serial.print("THROTTLE ");
  Serial.println(value);
}

void setup()
{
  Serial.begin(9600);

  // Relay
  pinMode(RELAY_1, OUTPUT);
  pinMode(RELAY_2, OUTPUT);
  pinMode(RELAY_3, OUTPUT);
  relayOff(RELAY_1);
  relayOff(RELAY_2);
  relayOff(RELAY_3);

  // Buttons
  pinMode(BUTTON_1, INPUT_PULLUP);
  pinMode(BUTTON_2, INPUT_PULLUP);
  pinMode(BUTTON_3, INPUT_PULLUP);

  // LED table
  pixels.begin();
  pixels.setBrightness(g_brightness);
  pixels.clear();
  pixels.show();

  // Throttle
  pinMode(THROTTLE_PIN, INPUT);

  Serial.println("Arduino Initialized");
}

void loop()
{
  // ---- 버튼 에지 감지 ----
  int b1 = digitalRead(BUTTON_1);
  int b2 = digitalRead(BUTTON_2);
  int b3 = digitalRead(BUTTON_3);

  if (prevB1 == HIGH && b1 == LOW) Serial.println("BTN 1");
  if (prevB2 == HIGH && b2 == LOW) Serial.println("BTN 2");
  if (prevB3 == HIGH && b3 == LOW) Serial.println("BTN 3");

  prevB1 = b1;
  prevB2 = b2;
  prevB3 = b3;

  // ---- 직렬 명령 처리 ----
  if (Serial.available() > 0) 
  {
    String cmd = Serial.readStringUntil('\n');
    cmd.trim();
    if (cmd.length() > 0) handleCommand(cmd);
  }

  // ---- 스로틀 송신 모드 처리 ----
  const unsigned long now = millis();

  switch (g_thrMode) {
    case Silent:
      // 기본 침묵 -> 아무 것도 전송하지 않음
      break;

    case Stream:
      // 값이 바뀌지 않아도 주기적으로 연속 송신
      if (now - g_lastThrottleMs >= THROTTLE_INTERVAL_MS) {
        g_lastThrottleMs = now;
        int raw = readThrottleAveraged(5);
        sendThrottle(raw);
      }
      break;

    case OncePending:
      // 현재 값을 1회 전송 후 Silent로 복귀
      {
        int raw = readThrottleAveraged(5);
        sendThrottle(raw);
        g_thrMode = Silent;
      }
      break;
  }

  delay(20); // 디바운싱(20ms)
}

// ----------------------------------------------------------------------
// 직렬 프로토콜
//   THROTTLE ON      -> 스로틀 연속 송신 시작
//   THROTTLE OFF     -> 스로틀 송신 중단(침묵)
//   THROTTLE ONCE    -> 현재 스로틀 1회 송신
//   THROTTLE         -> (하위 호환) 현재 스로틀 1회 송신
//
//   PIX i R G B
//   RANGE s e R G B
//   FILL R G B
//   CLEAR
//   BRIGHT n
//   SHOW
//   LED1 ON/OFF, LED2 ON/OFF, LED3 ON/OFF
// ----------------------------------------------------------------------
void handleCommand(String cmd)
{
  cmd.trim();

  // 대소문자 무시를 위해 모두 대문자로 변환
  cmd.toUpperCase();

  // ----- 스로틀 제어 -----
  if (cmd == "THROTTLE ON") 
  {
    g_thrMode = Stream;
    g_lastThrottleMs = 0; // 즉시 한 번 보내도록 타이머 리셋
    if (SERIAL_ACK) Serial.println("ACK THROTTLE ON");
    return;
  }
  if (cmd == "THROTTLE OFF") 
  {
    g_thrMode = Silent;
    if (SERIAL_ACK) Serial.println("ACK THROTTLE OFF");
    return;
  }
  if (cmd == "THROTTLE ONCE" || cmd == "THROTTLE") 
  {
    // "THROTTLE" 단독은 기존 동작과의 하위 호환으로 1회 송신 처리
    g_thrMode = OncePending;
    if (SERIAL_ACK) Serial.println("ACK THROTTLE ONCE");
    return;
  }

  // ----- 릴레이 명령 -----
  if (cmd == "LED1 ON")      { relayOn(RELAY_1);  if (SERIAL_ACK) Serial.println("ACK LED1 ON");  return; }
  if (cmd == "LED1 OFF")     { relayOff(RELAY_1); if (SERIAL_ACK) Serial.println("ACK LED1 OFF"); return; }
  if (cmd == "LED2 ON")      { relayOn(RELAY_2);  if (SERIAL_ACK) Serial.println("ACK LED2 ON");  return; }
  if (cmd == "LED2 OFF")     { relayOff(RELAY_2); if (SERIAL_ACK) Serial.println("ACK LED2 OFF"); return; }
  if (cmd == "LED3 ON")      { relayOn(RELAY_3);  if (SERIAL_ACK) Serial.println("ACK LED3 ON");  return; }
  if (cmd == "LED3 OFF")     { relayOff(RELAY_3); if (SERIAL_ACK) Serial.println("ACK LED3 OFF"); return; }

  // ----- 네오픽셀 제어 -----
  if (cmd == "SHOW") 
  {
    pixels.show();
    return;
  }

  if (cmd == "CLEAR") 
  {
    pixels.clear();
    pixels.show();
    return;
  }

  if (cmd.startsWith("BRIGHT")) 
  {
    int n;
    if (sscanf(cmd.c_str(), "BRIGHT %d", &n) == 1)
    {
      g_brightness = clamp255(n);
      pixels.setBrightness(g_brightness);
      pixels.show();
    } 
    else
    {
      if (SERIAL_ACK) Serial.println("ERR BRIGHT");
    }
    return;
  }

  if (cmd.startsWith("FILL")) 
  {
    int r, g, b;
    if (sscanf(cmd.c_str(), "FILL %d %d %d", &r, &g, &b) == 3) 
    {
      uint8_t R = clamp255(r), G = clamp255(g), B = clamp255(b);
      for (int i = 0; i < NUMPIXELS; i++) pixels.setPixelColor(i, pixels.Color(R, G, B));
      pixels.show();
    } 
    else 
    {
      if (SERIAL_ACK) Serial.println("ERR FILL");
    }
    return;
  }

  if (cmd.startsWith("PIX")) 
  {
    int idx, r, g, b;
    if (sscanf(cmd.c_str(), "PIX %d %d %d %d", &idx, &r, &g, &b) == 4) 
    {
      if (idx < 0 || idx >= NUMPIXELS)
      { 
        if (SERIAL_ACK) Serial.println("ERR PIX"); 
        return; 
      }
      uint8_t R = clamp255(r), G = clamp255(g), B = clamp255(b);
      pixels.setPixelColor(idx, pixels.Color(R, G, B));
      pixels.show();
    }
    else
    {
      if (SERIAL_ACK) Serial.println("ERR PIX");
    }
    return;
  }

  if (cmd.startsWith("RANGE"))
  {
    int s, e, r, g, b;
    if (sscanf(cmd.c_str(), "RANGE %d %d %d %d %d", &s, &e, &r, &g, &b) == 5)
    {
      if (s < 0) s = 0;
      if (e >= NUMPIXELS) e = NUMPIXELS - 1;
      if (s > e) { if (SERIAL_ACK) Serial.println("ERR RANGE"); return; }
      uint8_t R = clamp255(r), G = clamp255(g), B = clamp255(b);
      for (int i = s; i <= e; i++) pixels.setPixelColor(i, pixels.Color(R, G, B));
      pixels.show();
    } 
    else 
    {
      if (SERIAL_ACK) Serial.println("ERR RANGE");
    }
    return;
  }

  // 알 수 없는 명령
  if (SERIAL_ACK) Serial.println("ERR");
}

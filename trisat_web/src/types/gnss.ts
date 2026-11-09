export type ConstellationType = 'galileo' | 'gps' | 'glonass' | 'beidou';

export interface Satellite {
  id: string; // e.g. E02, G14, R07, B22
  prn: number;
  constellation: ConstellationType;
  azimuth: number; // 0 to 360 deg
  elevation: number; // 0 to 90 deg
  cn0: number; // dB-Hz
  hasOsnma: boolean; // Galileo OSNMA capable
  hasHas: boolean; // Galileo HAS E6 capable
  inSolution: boolean;
  frequency: string; // e.g. E1/E5/E6 or L1/L2
}

export interface MissionLocation {
  name: string;
  lat: number;
  lon: number;
  alt: number; // meters
}

export interface HourlyForecast {
  hour: number; // 0 to 23
  timeLabel: string;
  totalSats: number;
  galileoCount: number;
  gpsCount: number;
  glonassCount: number;
  beidouCount: number;
  osnmaCount: number;
  hasCount: number;
  pdop: number;
  hdop: number;
  vdop: number;
  gdop: number;
  isGoldenWindow: boolean;
  satellites: Satellite[];
}

export interface MissionConfig {
  location: MissionLocation;
  date: string;
  elevationMask: number; // degrees (e.g. 10)
  constellations: Record<ConstellationType, boolean>;
}

export interface ThreatState {
  jamming: boolean;
  spoofing: boolean;
  meaconing: boolean;
  macTamper: boolean;
  rfHealthScore: number; // 0 to 100
}

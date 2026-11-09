import * as satellite from 'satellite.js';
import tleCache from './tleCache.json';
import { Satellite, ConstellationType, HourlyForecast, MissionConfig } from '../types/gnss';

export const LOCATION_PRESETS = [
  { name: 'Fuentecén (Ribera del Duero / Burgos, ES)', lat: 41.6378, lon: -3.8689, alt: 837 },
  { name: 'Madrid (IGN / Geodetic Hub, ES)', lat: 40.4168, lon: -3.7038, alt: 667 },
  { name: 'Prague (EUSPA Headquarters, CZ)', lat: 50.0755, lon: 14.4378, alt: 280 },
  { name: 'Noordwijk (ESA ESTEC, NL)', lat: 52.2181, lon: 4.4194, alt: 5 },
  { name: 'Toulouse (Space Center / CNES, FR)', lat: 43.6047, lon: 1.4442, alt: 145 },
  { name: 'Washington D.C. (USNO, USA)', lat: 38.9072, lon: -77.0369, alt: 72 }
];

// Official Galileo GSAT to PRN Mapping (EUSPA)
const GSAT_TO_PRN: Record<string, string> = {
  'GSAT0101': 'E11', 'GSAT0102': 'E12', 'GSAT0103': 'E19', 'GSAT0104': 'E20',
  'GSAT0201': 'E18', 'GSAT0202': 'E14', 'GSAT0203': 'E26', 'GSAT0204': 'E22',
  'GSAT0205': 'E24', 'GSAT0206': 'E30', 'GSAT0207': 'E07', 'GSAT0208': 'E08',
  'GSAT0209': 'E09', 'GSAT0210': 'E01', 'GSAT0211': 'E02', 'GSAT0212': 'E03',
  'GSAT0213': 'E04', 'GSAT0214': 'E05', 'GSAT0215': 'E21', 'GSAT0216': 'E25',
  'GSAT0217': 'E27', 'GSAT0218': 'E31', 'GSAT0219': 'E36', 'GSAT0220': 'E13',
  'GSAT0221': 'E15', 'GSAT0222': 'E33', 'GSAT0223': 'E34', 'GSAT0224': 'E10',
  'GSAT0225': 'E29', 'GSAT0226': 'E23', 'GSAT0227': 'E06', 'GSAT0228': 'E16',
  'GSAT0229': 'E17'
};

// Known Galileo OSNMA capable satellites (~85% of active fleet)
const OSNMA_PRNS = new Set([
  'E01', 'E02', 'E03', 'E04', 'E05', 'E07', 'E08', 'E09', 'E11', 'E12',
  'E13', 'E15', 'E18', 'E19', 'E21', 'E24', 'E25', 'E26', 'E27', 'E29',
  'E30', 'E31', 'E33', 'E34', 'E36'
]);

// Known Galileo E6 HAS capable satellites
const HAS_PRNS = new Set([
  'E01', 'E02', 'E03', 'E04', 'E05', 'E07', 'E08', 'E09', 'E11', 'E12',
  'E13', 'E15', 'E18', 'E19', 'E21', 'E25', 'E27', 'E29', 'E30', 'E31', 'E33'
]);

interface ParsedTle {
  name: string;
  prn: string;
  prnNum: number;
  constellation: ConstellationType;
  satrec: satellite.SatRec;
}

let cachedParsedTles: ParsedTle[] | null = null;

function parseAllTles(): ParsedTle[] {
  if (cachedParsedTles) return cachedParsedTles;

  const result: ParsedTle[] = [];

  const parseSection = (rawText: string, constel: ConstellationType) => {
    const lines = rawText.trim().split('\n');
    for (let i = 0; i < lines.length; i += 3) {
      const name = lines[i]?.trim() || '';
      const l1 = lines[i + 1]?.trim() || '';
      const l2 = lines[i + 2]?.trim() || '';
      if (!l1 || !l2) continue;

      let prn = '';
      let prnNum = 1;

      if (constel === 'galileo') {
        const m = name.match(/GSAT\d+/);
        prn = m ? (GSAT_TO_PRN[m[0]] || m[0]) : `E${(i / 3 + 1).toString().padStart(2, '0')}`;
        prnNum = parseInt(prn.replace(/\D/g, '')) || 1;
      } else if (constel === 'gps') {
        const m = name.match(/PRN\s*(\d+)/i);
        prnNum = m ? parseInt(m[1]) : (i / 3 + 1);
        prn = `G${prnNum.toString().padStart(2, '0')}`;
      } else if (constel === 'glonass') {
        const m = name.match(/\((\d+)\)/);
        const slot = m ? parseInt(m[1]) : 0;
        prnNum = slot > 700 ? (slot - 700) : (i / 3 + 1);
        prn = `R${prnNum.toString().padStart(2, '0')}`;
      } else if (constel === 'beidou') {
        const m = name.match(/\(([CB]\d+)\)/i);
        if (m) {
          prn = m[1].toUpperCase();
          prnNum = parseInt(prn.replace(/\D/g, '')) || (i / 3 + 1);
        } else {
          prnNum = (i / 3 + 1);
          prn = `B${prnNum.toString().padStart(2, '0')}`;
        }
      }

      try {
        const satrec = satellite.twoline2satrec(l1, l2);
        result.push({
          name,
          prn,
          prnNum,
          constellation: constel,
          satrec
        });
      } catch (e) {
        // skip malformed
      }
    }
  };

  parseSection(tleCache.galileo, 'galileo');
  parseSection(tleCache.gps, 'gps');
  parseSection(tleCache.glonass, 'glonass');
  parseSection(tleCache.beidou, 'beidou');

  cachedParsedTles = result;
  return result;
}

/**
 * Computes exact satellite positions (Azimuth and Elevation) matching Trimble GNSS Planning
 * using Vallado SGP4 orbital propagation from CelesTrak TLEs.
 */
export function calculateSatellitesForEpoch(
  latDeg: number,
  lonDeg: number,
  hourOffset: number,
  elevationMask: number,
  activeConstellations: Record<ConstellationType, boolean>,
  baseDate?: Date
): Satellite[] {
  const tles = parseAllTles();
  const satellites: Satellite[] = [];

  // Determine calculation epoch
  const epoch = baseDate ? new Date(baseDate.getTime()) : new Date();
  epoch.setUTCHours(hourOffset, 0, 0, 0);

  const gmst = satellite.gstime(epoch);
  const observerGd = {
    latitude: satellite.degreesToRadians(latDeg),
    longitude: satellite.degreesToRadians(lonDeg),
    height: 0.735 // km
  };

  for (const item of tles) {
    if (!activeConstellations[item.constellation]) continue;

    try {
      const pv = satellite.propagate(item.satrec, epoch);
      if (!pv || !pv.position || typeof pv.position !== 'object' || pv.position.x === undefined) {
        continue;
      }

      const ecf = satellite.eciToEcf(pv.position as satellite.EciVec3<number>, gmst);
      const look = satellite.ecfToLookAngles(observerGd, ecf);

      const azDeg = look.azimuth * (180 / Math.PI);
      const elDeg = look.elevation * (180 / Math.PI);

      // Track all satellites with elevation >= -5° for skyplot
      if (elDeg >= -5) {
        const isOsnma = item.constellation === 'galileo' && OSNMA_PRNS.has(item.prn);
        const isHas = item.constellation === 'galileo' && HAS_PRNS.has(item.prn);

        // Realistic C/N0 model based on elevation angle (dB-Hz)
        const elRad = Math.max(0, satellite.degreesToRadians(elDeg));
        const baseCn0 = 26 + 24 * Math.sin(elRad);
        const cn0 = Math.round(Math.min(52, Math.max(16, baseCn0)));

        satellites.push({
          id: item.prn,
          prn: item.prnNum,
          constellation: item.constellation,
          azimuth: Math.round((azDeg + 360) % 360),
          elevation: Math.round(elDeg),
          cn0,
          hasOsnma: isOsnma,
          hasHas: isHas,
          inSolution: elDeg >= elevationMask,
          frequency: item.constellation === 'galileo' ? 'E1 / E5 / E6' : 'L1 / L2 / L5'
        });
      }
    } catch (e) {
      // skip numerical issues
    }
  }

  satellites.sort((a, b) => {
    if (a.constellation !== b.constellation) return a.constellation.localeCompare(b.constellation);
    return a.prn - b.prn;
  });

  return satellites;
}

/**
 * Computes exact DOP (Dilution of Precision) matrix from visible satellites above elevation cutoff.
 */
export function calculateDop(satellites: Satellite[], elevationMask: number): {
  pdop: number;
  hdop: number;
  vdop: number;
  gdop: number;
} {
  const visible = satellites.filter(s => s.elevation >= elevationMask);
  if (visible.length < 4) {
    return { pdop: 99.9, hdop: 99.9, vdop: 99.9, gdop: 99.9 };
  }

  const A = [
    [0, 0, 0, 0],
    [0, 0, 0, 0],
    [0, 0, 0, 0],
    [0, 0, 0, 0]
  ];

  for (const s of visible) {
    const el = s.elevation * (Math.PI / 180);
    const az = s.azimuth * (Math.PI / 180);

    const row = [
      -Math.cos(el) * Math.sin(az), // East
      -Math.cos(el) * Math.cos(az), // North
      -Math.sin(el),                // Up
      1.0                           // Clock bias
    ];

    for (let r = 0; r < 4; r++) {
      for (let c = 0; c < 4; c++) {
        A[r][c] += row[r] * row[c];
      }
    }
  }

  const inv = invert4x4(A);
  if (!inv) {
    return { pdop: 99.9, hdop: 99.9, vdop: 99.9, gdop: 99.9 };
  }

  const q_ee = Math.max(0, inv[0][0]);
  const q_nn = Math.max(0, inv[1][1]);
  const q_uu = Math.max(0, inv[2][2]);
  const q_tt = Math.max(0, inv[3][3]);

  const hdop = Math.sqrt(q_ee + q_nn);
  const vdop = Math.sqrt(q_uu);
  const pdop = Math.sqrt(q_ee + q_nn + q_uu);
  const gdop = Math.sqrt(q_ee + q_nn + q_uu + q_tt);

  return {
    pdop: Number(Math.min(20, Math.max(0.7, pdop)).toFixed(2)),
    hdop: Number(Math.min(20, Math.max(0.4, hdop)).toFixed(2)),
    vdop: Number(Math.min(20, Math.max(0.6, vdop)).toFixed(2)),
    gdop: Number(Math.min(20, Math.max(0.9, gdop)).toFixed(2))
  };
}

/**
 * Computes 24-hour mission planning forecast using exact SGP4 orbit propagation.
 */
export function generate24HourForecast(config: MissionConfig): HourlyForecast[] {
  const forecast: HourlyForecast[] = [];
  const missionDate = config.date ? new Date(`${config.date}T00:00:00Z`) : new Date();

  for (let hour = 0; hour < 24; hour++) {
    const timeLabel = `${hour < 10 ? '0' + hour : hour}:00 UTC`;
    const sats = calculateSatellitesForEpoch(
      config.location.lat,
      config.location.lon,
      hour,
      config.elevationMask,
      config.constellations,
      missionDate
    );

    const activeSats = sats.filter(s => s.elevation >= config.elevationMask);
    const dop = calculateDop(sats, config.elevationMask);

    const galileoCount = activeSats.filter(s => s.constellation === 'galileo').length;
    const gpsCount = activeSats.filter(s => s.constellation === 'gps').length;
    const glonassCount = activeSats.filter(s => s.constellation === 'glonass').length;
    const beidouCount = activeSats.filter(s => s.constellation === 'beidou').length;
    const osnmaCount = activeSats.filter(s => s.hasOsnma).length;
    const hasCount = activeSats.filter(s => s.hasHas).length;

    forecast.push({
      hour,
      timeLabel,
      totalSats: activeSats.length,
      galileoCount,
      gpsCount,
      glonassCount,
      beidouCount,
      osnmaCount,
      hasCount,
      pdop: dop.pdop,
      hdop: dop.hdop,
      vdop: dop.vdop,
      gdop: dop.gdop,
      isGoldenWindow: false,
      satellites: sats
    });
  }

  let bestHour = 0;
  let bestScore = Infinity;

  for (let i = 0; i < forecast.length; i++) {
    const f = forecast[i];
    let score = f.pdop;
    if (f.osnmaCount < 4) score += 5.0;
    if (f.totalSats < 15) score += 3.0;

    if (score < bestScore) {
      bestScore = score;
      bestHour = i;
    }
  }

  for (let offset = -1; offset <= 1; offset++) {
    const h = (bestHour + offset + 24) % 24;
    forecast[h].isGoldenWindow = true;
  }

  return forecast;
}

function invert4x4(M: number[][]): number[][] | null {
  const A: number[][] = M.map(row => [...row]);
  const I: number[][] = [
    [1, 0, 0, 0],
    [0, 1, 0, 0],
    [0, 0, 1, 0],
    [0, 0, 0, 1]
  ];

  for (let i = 0; i < 4; i++) {
    let pivot = i;
    for (let j = i + 1; j < 4; j++) {
      if (Math.abs(A[j][i]) > Math.abs(A[pivot][i])) {
        pivot = j;
      }
    }

    if (Math.abs(A[pivot][i]) < 1e-9) return null;

    [A[i], A[pivot]] = [A[pivot], A[i]];
    [I[i], I[pivot]] = [I[pivot], I[i]];

    const div = A[i][i];
    for (let j = 0; j < 4; j++) {
      A[i][j] /= div;
      I[i][j] /= div;
    }

    for (let j = 0; j < 4; j++) {
      if (j !== i) {
        const mult = A[j][i];
        for (let k = 0; k < 4; k++) {
          A[j][k] -= mult * A[i][k];
          I[j][k] -= mult * I[i][k];
        }
      }
    }
  }

  return I;
}

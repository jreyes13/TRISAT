import React from 'react';
import { Satellite, ConstellationType } from '../types/gnss';
import { ShieldCheck, Zap } from 'lucide-react';

interface SignalSpectrumProps {
  satellites: Satellite[];
  elevationMask: number;
  selectedSatellite: Satellite | null;
  onSelectSatellite: (sat: Satellite) => void;
}

const CONSTELLATION_BADGES: Record<ConstellationType, { label: string; bg: string; text: string; border: string }> = {
  galileo: { label: 'Galileo', bg: 'bg-cyan-500/10', text: 'text-cyan-400', border: 'border-cyan-500/30' },
  gps: { label: 'GPS', bg: 'bg-amber-500/10', text: 'text-amber-400', border: 'border-amber-500/30' },
  glonass: { label: 'GLONASS', bg: 'bg-red-500/10', text: 'text-red-400', border: 'border-red-500/30' },
  beidou: { label: 'BeiDou', bg: 'bg-emerald-500/10', text: 'text-emerald-400', border: 'border-emerald-500/30' }
};

export const SignalSpectrum: React.FC<SignalSpectrumProps> = ({
  satellites,
  elevationMask,
  selectedSatellite,
  onSelectSatellite
}) => {
  // Sort by constellation and then PRN
  const sorted = [...satellites].sort((a, b) => {
    if (a.constellation !== b.constellation) return a.constellation.localeCompare(b.constellation);
    return a.prn - b.prn;
  });

  return (
    <div className="bg-slate-900/60 backdrop-blur-md rounded-2xl border border-slate-800/80 p-4.5">
      <div className="flex items-center justify-between mb-3.5">
        <div className="flex items-center gap-2">
          <div className="w-2.5 h-2.5 rounded-full bg-cyan-400 animate-pulse" />
          <h3 className="text-sm font-bold tracking-wide uppercase text-slate-200">
            Carrier-to-Noise Ratio (C/N₀ Spectrum)
          </h3>
        </div>
        <div className="flex items-center gap-2 text-[11px] font-mono text-slate-400">
          <span className="flex items-center gap-1"><span className="w-2 h-2 rounded-full bg-emerald-500" /> ≥35 dB-Hz</span>
          <span className="flex items-center gap-1"><span className="w-2 h-2 rounded-full bg-yellow-500" /> ≥25</span>
          <span className="flex items-center gap-1"><span className="w-2 h-2 rounded-full bg-red-500" /> &lt;25</span>
        </div>
      </div>

      {/* Responsive Horizontal Scroll Container */}
      <div className="overflow-x-auto pb-2 pt-4">
        <div className="flex items-end gap-2.5 min-w-max h-44 px-1">
          {sorted.map(sat => {
            const isBelowMask = sat.elevation < elevationMask;
            const isSelected = selectedSatellite?.id === sat.id;
            
            // Bar height relative to 55 dB-Hz
            const barPercent = Math.min(100, Math.max(10, (sat.cn0 / 55) * 100));
            const barColor = isBelowMask
              ? 'bg-slate-700'
              : sat.cn0 >= 35
              ? 'bg-gradient-to-t from-emerald-600 to-emerald-400'
              : sat.cn0 >= 25
              ? 'bg-gradient-to-t from-amber-600 to-amber-400'
              : 'bg-gradient-to-t from-rose-600 to-rose-400';

            const badge = CONSTELLATION_BADGES[sat.constellation];

            return (
              <button
                key={sat.id}
                onClick={() => onSelectSatellite(sat)}
                className={`flex flex-col items-center group transition cursor-pointer select-none rounded-lg p-1.5 ${
                  isSelected ? 'bg-cyan-950/40 ring-1 ring-cyan-400' : 'hover:bg-slate-800/40'
                }`}
                style={{ width: '42px' }}
              >
                {/* dB-Hz label */}
                <span className={`text-[10px] font-mono font-bold mb-1 ${isBelowMask ? 'text-slate-500' : 'text-slate-300'}`}>
                  {sat.cn0}
                </span>

                {/* Vertical Bar */}
                <div className="w-4 bg-slate-950/60 rounded-full h-24 flex items-end p-0.5 border border-slate-800">
                  <div
                    className={`w-full rounded-full transition-all duration-300 ${barColor}`}
                    style={{ height: `${barPercent}%` }}
                  />
                </div>

                {/* Badges / Icons */}
                <div className="flex items-center gap-0.5 mt-1.5 h-3.5">
                  {sat.hasOsnma && (
                    <span title="OSNMA Authenticated">
                      <ShieldCheck className="w-3 h-3 text-cyan-400" />
                    </span>
                  )}
                  {sat.hasHas && (
                    <span title="Galileo HAS High Accuracy">
                      <Zap className="w-2.5 h-2.5 text-amber-400" />
                    </span>
                  )}
                </div>

                {/* PRN Badge */}
                <span
                  className={`mt-1 text-[10px] font-mono font-bold px-1 rounded border ${badge.bg} ${badge.text} ${badge.border}`}
                >
                  {sat.id}
                </span>
                
                {/* Elevation */}
                <span className="text-[9px] font-mono text-slate-500 mt-0.5">
                  {sat.elevation}°
                </span>
              </button>
            );
          })}
        </div>
      </div>
    </div>
  );
};

import React from 'react';
import { ThreatState } from '../types/gnss';
import {
  ShieldAlert,
  Radio,
  Crosshair,
  Repeat,
  Binary,
  CheckCircle,
  RefreshCw
} from 'lucide-react';

interface ThreatAnalysisProps {
  threats: ThreatState;
  onToggleThreat: (key: keyof Omit<ThreatState, 'rfHealthScore'>) => void;
  onResetThreats: () => void;
}

export const ThreatAnalysis: React.FC<ThreatAnalysisProps> = ({
  threats,
  onToggleThreat,
  onResetThreats
}) => {
  const isClean = !threats.jamming && !threats.spoofing && !threats.meaconing && !threats.macTamper;

  return (
    <div className="bg-slate-900/60 backdrop-blur-md rounded-2xl border border-slate-800/80 p-5 space-y-5">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className={`w-9 h-9 rounded-xl flex items-center justify-center border ${
            isClean ? 'bg-emerald-500/10 border-emerald-500/30' : 'bg-rose-500/10 border-rose-500/30'
          }`}>
            <ShieldAlert className={`w-5 h-5 ${isClean ? 'text-emerald-400' : 'text-rose-400 animate-bounce'}`} />
          </div>
          <div>
            <h3 className="text-sm font-bold uppercase tracking-wider text-slate-200">
              RF Integrity & Anti-Spoofing Diagnostic
            </h3>
            <p className="text-xs text-slate-400">
              Simulate electronic warfare and cryptographic attacks to evaluate TRISAT defense response.
            </p>
          </div>
        </div>

        {/* RF Health Meter */}
        <div className="flex items-center gap-3 bg-slate-950/80 px-4 py-2 rounded-xl border border-slate-800">
          <div className="text-right">
            <span className="text-[10px] uppercase font-mono text-slate-400 block">RF Health Score</span>
            <span className={`text-xl font-bold font-mono ${
              threats.rfHealthScore >= 80 ? 'text-emerald-400' : threats.rfHealthScore >= 50 ? 'text-amber-400' : 'text-rose-500'
            }`}>
              {threats.rfHealthScore}%
            </span>
          </div>
          <div className="w-16 h-2.5 bg-slate-800 rounded-full overflow-hidden p-0.5">
            <div
              className={`h-full rounded-full transition-all duration-500 ${
                threats.rfHealthScore >= 80 ? 'bg-emerald-500' : threats.rfHealthScore >= 50 ? 'bg-amber-500' : 'bg-rose-600'
              }`}
              style={{ width: `${threats.rfHealthScore}%` }}
            />
          </div>
        </div>
      </div>

      {/* Attack Injection Controls */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-3">
        {/* Jamming Button */}
        <button
          onClick={() => onToggleThreat('jamming')}
          className={`flex items-start gap-3 p-3.5 rounded-xl border text-left transition cursor-pointer ${
            threats.jamming
              ? 'bg-rose-950/60 border-rose-500/60 shadow-lg shadow-rose-950/50'
              : 'bg-slate-950/50 border-slate-800 hover:bg-slate-800/40'
          }`}
        >
          <Radio className={`w-5 h-5 mt-0.5 shrink-0 ${threats.jamming ? 'text-rose-400 animate-pulse' : 'text-slate-400'}`} />
          <div>
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold text-white">Jamming Attack</span>
              <span className={`text-[10px] font-mono uppercase font-bold px-1.5 py-0.2 rounded ${
                threats.jamming ? 'bg-rose-500/20 text-rose-300' : 'bg-slate-800 text-slate-400'
              }`}>
                {threats.jamming ? 'ACTIVE' : 'OFF'}
              </span>
            </div>
            <p className="text-[11px] text-slate-400 mt-1">
              Injects wideband Gaussian noise across E1/L1 bands. Drastically depresses C/N₀.
            </p>
          </div>
        </button>

        {/* Spoofing Button */}
        <button
          onClick={() => onToggleThreat('spoofing')}
          className={`flex items-start gap-3 p-3.5 rounded-xl border text-left transition cursor-pointer ${
            threats.spoofing
              ? 'bg-amber-950/60 border-amber-500/60 shadow-lg shadow-amber-950/50'
              : 'bg-slate-950/50 border-slate-800 hover:bg-slate-800/40'
          }`}
        >
          <Crosshair className={`w-5 h-5 mt-0.5 shrink-0 ${threats.spoofing ? 'text-amber-400 animate-spin' : 'text-slate-400'}`} />
          <div>
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold text-white">Coherent Spoofing</span>
              <span className={`text-[10px] font-mono uppercase font-bold px-1.5 py-0.2 rounded ${
                threats.spoofing ? 'bg-amber-500/20 text-amber-300' : 'bg-slate-800 text-slate-400'
              }`}>
                {threats.spoofing ? 'ACTIVE' : 'OFF'}
              </span>
            </div>
            <p className="text-[11px] text-slate-400 mt-1">
              Generates fake ephemeris vectors. Detected via AGC power step & Doppler mismatch.
            </p>
          </div>
        </button>

        {/* Meaconing Button */}
        <button
          onClick={() => onToggleThreat('meaconing')}
          className={`flex items-start gap-3 p-3.5 rounded-xl border text-left transition cursor-pointer ${
            threats.meaconing
              ? 'bg-purple-950/60 border-purple-500/60 shadow-lg shadow-purple-950/50'
              : 'bg-slate-950/50 border-slate-800 hover:bg-slate-800/40'
          }`}
        >
          <Repeat className={`w-5 h-5 mt-0.5 shrink-0 ${threats.meaconing ? 'text-purple-400 animate-pulse' : 'text-slate-400'}`} />
          <div>
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold text-white">Meaconing (Replay)</span>
              <span className={`text-[10px] font-mono uppercase font-bold px-1.5 py-0.2 rounded ${
                threats.meaconing ? 'bg-purple-500/20 text-purple-300' : 'bg-slate-800 text-slate-400'
              }`}>
                {threats.meaconing ? 'ACTIVE' : 'OFF'}
              </span>
            </div>
            <p className="text-[11px] text-slate-400 mt-1">
              Replays recorded RF with time delay. OSNMA TESLA chain key-expiration triggers alert.
            </p>
          </div>
        </button>

        {/* MAC Tampering */}
        <button
          onClick={() => onToggleThreat('macTamper')}
          className={`flex items-start gap-3 p-3.5 rounded-xl border text-left transition cursor-pointer ${
            threats.macTamper
              ? 'bg-red-950/60 border-red-500/60 shadow-lg shadow-red-950/50'
              : 'bg-slate-950/50 border-slate-800 hover:bg-slate-800/40'
          }`}
        >
          <Binary className={`w-5 h-5 mt-0.5 shrink-0 ${threats.macTamper ? 'text-red-400 animate-pulse' : 'text-slate-400'}`} />
          <div>
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold text-white">OSNMA MAC Tamper</span>
              <span className={`text-[10px] font-mono uppercase font-bold px-1.5 py-0.2 rounded ${
                threats.macTamper ? 'bg-red-500/20 text-red-300' : 'bg-slate-800 text-slate-400'
              }`}>
                {threats.macTamper ? 'ACTIVE' : 'OFF'}
              </span>
            </div>
            <p className="text-[11px] text-slate-400 mt-1">
              Corrupts navigation subframe bits. Cryptographic tag check fails immediately.
            </p>
          </div>
        </button>
      </div>

      {/* Status Alert Banner */}
      {!isClean ? (
        <div className="bg-rose-950/30 border border-rose-500/40 rounded-xl p-3.5 flex items-center justify-between">
          <div className="flex items-center gap-2.5">
            <span className="relative flex h-3 w-3">
              <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-rose-400 opacity-75"></span>
              <span className="relative inline-flex rounded-full h-3 w-3 bg-rose-500"></span>
            </span>
            <span className="text-xs font-mono font-bold text-rose-300">
              THREAT ACTIVE: {threats.jamming ? '[JAMMING] ' : ''}{threats.spoofing ? '[SPOOFING] ' : ''}{threats.meaconing ? '[MEACONING] ' : ''}{threats.macTamper ? '[MAC_INVALID] ' : ''}
              — Autonomous protection fallback engaged.
            </span>
          </div>

          <button
            onClick={onResetThreats}
            className="flex items-center gap-1.5 px-3 py-1 rounded-lg bg-rose-500/20 hover:bg-rose-500/30 text-rose-200 border border-rose-500/40 text-xs font-semibold transition"
          >
            <RefreshCw className="w-3.5 h-3.5" /> Disarm Threats
          </button>
        </div>
      ) : (
        <div className="bg-emerald-950/20 border border-emerald-500/30 rounded-xl p-3 flex items-center gap-2 text-xs text-emerald-300 font-mono">
          <CheckCircle className="w-4 h-4 text-emerald-400 shrink-0" />
          <span>RF Environment Nominal · No spoofing or jamming detected · OSNMA cryptographic root key validated</span>
        </div>
      )}
    </div>
  );
};

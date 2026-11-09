import React, { useState, useEffect } from 'react';
import { Satellite, ThreatState } from '../types/gnss';
import { Terminal, Shield, Trash2, Pause, Play } from 'lucide-react';

interface InavTerminalProps {
  satellites: Satellite[];
  threats: ThreatState;
}

interface InavFrame {
  id: string;
  time: string;
  svId: string;
  wordType: number;
  wordName: string;
  hex: string;
  osnmaStatus: 'VERIFIED' | 'TAMPERED' | 'UNAUTHENTICATED';
}

export const InavTerminal: React.FC<InavTerminalProps> = ({ satellites, threats }) => {
  const [frames, setFrames] = useState<InavFrame[]>([]);
  const [isPaused, setIsPaused] = useState(false);

  useEffect(() => {
    if (isPaused) return;

    const interval = setInterval(() => {
      const galileoSats = satellites.filter(s => s.constellation === 'galileo');
      if (galileoSats.length === 0) return;

      const randomSat = galileoSats[Math.floor(Math.random() * galileoSats.length)];
      const wordTypes = [
        { type: 1, name: 'Ephemeris (1/4)' },
        { type: 2, name: 'Ephemeris (2/4)' },
        { type: 3, name: 'Ephemeris (3/4)' },
        { type: 4, name: 'Ephemeris (4/4)' },
        { type: 5, name: 'Ionosphere & GST' },
        { type: 16, name: 'Reduced CED / Clock' },
        { type: 44, name: 'OSNMA HKROOT / MACK' }
      ];
      const word = wordTypes[Math.floor(Math.random() * wordTypes.length)];

      // Generate realistic 128-bit hex string
      const hex = Array.from({ length: 8 }, () =>
        Math.floor(Math.random() * 0xffffffff).toString(16).padStart(8, '0')
      ).join('').toUpperCase().slice(0, 32);

      const status: 'VERIFIED' | 'TAMPERED' | 'UNAUTHENTICATED' = threats.macTamper
        ? 'TAMPERED'
        : randomSat.hasOsnma
        ? 'VERIFIED'
        : 'UNAUTHENTICATED';

      const now = new Date();
      const timeStr = `${now.getUTCHours().toString().padStart(2, '0')}:${now.getUTCMinutes().toString().padStart(2, '0')}:${now.getUTCSeconds().toString().padStart(2, '0')}.${Math.floor(now.getUTCMilliseconds() / 100)}`;

      setFrames(prev => [
        {
          id: `${Date.now()}-${Math.random()}`,
          time: timeStr,
          svId: randomSat.id,
          wordType: word.type,
          wordName: word.name,
          hex,
          osnmaStatus: status
        },
        ...prev.slice(0, 49) // Keep last 50
      ]);
    }, 1200);

    return () => clearInterval(interval);
  }, [satellites, threats.macTamper, isPaused]);

  return (
    <div className="bg-slate-900/60 backdrop-blur-md rounded-2xl border border-slate-800/80 p-5 space-y-3 font-mono">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Terminal className="w-4 h-4 text-cyan-400" />
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-200">
            Galileo I/NAV Subframe Decoder & OSNMA Stream
          </h3>
        </div>

        <div className="flex items-center gap-2">
          <button
            onClick={() => setIsPaused(!isPaused)}
            className="p-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 transition"
            title={isPaused ? 'Resume Stream' : 'Pause Stream'}
          >
            {isPaused ? <Play className="w-3.5 h-3.5" /> : <Pause className="w-3.5 h-3.5" />}
          </button>
          <button
            onClick={() => setFrames([])}
            className="p-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 transition"
            title="Clear Stream"
          >
            <Trash2 className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      {/* Terminal Viewport */}
      <div className="bg-slate-950/90 rounded-xl p-3 border border-slate-800/80 h-52 overflow-y-auto space-y-1.5 text-[11px]">
        {frames.length === 0 ? (
          <div className="text-slate-600 text-center py-10 flex flex-col items-center gap-2">
            <Shield className="w-6 h-6 text-slate-700" />
            <span>Listening to Galileo E1-B I/NAV subframes (1 frame / 1200 ms)...</span>
          </div>
        ) : (
          frames.map(f => (
            <div
              key={f.id}
              className={`flex items-baseline gap-2.5 px-2 py-1 rounded transition border ${
                f.osnmaStatus === 'TAMPERED'
                  ? 'bg-rose-950/30 border-rose-800/50 text-rose-300'
                  : f.osnmaStatus === 'VERIFIED'
                  ? 'bg-slate-900/40 border-slate-800/60 text-slate-300'
                  : 'bg-slate-900/20 border-transparent text-slate-400'
              }`}
            >
              <span className="text-slate-500 shrink-0">{f.time}</span>
              <span className="text-cyan-400 font-bold shrink-0">{f.svId}</span>
              <span className="text-slate-400 shrink-0">WT {f.wordType.toString().padStart(2, ' ')} ({f.wordName})</span>
              <span className="text-slate-500 truncate select-all">{f.hex}</span>
              
              <span className={`ml-auto text-[9px] font-bold px-1.5 py-0.5 rounded shrink-0 ${
                f.osnmaStatus === 'TAMPERED'
                  ? 'bg-rose-500/20 text-rose-300 border border-rose-500/40'
                  : f.osnmaStatus === 'VERIFIED'
                  ? 'bg-cyan-500/20 text-cyan-300 border border-cyan-500/40'
                  : 'bg-slate-800 text-slate-400'
              }`}>
                {f.osnmaStatus}
              </span>
            </div>
          ))
        )}
      </div>
    </div>
  );
};

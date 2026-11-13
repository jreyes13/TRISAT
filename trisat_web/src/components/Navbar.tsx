import React from 'react';
import { Compass, Radio, ShieldAlert, Download, ArrowLeft } from 'lucide-react';

export type ActiveTab = 'planner' | 'cockpit' | 'threats' | 'architecture' | 'downloads';

interface NavbarProps {
  activeTab: ActiveTab;
  onSelectTab: (tab: ActiveTab) => void;
}

export const Navbar: React.FC<NavbarProps> = ({ activeTab, onSelectTab }) => {
  return (
    <header className="sticky top-0 z-50 bg-slate-950/80 backdrop-blur-xl border-b border-slate-800/80 px-4 lg:px-8 py-3">
      <div className="max-w-7xl mx-auto flex flex-col md:flex-row items-center justify-between gap-3">
        {/* Brand & Identity */}
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-xl overflow-hidden border border-slate-700/60 shadow-lg shadow-cyan-950/40 shrink-0 bg-slate-900">
            <img src="./app_icon.png" alt="TRISAT App Icon" className="w-full h-full object-cover" />
          </div>

          <div>
            <div className="flex items-center gap-2">
              <h1 className="text-base font-extrabold tracking-tight text-white">TRISAT</h1>
              <span className="px-2 py-0.5 rounded-full text-[10px] font-mono font-bold bg-cyan-500/10 text-cyan-400 border border-cyan-500/30">
                v2.4 WEB TERMINAL
              </span>
            </div>
            <p className="text-[11px] text-slate-400 font-medium">
              Tri-Band GNSS · Galileo OSNMA & HAS PPP Intelligence
            </p>
          </div>
        </div>

        {/* Clean Core Navigation Tabs */}
        <nav className="flex items-center gap-1.5 bg-slate-900/90 p-1 rounded-xl border border-slate-800/80">
          <button
            onClick={() => onSelectTab('planner')}
            className={`flex items-center gap-2 px-3.5 py-1.5 rounded-lg text-xs font-semibold transition cursor-pointer ${
              activeTab === 'planner'
                ? 'bg-cyan-500 text-slate-950 shadow-md shadow-cyan-500/20'
                : 'text-slate-400 hover:text-white hover:bg-slate-800/50'
            }`}
          >
            <Compass className="w-3.5 h-3.5" />
            <span>Mission Planner</span>
          </button>

          <button
            onClick={() => onSelectTab('cockpit')}
            className={`flex items-center gap-2 px-3.5 py-1.5 rounded-lg text-xs font-semibold transition cursor-pointer ${
              activeTab === 'cockpit'
                ? 'bg-cyan-500 text-slate-950 shadow-md shadow-cyan-500/20'
                : 'text-slate-400 hover:text-white hover:bg-slate-800/50'
            }`}
          >
            <Radio className="w-3.5 h-3.5" />
            <span>Live Cockpit</span>
          </button>

          <button
            onClick={() => onSelectTab('threats')}
            className={`flex items-center gap-2 px-3.5 py-1.5 rounded-lg text-xs font-semibold transition cursor-pointer ${
              activeTab === 'threats'
                ? 'bg-cyan-500 text-slate-950 shadow-md shadow-cyan-500/20'
                : 'text-slate-400 hover:text-white hover:bg-slate-800/50'
            }`}
          >
            <ShieldAlert className="w-3.5 h-3.5" />
            <span>Anti-Spoofing</span>
          </button>

          <button
            onClick={() => onSelectTab('downloads')}
            className={`flex items-center gap-2 px-3.5 py-1.5 rounded-lg text-xs font-semibold transition cursor-pointer ${
              activeTab === 'downloads'
                ? 'bg-emerald-500 text-slate-950 shadow-md shadow-emerald-500/20'
                : 'text-emerald-400/90 hover:text-emerald-300 hover:bg-slate-800/50'
            }`}
          >
            <Download className="w-3.5 h-3.5" />
            <span>Downloads</span>
            <span className="px-1.5 py-0.2 rounded text-[9px] font-mono font-bold bg-emerald-500/20 text-emerald-300">
              APK
            </span>
          </button>
        </nav>

        {/* Back Link to jreyes13.net */}
        <div className="flex items-center gap-2">
          <a
            href="https://jreyes13.net"
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-slate-900/90 hover:bg-slate-800 text-slate-300 hover:text-white border border-slate-800 text-xs font-medium transition cursor-pointer"
            title="Volver a jreyes13.net"
          >
            <ArrowLeft className="w-3.5 h-3.5 text-cyan-400" />
            <span>jreyes13.net</span>
          </a>
        </div>
      </div>
    </header>
  );
};

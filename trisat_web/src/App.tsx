import React, { useState, useMemo } from 'react';
import { Navbar, ActiveTab } from './components/Navbar';
import { Skyplot } from './components/Skyplot';
import { SignalSpectrum } from './components/SignalSpectrum';
import { MissionPlanner } from './components/MissionPlanner';
import { ThreatAnalysis } from './components/ThreatAnalysis';
import { InavTerminal } from './components/InavTerminal';
import { Downloads } from './components/Downloads';
import { LOCATION_PRESETS, generate24HourForecast, calculateSatellitesForEpoch, calculateDop } from './core/orbitMath';
import { MissionConfig, Satellite, ThreatState, ConstellationType } from './types/gnss';
import {
  Globe,
  Radio,
  Navigation,
  Activity,
  Layers,
  ShieldCheck,
  CheckCircle2,
  Cpu,
  Terminal,
  Clock,
  Wifi,
  Sliders,
  ExternalLink,
  ArrowLeft
} from 'lucide-react';

export const App: React.FC = () => {
  const [activeTab, setActiveTab] = useState<ActiveTab>('planner');

  // Mission Planner Configuration State
  const [missionConfig, setMissionConfig] = useState<MissionConfig>({
    location: LOCATION_PRESETS[0], // Fuentecén (Ribera del Duero / Burgos)
    date: new Date().toISOString().split('T')[0],
    elevationMask: 10,
    constellations: {
      galileo: true,
      gps: true,
      glonass: true,
      beidou: true
    }
  });

  // Selected hour for mission forecasting (defaults to current UTC hour)
  const [selectedHour, setSelectedHour] = useState<number>(new Date().getUTCHours());

  // Selected satellite for inspection
  const [selectedSatellite, setSelectedSatellite] = useState<Satellite | null>(null);

  // Optional orbit trails toggle (OFF by default for a clean skyplot)
  const [showTrails, setShowTrails] = useState<boolean>(false);

  // Threat Injection State
  const [threats, setThreats] = useState<ThreatState>({
    jamming: false,
    spoofing: false,
    meaconing: false,
    macTamper: false,
    rfHealthScore: 100
  });

  // 24-Hour Forecast generation (memoized)
  const forecast = useMemo(() => {
    return generate24HourForecast(missionConfig);
  }, [missionConfig]);

  // Current epoch satellites based on selected hour and threat injection
  const currentEpochSatellites = useMemo(() => {
    const baseSats = calculateSatellitesForEpoch(
      missionConfig.location.lat,
      missionConfig.location.lon,
      selectedHour,
      missionConfig.elevationMask,
      missionConfig.constellations
    );

    // If Jamming attack is active, reduce C/N0 by 15-25 dB-Hz across the board
    if (threats.jamming) {
      return baseSats.map(s => ({
        ...s,
        cn0: Math.max(12, s.cn0 - 22)
      }));
    }

    return baseSats;
  }, [missionConfig, selectedHour, threats.jamming]);

  // Current DOP and metrics
  const currentDop = useMemo(() => {
    return calculateDop(currentEpochSatellites, missionConfig.elevationMask);
  }, [currentEpochSatellites, missionConfig.elevationMask]);

  // Handle Threat toggles
  const handleToggleThreat = (key: keyof Omit<ThreatState, 'rfHealthScore'>) => {
    setThreats(prev => {
      const updated = { ...prev, [key]: !prev[key] };
      let penalty = 0;
      if (updated.jamming) penalty += 40;
      if (updated.spoofing) penalty += 30;
      if (updated.meaconing) penalty += 20;
      if (updated.macTamper) penalty += 35;
      updated.rfHealthScore = Math.max(5, 100 - penalty);
      return updated;
    });
  };

  const handleResetThreats = () => {
    setThreats({
      jamming: false,
      spoofing: false,
      meaconing: false,
      macTamper: false,
      rfHealthScore: 100
    });
  };

  const toggleConstellation = (type: ConstellationType) => {
    setMissionConfig(prev => ({
      ...prev,
      constellations: {
        ...prev.constellations,
        [type]: !prev.constellations[type]
      }
    }));
  };

  const visibleSatsCount = currentEpochSatellites.filter(s => s.elevation >= missionConfig.elevationMask).length;
  const osnmaCount = currentEpochSatellites.filter(s => s.hasOsnma && s.elevation >= missionConfig.elevationMask).length;
  const osnmaVerified = osnmaCount >= 4 && !threats.macTamper && !threats.spoofing;

  return (
    <div className="min-h-screen flex flex-col bg-slate-950 text-slate-100">
      {/* Top Navigation */}
      <Navbar
        activeTab={activeTab}
        onSelectTab={setActiveTab}
      />

      {/* Main Content Area */}
      <main className="flex-1 max-w-7xl mx-auto w-full p-4 lg:p-8 space-y-6">

        {/* Constellation Toggle Filter Chips (Always accessible) */}
        <div className="flex flex-wrap items-center justify-between gap-3 bg-slate-900/40 p-3 rounded-2xl border border-slate-800/80">
          <div className="flex items-center gap-2">
            <span className="text-xs font-bold uppercase tracking-wider text-slate-400">
              Constellations:
            </span>
            <div className="flex items-center gap-1.5">
              <button
                onClick={() => toggleConstellation('galileo')}
                className={`px-3 py-1 rounded-lg text-xs font-semibold border transition cursor-pointer ${
                  missionConfig.constellations.galileo
                    ? 'bg-cyan-500/20 text-cyan-300 border-cyan-500/40 shadow-sm'
                    : 'bg-slate-950 text-slate-500 border-slate-800'
                }`}
              >
                🇪🇺 Galileo (E)
              </button>
              <button
                onClick={() => toggleConstellation('gps')}
                className={`px-3 py-1 rounded-lg text-xs font-semibold border transition cursor-pointer ${
                  missionConfig.constellations.gps
                    ? 'bg-amber-500/20 text-amber-300 border-amber-500/40 shadow-sm'
                    : 'bg-slate-950 text-slate-500 border-slate-800'
                }`}
              >
                🇺🇸 GPS (G)
              </button>
              <button
                onClick={() => toggleConstellation('glonass')}
                className={`px-3 py-1 rounded-lg text-xs font-semibold border transition cursor-pointer ${
                  missionConfig.constellations.glonass
                    ? 'bg-red-500/20 text-red-300 border-red-500/40 shadow-sm'
                    : 'bg-slate-950 text-slate-500 border-slate-800'
                }`}
              >
                🇷🇺 GLONASS (R)
              </button>
              <button
                onClick={() => toggleConstellation('beidou')}
                className={`px-3 py-1 rounded-lg text-xs font-semibold border transition cursor-pointer ${
                  missionConfig.constellations.beidou
                    ? 'bg-emerald-500/20 text-emerald-300 border-emerald-500/40 shadow-sm'
                    : 'bg-slate-950 text-slate-500 border-slate-800'
                }`}
              >
                🇨🇳 BeiDou (B)
              </button>
            </div>
          </div>

          <div className="flex items-center gap-3 text-xs font-mono text-slate-400">
            <span>Cutoff: <strong className="text-emerald-400">{missionConfig.elevationMask}°</strong></span>
            <span>·</span>
            <span>Epoch: <strong className="text-cyan-400">{selectedHour}:00 UTC</strong></span>
            <span>·</span>
            <span>Visible: <strong className="text-white">{visibleSatsCount} Sats</strong></span>
          </div>
        </div>

        {/* TAB 1: MISSION PLANNER */}
        {activeTab === 'planner' && (
          <div className="space-y-6">
            <MissionPlanner
              config={missionConfig}
              onChangeConfig={setMissionConfig}
              forecast={forecast}
              selectedHour={selectedHour}
              onSelectHour={setSelectedHour}
            />

            {/* Side-by-side Skyplot and Selected Satellite Inspector */}
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
              {/* Polar Skyplot */}
              <div className="lg:col-span-1 bg-slate-900/60 backdrop-blur-md rounded-2xl border border-slate-800/80 p-5 flex flex-col items-center">
                <div className="w-full flex items-center justify-between mb-3">
                  <h3 className="text-xs font-bold uppercase tracking-wider text-slate-200 flex items-center gap-1.5">
                    <Globe className="w-4 h-4 text-cyan-400" /> Polar Skyplot ({selectedHour}:00 UTC)
                  </h3>
                  <button
                    onClick={() => setShowTrails(!showTrails)}
                    className={`text-[10px] font-mono px-2 py-0.5 rounded border transition cursor-pointer ${
                      showTrails
                        ? 'bg-cyan-500/20 text-cyan-300 border-cyan-500/40'
                        : 'bg-slate-800 text-slate-400 border-slate-700 hover:text-white'
                    }`}
                  >
                    {showTrails ? 'Trails: ON' : 'Trails: OFF'}
                  </button>
                </div>

                <Skyplot
                  satellites={currentEpochSatellites}
                  elevationMask={missionConfig.elevationMask}
                  selectedSatellite={selectedSatellite}
                  onSelectSatellite={setSelectedSatellite}
                  width={340}
                  height={340}
                  showTrails={showTrails}
                />

                <span className="text-[11px] text-slate-500 mt-3 text-center">
                  Outer dashed circle marks the {missionConfig.elevationMask}° terrain elevation cutoff.
                </span>
              </div>

              {/* Signal Strength Spectrum & Inspector */}
              <div className="lg:col-span-2 space-y-6">
                <SignalSpectrum
                  satellites={currentEpochSatellites}
                  elevationMask={missionConfig.elevationMask}
                  selectedSatellite={selectedSatellite}
                  onSelectSatellite={setSelectedSatellite}
                />

                {/* Satellite Inspector Card */}
                {selectedSatellite ? (
                  <div className="bg-slate-900/60 backdrop-blur-md rounded-2xl border border-cyan-500/40 p-5">
                    <div className="flex items-center justify-between mb-3">
                      <div className="flex items-center gap-2.5">
                        <span className="text-xl font-bold font-mono text-cyan-300">
                          {selectedSatellite.id}
                        </span>
                        <span className="px-2 py-0.5 rounded text-xs font-bold uppercase bg-slate-800 text-slate-300">
                          {selectedSatellite.constellation.toUpperCase()}
                        </span>
                        {selectedSatellite.hasOsnma && (
                          <span className="px-2 py-0.5 rounded text-xs font-bold bg-cyan-500/20 text-cyan-300 border border-cyan-500/40 flex items-center gap-1">
                            <ShieldCheck className="w-3.5 h-3.5" /> OSNMA Authenticated
                          </span>
                        )}
                      </div>
                      <button
                        onClick={() => setSelectedSatellite(null)}
                        className="text-xs text-slate-500 hover:text-white"
                      >
                        Close
                      </button>
                    </div>

                    <div className="grid grid-cols-2 md:grid-cols-4 gap-3 text-xs font-mono">
                      <div className="p-2.5 rounded-lg bg-slate-950 border border-slate-800">
                        <span className="text-slate-500 block text-[10px]">ELEVATION</span>
                        <span className="text-lg font-bold text-white">{selectedSatellite.elevation}°</span>
                      </div>
                      <div className="p-2.5 rounded-lg bg-slate-950 border border-slate-800">
                        <span className="text-slate-500 block text-[10px]">AZIMUTH</span>
                        <span className="text-lg font-bold text-white">{selectedSatellite.azimuth}°</span>
                      </div>
                      <div className="p-2.5 rounded-lg bg-slate-950 border border-slate-800">
                        <span className="text-slate-500 block text-[10px]">C/N₀ SIGNAL</span>
                        <span className="text-lg font-bold text-emerald-400">{selectedSatellite.cn0} dB-Hz</span>
                      </div>
                      <div className="p-2.5 rounded-lg bg-slate-950 border border-slate-800">
                        <span className="text-slate-500 block text-[10px]">FREQUENCY</span>
                        <span className="text-xs font-bold text-slate-200 mt-1 block">{selectedSatellite.frequency}</span>
                      </div>
                    </div>
                  </div>
                ) : (
                  <div className="bg-slate-900/40 rounded-2xl border border-dashed border-slate-800 p-5 text-center text-xs text-slate-500">
                    Click any satellite in the Skyplot or Spectrum bar to inspect its Keplerian azimuth, elevation, and cryptographic OSNMA status.
                  </div>
                )}
              </div>
            </div>
          </div>
        )}

        {/* TAB 2: LIVE COCKPIT (macOS REPLICA) */}
        {activeTab === 'cockpit' && (
          <div className="space-y-6">
            {/* Quick Epoch & Location Controller */}
            <div className="bg-slate-900/60 backdrop-blur-md rounded-2xl border border-slate-800/80 p-4 flex flex-col md:flex-row items-center justify-between gap-4">
              <div className="flex items-center gap-3">
                <div className="w-8 h-8 rounded-lg bg-cyan-500/10 border border-cyan-500/30 flex items-center justify-center">
                  <Clock className="w-4 h-4 text-cyan-400" />
                </div>
                <div>
                  <span className="text-xs font-bold text-white uppercase tracking-wider block">
                    Simulation Epoch & Time Scrubbing
                  </span>
                  <span className="text-[11px] font-mono text-cyan-300">
                    Active: {selectedHour.toString().padStart(2, '0')}:00 UTC · Site: {missionConfig.location.name}
                  </span>
                </div>
              </div>

              {/* Slider & Buttons */}
              <div className="flex items-center gap-4 w-full md:w-auto">
                <div className="flex items-center gap-2 flex-1 md:w-64">
                  <span className="text-[10px] font-mono text-slate-500">00h</span>
                  <input
                    type="range"
                    min="0"
                    max="23"
                    step="1"
                    value={selectedHour}
                    onChange={e => setSelectedHour(Number(e.target.value))}
                    className="w-full accent-cyan-400 h-1.5 bg-slate-800 rounded-lg cursor-pointer"
                  />
                  <span className="text-[10px] font-mono text-slate-500">23h</span>
                </div>

                <button
                  onClick={() => setSelectedHour(new Date().getUTCHours())}
                  className="px-2.5 py-1 rounded-lg bg-cyan-500/20 hover:bg-cyan-500/30 text-cyan-300 border border-cyan-500/40 text-xs font-semibold font-mono transition shrink-0"
                >
                  NOW ({new Date().getUTCHours().toString().padStart(2, '0')}h)
                </button>
              </div>
            </div>

            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
              {/* Skyplot View */}
              <div className="lg:col-span-1 bg-slate-900/60 backdrop-blur-md rounded-2xl border border-slate-800/80 p-5 flex flex-col items-center">
                <div className="w-full flex items-center justify-between mb-3">
                  <h3 className="text-xs font-bold uppercase tracking-wider text-slate-200 flex items-center gap-1.5">
                    <Radio className="w-4 h-4 text-cyan-400 animate-pulse" /> Live Satellite Tracking
                  </h3>
                  <button
                    onClick={() => setShowTrails(!showTrails)}
                    className={`text-[10px] font-mono px-2 py-0.5 rounded border transition cursor-pointer ${
                      showTrails
                        ? 'bg-cyan-500/20 text-cyan-300 border-cyan-500/40'
                        : 'bg-slate-800 text-slate-400 border-slate-700 hover:text-white'
                    }`}
                  >
                    {showTrails ? 'Trails: ON' : 'Trails: OFF'}
                  </button>
                </div>

                <Skyplot
                  satellites={currentEpochSatellites}
                  elevationMask={missionConfig.elevationMask}
                  selectedSatellite={selectedSatellite}
                  onSelectSatellite={setSelectedSatellite}
                  width={340}
                  height={340}
                  showTrails={showTrails}
                />
              </div>

              {/* Position Telemetry & DOP Geometry */}
              <div className="lg:col-span-2 space-y-6">
                {/* Telemetry Card */}
                <div className="bg-slate-900/60 backdrop-blur-md rounded-2xl border border-slate-800/80 p-5">
                  <div className="flex items-center justify-between mb-4">
                    <h3 className="text-sm font-bold uppercase tracking-wider text-slate-200 flex items-center gap-2">
                      <Navigation className="w-4 h-4 text-cyan-400" /> Geodetic PVT Position Telemetry
                    </h3>
                    <span className="px-2.5 py-0.5 rounded-full text-xs font-mono font-bold bg-cyan-500/10 text-cyan-300 border border-cyan-500/30">
                      3D FIX · RTK FLOAT
                    </span>
                  </div>

                  <div className="grid grid-cols-2 md:grid-cols-4 gap-4 font-mono">
                    <div className="p-3 bg-slate-950 rounded-xl border border-slate-800">
                      <span className="text-[10px] text-slate-500 uppercase block">Latitude</span>
                      <span className="text-base font-bold text-white">
                        {missionConfig.location.lat.toFixed(6)}° N
                      </span>
                    </div>

                    <div className="p-3 bg-slate-950 rounded-xl border border-slate-800">
                      <span className="text-[10px] text-slate-500 uppercase block">Longitude</span>
                      <span className="text-base font-bold text-white">
                        {missionConfig.location.lon.toFixed(6)}° E
                      </span>
                    </div>

                    <div className="p-3 bg-slate-950 rounded-xl border border-slate-800">
                      <span className="text-[10px] text-slate-500 uppercase block">Altitude (MSL)</span>
                      <span className="text-base font-bold text-white">
                        {missionConfig.location.alt.toFixed(1)} m
                      </span>
                    </div>

                    <div className="p-3 bg-slate-950 rounded-xl border border-slate-800">
                      <span className="text-[10px] text-slate-500 uppercase block">Velocity</span>
                      <span className="text-base font-bold text-emerald-400">
                        0.04 m/s
                      </span>
                    </div>
                  </div>

                  {/* DOP Geometry Row */}
                  <div className="grid grid-cols-4 gap-3 mt-4 text-xs font-mono">
                    <div className="p-2.5 bg-slate-950/70 rounded-lg border border-slate-800/80 text-center">
                      <span className="text-[10px] text-slate-500 block">PDOP</span>
                      <span className="text-sm font-bold text-cyan-300">{currentDop.pdop.toFixed(2)}</span>
                    </div>
                    <div className="p-2.5 bg-slate-950/70 rounded-lg border border-slate-800/80 text-center">
                      <span className="text-[10px] text-slate-500 block">HDOP</span>
                      <span className="text-sm font-bold text-emerald-300">{currentDop.hdop.toFixed(2)}</span>
                    </div>
                    <div className="p-2.5 bg-slate-950/70 rounded-lg border border-slate-800/80 text-center">
                      <span className="text-[10px] text-slate-500 block">VDOP</span>
                      <span className="text-sm font-bold text-slate-300">{currentDop.vdop.toFixed(2)}</span>
                    </div>
                    <div className="p-2.5 bg-slate-950/70 rounded-lg border border-slate-800/80 text-center">
                      <span className="text-[10px] text-slate-500 block">GDOP</span>
                      <span className="text-sm font-bold text-indigo-300">{currentDop.gdop.toFixed(2)}</span>
                    </div>
                  </div>
                </div>

                {/* Signal Spectrum */}
                <SignalSpectrum
                  satellites={currentEpochSatellites}
                  elevationMask={missionConfig.elevationMask}
                  selectedSatellite={selectedSatellite}
                  onSelectSatellite={setSelectedSatellite}
                />
              </div>
            </div>

            {/* Live Galileo I/NAV Terminal */}
            <InavTerminal
              satellites={currentEpochSatellites}
              threats={threats}
            />
          </div>
        )}

        {/* TAB 3: THREAT ANALYSIS */}
        {activeTab === 'threats' && (
          <div className="space-y-6">
            <ThreatAnalysis
              threats={threats}
              onToggleThreat={handleToggleThreat}
              onResetThreats={handleResetThreats}
            />

            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
              <SignalSpectrum
                satellites={currentEpochSatellites}
                elevationMask={missionConfig.elevationMask}
                selectedSatellite={selectedSatellite}
                onSelectSatellite={setSelectedSatellite}
              />

              <InavTerminal
                satellites={currentEpochSatellites}
                threats={threats}
              />
            </div>
          </div>
        )}

        {/* TAB 4: ARCHITECTURE & HARDWARE */}
        {activeTab === 'architecture' && (
          <div className="space-y-6">
            <div className="bg-slate-900/60 backdrop-blur-md rounded-2xl border border-slate-800/80 p-6 space-y-6">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
                <div className="flex items-center gap-3">
                  <div className="w-10 h-10 rounded-xl bg-cyan-500/10 border border-cyan-500/30 flex items-center justify-center">
                    <Cpu className="w-5 h-5 text-cyan-400" />
                  </div>
                  <div>
                    <h2 className="text-base font-bold text-white uppercase tracking-wider">
                      TRISAT Technical Architecture & Multiplatform Stack
                    </h2>
                    <p className="text-xs text-slate-400">
                      Tri-Band reception · Tri-Service verification · Tri-Platform native execution
                    </p>
                  </div>
                </div>

                <button
                  onClick={() => setActiveTab('cockpit')}
                  className="self-start sm:self-auto flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs font-semibold text-slate-300 hover:text-white transition cursor-pointer"
                >
                  <ArrowLeft className="w-3.5 h-3.5 text-cyan-400" />
                  <span>Back to Cockpit</span>
                </button>
              </div>

              {/* Tri-Service Grid */}
              <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                <div className="p-4 rounded-xl bg-slate-950 border border-cyan-500/30">
                  <div className="flex items-center gap-2 mb-2">
                    <ShieldCheck className="w-4 h-4 text-cyan-400" />
                    <h4 className="text-sm font-bold text-white">Galileo OSNMA</h4>
                  </div>
                  <p className="text-xs text-slate-300 leading-relaxed">
                    Open Service Navigation Message Authentication. Cryptographically verifies I/NAV navigation data using TESLA key chains and HMAC signatures on E1-B signals.
                  </p>
                </div>

                <div className="p-4 rounded-xl bg-slate-950 border border-amber-500/30">
                  <div className="flex items-center gap-2 mb-2">
                    <Activity className="w-4 h-4 text-amber-400" />
                    <h4 className="text-sm font-bold text-white">Galileo HAS (E6)</h4>
                  </div>
                  <p className="text-xs text-slate-300 leading-relaxed">
                    High Accuracy Service. Streams PPP orbit, satellite clock, and phase bias corrections via E6-B C/NAV to achieve sub-20 cm decimetric positioning.
                  </p>
                </div>

                <div className="p-4 rounded-xl bg-slate-950 border border-emerald-500/30">
                  <div className="flex items-center gap-2 mb-2">
                    <Radio className="w-4 h-4 text-emerald-400" />
                    <h4 className="text-sm font-bold text-white">EWSS & Integrity</h4>
                  </div>
                  <p className="text-xs text-slate-300 leading-relaxed">
                    Emergency Warning Satellite Service and autonomous anti-jamming / anti-spoofing diagnostics for critical infrastructure and precision agriculture.
                  </p>
                </div>
              </div>

              {/* Hardware Bridges */}
              <div className="space-y-3 pt-2">
                <h4 className="text-xs font-bold uppercase tracking-wider text-slate-400">
                  Supported Hardware Bridges & Receivers
                </h4>
                <div className="grid grid-cols-1 md:grid-cols-3 gap-3 text-xs">
                  <div className="p-3 bg-slate-950/80 rounded-xl border border-slate-800">
                    <strong className="text-cyan-300 block mb-1">u-blox ZED-F9P + ESP32</strong>
                    <span className="text-slate-400">Dual-band RTK receiver paired with ESP32 hybrid bridge for wireless BLE NUS and WiFi UDP streaming.</span>
                  </div>
                  <div className="p-3 bg-slate-950/80 rounded-xl border border-slate-800">
                    <strong className="text-amber-300 block mb-1">Geneq SXblue USB</strong>
                    <span className="text-slate-400">Sub-meter survey receiver connected via FTDI USB-CDC daemon with paced I/NAV forwarding.</span>
                  </div>
                  <div className="p-3 bg-slate-950/80 rounded-xl border border-slate-800">
                    <strong className="text-emerald-300 block mb-1">HackRF Dual SDR</strong>
                    <span className="text-slate-400">Software-defined radio baseband generator for E1 OSNMA and E6 HAS RF signal synthesis and testing.</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        )}

        {/* Tab 5: Downloads & Android APK Sideloading */}
        {activeTab === 'downloads' && (
          <Downloads />
        )}

      </main>

      {/* Sleek Minimalist Footer with Internal Architecture, GitHub, & jreyes13.net */}
      <footer className="border-t border-slate-800/80 bg-slate-950/90 backdrop-blur-md px-4 lg:px-8 py-5">
        <div className="max-w-7xl mx-auto flex flex-col sm:flex-row items-center justify-between gap-4 text-xs font-mono">
          {/* Left: Brand info & OSNMA verification */}
          <div className="flex flex-wrap items-center gap-3">
            <div className="flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-cyan-400"></span>
              <span className="font-bold text-slate-200">TRISAT v2.4</span>
            </div>
            <span className="text-slate-600 hidden sm:inline">·</span>
            <div className="flex items-center gap-1.5 px-2.5 py-0.5 rounded-full bg-slate-900 border border-slate-800 text-[11px]">
              <span className={`w-1.5 h-1.5 rounded-full ${osnmaVerified ? 'bg-emerald-400 animate-pulse' : 'bg-amber-400'}`} />
              <span className={osnmaVerified ? 'text-emerald-300 font-semibold' : 'text-amber-300'}>
                {osnmaVerified ? 'Galileo OSNMA Authenticated' : 'Provisional Fix'}
              </span>
            </div>
          </div>

          {/* Center: Internal navigation & external resources */}
          <div className="flex flex-wrap items-center gap-4 text-slate-400">
            <button
              onClick={() => setActiveTab('architecture')}
              className={`hover:text-cyan-400 transition cursor-pointer flex items-center gap-1.5 ${
                activeTab === 'architecture' ? 'text-cyan-400 font-bold' : ''
              }`}
            >
              <Cpu className="w-3.5 h-3.5" />
              <span>Architecture & Stack</span>
            </button>

            <span>·</span>

            <a
              href="https://github.com/jreyes13/TRISAT"
              target="_blank"
              rel="noopener noreferrer"
              className="hover:text-white transition flex items-center gap-1"
            >
              <span>GitHub</span>
              <ExternalLink className="w-3 h-3 text-slate-500" />
            </a>

            <span>·</span>

            <a
              href="https://jreyes13.net"
              className="hover:text-cyan-400 text-slate-300 transition flex items-center gap-1"
            >
              <span>jreyes13.net</span>
              <ExternalLink className="w-3 h-3 text-cyan-400" />
            </a>
          </div>

          {/* Right: Author & Year */}
          <div className="text-slate-500 text-[11px]">
            Joaquín Reyes González · 2026
          </div>
        </div>
      </footer>
    </div>
  );
};
export default App;

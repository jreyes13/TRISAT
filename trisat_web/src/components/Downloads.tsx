import React from 'react';
import { Download, Smartphone, ShieldCheck, CheckCircle2, HardDrive, Cpu, Cloud, FileCode, ExternalLink } from 'lucide-react';

export const Downloads: React.FC = () => {
  const gdriveUrl = "https://drive.google.com/file/d/1jjjgb-XtwdVf9qXxyHrRuI1HG-qJ_dW9/view?usp=drivesdk";
  const directApkUrl = "/downloads/TRISAT-v2.4-debug.apk";
  const sha256Checksum = "e350a413eee592db59b794ae4b65ecf7773f343dc72dcc8c9bd8a2decf26de1e";

  return (
    <div className="space-y-6">
      {/* Hero Header */}
      <div className="bg-slate-900/90 border border-slate-800 rounded-2xl p-6 lg:p-8 backdrop-blur-xl relative overflow-hidden">
        <div className="absolute -right-16 -top-16 w-64 h-64 bg-cyan-500/10 rounded-full blur-3xl pointer-events-none" />
        <div className="relative z-10 flex flex-col md:flex-row items-start md:items-center justify-between gap-6">
          <div className="space-y-2 max-w-2xl">
            <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-emerald-500/15 border border-emerald-500/30 text-emerald-400 text-xs font-mono font-bold">
              <Smartphone className="w-3.5 h-3.5" />
              <span>OFFICIAL ANDROID RELEASE</span>
            </div>
            <h2 className="text-2xl lg:text-3xl font-extrabold text-white tracking-tight">
              TRISAT Mobile v2.4 (Android APK)
            </h2>
            <p className="text-sm text-slate-300 leading-relaxed">
              Standalone native build with Android GNSS HAL integration, SGP4 Keplerian orbital verification, Galileo OSNMA cryptographic authentication, and Fuentecén (Ribera del Duero) reference base.
            </p>
          </div>

          <div className="flex flex-col sm:flex-row gap-3 w-full md:w-auto shrink-0">
            <a
              href={directApkUrl}
              download="TRISAT-v2.4-debug.apk"
              className="flex items-center justify-center gap-2.5 px-6 py-3 rounded-xl bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-bold text-sm transition shadow-lg shadow-cyan-500/25"
            >
              <Download className="w-4 h-4" />
              <span>Direct Download (APK)</span>
            </a>

            <a
              href={gdriveUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="flex items-center justify-center gap-2 px-5 py-3 rounded-xl bg-slate-800 hover:bg-slate-700 text-white font-semibold text-sm border border-slate-700 transition"
            >
              <Cloud className="w-4 h-4 text-cyan-400" />
              <span>Google Drive Mirror</span>
              <ExternalLink className="w-3 h-3 text-slate-400" />
            </a>
          </div>
        </div>
      </div>

      {/* Package Telemetry & Specs */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="p-4 bg-slate-900/60 border border-slate-800 rounded-xl space-y-1">
          <div className="flex items-center gap-2 text-slate-400 text-xs font-semibold">
            <HardDrive className="w-4 h-4 text-cyan-400" />
            <span>Package Size</span>
          </div>
          <p className="text-lg font-bold text-white font-mono">27.5 MB</p>
          <p className="text-[11px] text-slate-500">Universal Debug APK</p>
        </div>

        <div className="p-4 bg-slate-900/60 border border-slate-800 rounded-xl space-y-1">
          <div className="flex items-center gap-2 text-slate-400 text-xs font-semibold">
            <Smartphone className="w-4 h-4 text-emerald-400" />
            <span>Target OS</span>
          </div>
          <p className="text-lg font-bold text-white font-mono">Android 9.0 - 15</p>
          <p className="text-[11px] text-slate-500">API 28 min · API 35 target</p>
        </div>

        <div className="p-4 bg-slate-900/60 border border-slate-800 rounded-xl space-y-1">
          <div className="flex items-center gap-2 text-slate-400 text-xs font-semibold">
            <Cpu className="w-4 h-4 text-amber-400" />
            <span>Architecture</span>
          </div>
          <p className="text-lg font-bold text-white font-mono">arm64-v8a / x86_64</p>
          <p className="text-[11px] text-slate-500">Compatible with all modern devices</p>
        </div>

        <div className="p-4 bg-slate-900/60 border border-slate-800 rounded-xl space-y-1">
          <div className="flex items-center gap-2 text-slate-400 text-xs font-semibold">
            <ShieldCheck className="w-4 h-4 text-blue-400" />
            <span>Build Channel</span>
          </div>
          <p className="text-lg font-bold text-white font-mono">Debug Sideload</p>
          <p className="text-[11px] text-slate-500">Direct install without store</p>
        </div>
      </div>

      {/* Sideloading Walkthrough & Integrity Verification */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Step-by-Step Installation */}
        <div className="bg-slate-900/80 border border-slate-800 rounded-2xl p-6 space-y-4">
          <h3 className="text-sm font-bold uppercase tracking-wider text-cyan-400 flex items-center gap-2">
            <CheckCircle2 className="w-4 h-4" />
            <span>How to Install on Android</span>
          </h3>

          <ol className="space-y-3 text-xs text-slate-300">
            <li className="flex gap-3">
              <span className="w-5 h-5 rounded-full bg-cyan-500/20 text-cyan-400 font-bold flex items-center justify-center shrink-0 text-[11px]">1</span>
              <div>
                <strong className="text-white block">Download the APK</strong>
                Click the direct download button above or open the Google Drive mirror link on your phone.
              </div>
            </li>
            <li className="flex gap-3">
              <span className="w-5 h-5 rounded-full bg-cyan-500/20 text-cyan-400 font-bold flex items-center justify-center shrink-0 text-[11px]">2</span>
              <div>
                <strong className="text-white block">Allow Unknown Sources</strong>
                When prompted by Android, tap <em>Settings</em> and toggle <em>Allow from this source</em> for your browser or Google Drive.
              </div>
            </li>
            <li className="flex gap-3">
              <span className="w-5 h-5 rounded-full bg-cyan-500/20 text-cyan-400 font-bold flex items-center justify-center shrink-0 text-[11px]">3</span>
              <div>
                <strong className="text-white block">Install & Launch TRISAT</strong>
                Tap <em>Install</em>. Once opened, grant <em>Precise Location</em> permission so the HAL hardware callbacks can read real antenna raw measurements.
              </div>
            </li>
          </ol>
        </div>

        {/* SHA-256 Checksum & Path */}
        <div className="bg-slate-900/80 border border-slate-800 rounded-2xl p-6 space-y-4 flex flex-col justify-between">
          <div className="space-y-3">
            <h3 className="text-sm font-bold uppercase tracking-wider text-slate-300 flex items-center gap-2">
              <FileCode className="w-4 h-4 text-cyan-400" />
              <span>SHA-256 Integrity Checksum</span>
            </h3>

            <div className="p-3 bg-slate-950 rounded-xl border border-slate-800 font-mono text-[11px] text-cyan-300 break-all select-all">
              {sha256Checksum}
            </div>

            <div className="space-y-1 text-xs text-slate-400">
              <p><strong className="text-slate-300">Local Build Path:</strong></p>
              <code className="text-[11px] font-mono text-slate-400 block p-2 bg-slate-950/70 rounded-lg break-all">
                trisat_android/build/outputs/apk/debug/trisat_android-debug.apk
              </code>
            </div>
          </div>

          <div className="pt-2 text-[11px] text-slate-500 border-t border-slate-800">
            Centrado geodésico por defecto: <strong>Fuentecén (Ribera del Duero / Burgos, ES) · 41.6378° N, 3.8689° W</strong>
          </div>
        </div>
      </div>
    </div>
  );
};

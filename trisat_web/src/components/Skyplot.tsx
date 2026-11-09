import React, { useRef, useEffect, useState } from 'react';
import { Satellite, ConstellationType } from '../types/gnss';

interface SkyplotProps {
  satellites: Satellite[];
  elevationMask: number;
  selectedSatellite: Satellite | null;
  onSelectSatellite: (sat: Satellite | null) => void;
  width?: number;
  height?: number;
  showTrails?: boolean;
}

const CONSTELLATION_COLORS: Record<ConstellationType, { fill: string; stroke: string; glow: string; trail: string }> = {
  galileo: { fill: '#06b6d4', stroke: '#0891b2', glow: 'rgba(6, 182, 212, 0.4)', trail: 'rgba(6, 182, 212, 0.25)' },
  gps: { fill: '#f59e0b', stroke: '#d97706', glow: 'rgba(245, 158, 11, 0.4)', trail: 'rgba(245, 158, 11, 0.25)' },
  glonass: { fill: '#ef4444', stroke: '#dc2626', glow: 'rgba(239, 68, 68, 0.4)', trail: 'rgba(239, 68, 68, 0.25)' },
  beidou: { fill: '#10b981', stroke: '#059669', glow: 'rgba(16, 185, 129, 0.4)', trail: 'rgba(16, 185, 129, 0.25)' }
};

export const Skyplot: React.FC<SkyplotProps> = ({
  satellites,
  elevationMask,
  selectedSatellite,
  onSelectSatellite,
  width = 380,
  height = 380,
  showTrails = false
}) => {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const [hoveredSat, setHoveredSat] = useState<Satellite | null>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    // Handle high DPI
    const dpr = window.devicePixelRatio || 1;
    canvas.width = width * dpr;
    canvas.height = height * dpr;
    ctx.scale(dpr, dpr);

    const centerX = width / 2;
    const centerY = height / 2;
    const radius = Math.min(centerX, centerY) - 26;

    ctx.clearRect(0, 0, width, height);

    // Deep Space Radial Background
    const bgGrad = ctx.createRadialGradient(centerX, centerY, 0, centerX, centerY, radius + 15);
    bgGrad.addColorStop(0, '#040d1a');
    bgGrad.addColorStop(0.85, '#020617');
    bgGrad.addColorStop(1, '#000000');

    ctx.beginPath();
    ctx.arc(centerX, centerY, radius, 0, 2 * Math.PI);
    ctx.fillStyle = bgGrad;
    ctx.fill();

    // Elevation Mask Shadow (Outer ring below cutoff)
    if (elevationMask > 0) {
      const maskRadius = radius * (1 - elevationMask / 90);
      ctx.beginPath();
      ctx.arc(centerX, centerY, radius, 0, 2 * Math.PI);
      ctx.arc(centerX, centerY, maskRadius, 0, 2 * Math.PI, true);
      ctx.fillStyle = 'rgba(239, 68, 68, 0.08)';
      ctx.fill();

      // Mask dashed boundary
      ctx.beginPath();
      ctx.arc(centerX, centerY, maskRadius, 0, 2 * Math.PI);
      ctx.strokeStyle = 'rgba(239, 68, 68, 0.45)';
      ctx.setLineDash([4, 4]);
      ctx.lineWidth = 1;
      ctx.stroke();
      ctx.setLineDash([]);
    }

    // Concentric Elevation Rings (0° horizon, 30°, 60°)
    const rings = [
      { el: 0, label: '0°' },
      { el: 30, label: '30°' },
      { el: 60, label: '60°' }
    ];

    rings.forEach(({ el, label }) => {
      const r = radius * (1 - el / 90);
      ctx.beginPath();
      ctx.arc(centerX, centerY, r, 0, 2 * Math.PI);
      ctx.strokeStyle = el === 0 ? '#334155' : 'rgba(51, 65, 85, 0.45)';
      ctx.lineWidth = el === 0 ? 1.5 : 1;
      ctx.stroke();

      if (el > 0) {
        ctx.fillStyle = '#64748b';
        ctx.font = '10px JetBrains Mono, monospace';
        ctx.textAlign = 'left';
        ctx.fillText(label, centerX + 5, centerY - r + 11);
      }
    });

    // Azimuth Spokes (0° N, 45° NE, 90° E, 135° SE, 180° S, 225° SW, 270° W, 315° NW)
    ctx.strokeStyle = 'rgba(51, 65, 85, 0.35)';
    ctx.lineWidth = 1;

    for (let deg = 0; deg < 360; deg += 45) {
      const rad = (deg - 90) * (Math.PI / 180);
      ctx.beginPath();
      ctx.moveTo(centerX, centerY);
      ctx.lineTo(centerX + radius * Math.cos(rad), centerY + radius * Math.sin(rad));
      ctx.stroke();
    }

    // Cardinal Direction Labels
    const cardinals = [
      { label: '360° / N', deg: 0, color: '#38bdf8' },
      { label: 'E', deg: 90, color: '#94a3b8' },
      { label: '180° / S', deg: 180, color: '#94a3b8' },
      { label: 'W', deg: 270, color: '#94a3b8' }
    ];

    cardinals.forEach(({ label, deg, color }) => {
      const rad = (deg - 90) * (Math.PI / 180);
      const lx = centerX + (radius + 15) * Math.cos(rad);
      const ly = centerY + (radius + 15) * Math.sin(rad);
      ctx.fillStyle = color;
      ctx.font = 'bold 10px Plus Jakarta Sans, sans-serif';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText(label, lx, ly);
    });

    // Draw Simulated Orbit Heading Trails (similar to Trimble GNSS Planning arc trails)
    if (showTrails) {
      satellites.forEach(sat => {
        if (sat.elevation < 5) return;
        const colors = CONSTELLATION_COLORS[sat.constellation];
        const r = radius * (1 - sat.elevation / 90);
        const rad = (sat.azimuth - 90) * (Math.PI / 180);

        const arcLength = 0.22;
        const trailRadStart = rad - arcLength;
        const trailRStart = Math.min(radius, Math.max(0, r + 12 * Math.cos(sat.azimuth * Math.PI / 180)));

        ctx.beginPath();
        const startX = centerX + trailRStart * Math.cos(trailRadStart);
        const startY = centerY + trailRStart * Math.sin(trailRadStart);
        const curX = centerX + r * Math.cos(rad);
        const curY = centerY + r * Math.sin(rad);

        ctx.moveTo(startX, startY);
        ctx.quadraticCurveTo(
          centerX + (r + 4) * Math.cos(rad - arcLength / 2),
          centerY + (r + 4) * Math.sin(rad - arcLength / 2),
          curX,
          curY
        );
        ctx.strokeStyle = colors.trail;
        ctx.lineWidth = 1.5;
        ctx.stroke();
      });
    }

    // Draw Satellites
    satellites.forEach(sat => {
      const r = radius * (1 - sat.elevation / 90);
      const rad = (sat.azimuth - 90) * (Math.PI / 180);
      const sx = centerX + r * Math.cos(rad);
      const sy = centerY + r * Math.sin(rad);

      const isBelowMask = sat.elevation < elevationMask;
      const isSelected = selectedSatellite?.id === sat.id;
      const isHovered = hoveredSat?.id === sat.id;
      const colors = CONSTELLATION_COLORS[sat.constellation];

      ctx.save();

      // OSNMA Halo Effect (for authenticated Galileo satellites)
      if (sat.hasOsnma && !isBelowMask) {
        ctx.beginPath();
        ctx.arc(sx, sy, 13, 0, 2 * Math.PI);
        ctx.strokeStyle = 'rgba(6, 182, 212, 0.55)';
        ctx.lineWidth = 1.8;
        ctx.setLineDash([3, 2]);
        ctx.stroke();
        ctx.setLineDash([]);
      }

      // Selection indicator
      if (isSelected || isHovered) {
        ctx.beginPath();
        ctx.arc(sx, sy, 16, 0, 2 * Math.PI);
        ctx.strokeStyle = '#ffffff';
        ctx.lineWidth = 2;
        ctx.stroke();
      }

      // Satellite Circle
      ctx.beginPath();
      ctx.arc(sx, sy, 9, 0, 2 * Math.PI);
      ctx.fillStyle = isBelowMask ? '#334155' : colors.fill;
      ctx.fill();
      ctx.strokeStyle = isBelowMask ? '#1e293b' : colors.stroke;
      ctx.lineWidth = 1.5;
      ctx.stroke();

      // Satellite PRN Label inside circle
      ctx.fillStyle = isBelowMask ? '#94a3b8' : '#020617';
      ctx.font = 'bold 9px JetBrains Mono, monospace';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText(sat.id, sx, sy);

      // Sub-label (C/N0 dB-Hz)
      if (!isBelowMask) {
        ctx.fillStyle = sat.cn0 >= 35 ? '#4ade80' : sat.cn0 >= 25 ? '#facc15' : '#f87171';
        ctx.font = '9px JetBrains Mono, monospace';
        ctx.fillText(`${sat.cn0}`, sx, sy + 15);
      }

      ctx.restore();
    });

  }, [satellites, elevationMask, selectedSatellite, hoveredSat, width, height, showTrails]);

  // Click & Hover Detection
  const handleMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const rect = canvas.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;

    const centerX = width / 2;
    const centerY = height / 2;
    const radius = Math.min(centerX, centerY) - 26;

    const found = satellites.find(sat => {
      const r = radius * (1 - sat.elevation / 90);
      const rad = (sat.azimuth - 90) * (Math.PI / 180);
      const sx = centerX + r * Math.cos(rad);
      const sy = centerY + r * Math.sin(rad);
      const dist = Math.hypot(mx - sx, my - sy);
      return dist <= 14;
    });

    setHoveredSat(found || null);
    canvas.style.cursor = found ? 'pointer' : 'default';
  };

  const handleClick = () => {
    if (hoveredSat) {
      onSelectSatellite(selectedSatellite?.id === hoveredSat.id ? null : hoveredSat);
    }
  };

  return (
    <div className="relative flex flex-col items-center">
      <canvas
        ref={canvasRef}
        style={{ width: `${width}px`, height: `${height}px` }}
        onMouseMove={handleMouseMove}
        onMouseLeave={() => setHoveredSat(null)}
        onClick={handleClick}
        className="rounded-full shadow-2xl shadow-cyan-950/20 border border-slate-800/80 bg-slate-950"
      />
    </div>
  );
};

interface ZentinelaLogoProps {
  size?: number;
  className?: string;
}

export default function ZentinelaLogo({ size = 28, className }: ZentinelaLogoProps) {
  const stroke = Math.max(1.5, size * 0.085);
  const pad = size * 0.3;
  const left = pad;
  const right = size - pad;
  const top = pad;
  const bottom = size - pad;

  const dotCx = right;
  const dotCy = bottom;
  const dotOuter = size * 0.16;
  const dotInner = size * 0.07;

  return (
    <svg
      width={size}
      height={size}
      viewBox={`0 0 ${size} ${size}`}
      className={className}
      xmlns="http://www.w3.org/2000/svg"
      aria-label="Zentinela"
      role="img"
    >
      <rect width={size} height={size} rx={6} ry={6} fill="#F54927" />
      <line
        x1={left}
        y1={top}
        x2={right}
        y2={top}
        stroke="white"
        strokeWidth={stroke}
        strokeLinecap="round"
      />
      <line
        x1={right}
        y1={top}
        x2={left}
        y2={bottom}
        stroke="white"
        strokeWidth={stroke}
        strokeLinecap="round"
      />
      <line
        x1={left}
        y1={bottom}
        x2={right}
        y2={bottom}
        stroke="white"
        strokeWidth={stroke}
        strokeLinecap="round"
      />
      <circle cx={dotCx} cy={dotCy} r={dotOuter} fill="white" />
      <circle cx={dotCx} cy={dotCy} r={dotInner} fill="#F54927" />
    </svg>
  );
}

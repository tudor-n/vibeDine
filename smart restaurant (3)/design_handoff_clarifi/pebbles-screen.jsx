// Pebbles screen — redesigned to match the app's visual language.
// Background: T.paper (#F4EEE2), same header style, intro heading,
// then organic pebble blobs for each category.

// Inherits T tokens from screens.jsx (loaded first)

// True organic pebble shapes — blob-like, not squircles
const BLOB_PATHS = [
  // round but lumpy
  "M50,5 C68,3 88,16 92,36 C97,58 84,82 64,92 C44,102 18,94 9,74 C0,54 8,26 24,14 C36,5 42,6 50,5 Z",
  // wide squat
  "M50,10 C70,6 90,18 94,40 C98,62 86,86 64,93 C42,100 16,90 8,68 C0,46 10,20 28,12 C38,7 44,12 50,10 Z",
  // tall egg
  "M50,4 C66,2 84,18 88,40 C92,64 80,88 60,95 C40,102 16,92 8,70 C0,48 10,22 28,10 C38,4 44,5 50,4 Z",
  // asymmetric lean-right
  "M52,6 C72,4 92,20 94,42 C96,66 82,88 60,94 C38,100 12,88 6,66 C0,44 12,18 32,9 C42,4 46,7 52,6 Z",
  // small chunky
  "M50,8 C68,5 88,20 90,42 C92,64 78,86 58,93 C38,100 14,90 7,68 C0,46 12,20 30,11 C40,6 44,9 50,8 Z",
  // wide round
  "M50,7 C72,4 92,18 96,40 C100,64 86,88 62,95 C38,102 12,92 5,68 C-2,44 10,18 30,9 C40,4 42,9 50,7 Z",
];

const PEB_DATA = [
  { id: 'principale', label: 'Feluri\nprincipale', count: 14, path: 0,
    img: 'https://images.unsplash.com/photo-1544025162-d76694265947?w=500&q=80' },
  { id: 'salate',     label: 'Salate',            count: 6,  path: 1,
    img: 'https://images.unsplash.com/photo-1512621776951-a57141f2eefd?w=500&q=80' },
  { id: 'supe',       label: 'Supe &\nciorbe',    count: 8,  path: 2,
    img: 'https://images.unsplash.com/photo-1547592180-85f173990554?w=500&q=80' },
  { id: 'bauturi',    label: 'Băuturi',           count: 22, path: 3,
    img: 'https://images.unsplash.com/photo-1551024709-8f23befc6f87?w=500&q=80' },
  { id: 'deserturi',  label: 'Deserturi',         count: 6,  path: 4,
    img: 'https://images.unsplash.com/photo-1488477181946-6428a0291777?w=500&q=80' },
  { id: 'vinuri',     label: 'Vinuri',            count: 18, path: 5,
    img: 'https://images.unsplash.com/photo-1510812431401-41d2bd2722f3?w=500&q=80' },
];

function PebblesScreen() {
  const [active, setActive] = React.useState(null);

  return (
    <div style={{
      width: '100%', height: '100%',
      position: 'relative', overflow: 'hidden',
      fontFamily: T.sans, color: T.ink,
      background: T.paper,
      paddingTop: T.STATUS_CLEAR,
      display: 'flex', flexDirection: 'column',
    }}>
      {/* grain — same as Screen component */}
      <div style={{
        position: 'absolute', inset: 0, pointerEvents: 'none', opacity: 0.35, mixBlendMode: 'multiply',
        backgroundImage: `url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='1.1' numOctaves='2' stitchTiles='stitch'/><feColorMatrix values='0 0 0 0 0.08  0 0 0 0 0.07  0 0 0 0 0.06  0 0 0 0.05 0'/></filter><rect width='100%25' height='100%25' filter='url(%23n)'/></svg>")`,
      }} />

      {/* Header — matches other screens */}
      <div style={{
        position: 'relative', padding: '14px 22px 0',
        display: 'flex', alignItems: 'center', justifyContent: 'space-between',
        flexShrink: 0,
      }}>
        <button style={{
          width: 36, height: 36, borderRadius: '50%',
          background: 'rgba(21,20,15,0.06)',
          border: '1px solid rgba(21,20,15,0.10)',
          display: 'grid', placeItems: 'center',
          color: T.ink, fontSize: 15, cursor: 'pointer',
          fontFamily: T.sans,
        }}>←</button>
        <span style={{
          fontFamily: T.mono, fontSize: 10, fontWeight: 500,
          letterSpacing: '0.14em', textTransform: 'uppercase',
          color: T.ink70,
          border: '1px solid rgba(21,20,15,0.10)',
          padding: '5px 9px', borderRadius: 999,
        }}>Masa 07</span>
      </div>

      {/* Heading */}
      <div style={{ position: 'relative', padding: '26px 22px 4px', flexShrink: 0 }}>
        <div style={{
          fontFamily: T.mono, fontSize: 10, letterSpacing: '0.18em',
          color: T.accent, textTransform: 'uppercase', marginBottom: 10,
        }}>
          Alegeți categoria —
        </div>
        <h2 style={{
          fontFamily: T.display, fontWeight: 500,
          fontSize: 40, lineHeight: 0.92, letterSpacing: '-0.04em',
          margin: 0, color: T.ink,
        }}>
          Ce aveți<br/>
          <span style={{ color: T.accent }}>poftă să</span><br/>
          mâncați?
        </h2>
      </div>

      {/* Pebble grid — white panel, 2 columns, staggered */}
      <div style={{
        position: 'relative', flex: 1, overflow: 'hidden auto',
        marginTop: 8,
        marginLeft: 14,
        marginRight: 14,
        padding: '0 0 90px',
      }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 29 }}>
          {[0, 1, 2].map(group => (
            <div key={group} style={{
              background: T.card,
              border: 'none',
              borderRadius: 22,
              padding: '16px 14px 20px',
            }}>
              <div style={{
                display: 'grid', gridTemplateColumns: '1fr 1fr',
                gap: '0 10px', alignItems: 'start',
              }}>
                {PEB_DATA.slice(group * 2, group * 2 + 2).map((pb, i) => {
            const isActive = pb.id === active;
            const path = BLOB_PATHS[pb.path];
            const clipId = `pc-${pb.id}`;
            const hiId = `ph-${pb.id}`;
            const staggerTop = i % 2 === 1 ? 22 : 0;

            return (
              <div
                key={pb.id}
                onClick={() => setActive(isActive ? null : pb.id)}
                style={{
                  marginTop: staggerTop,
                  cursor: 'pointer',
                  display: 'flex', flexDirection: 'column', alignItems: 'center',
                  gap: 10,
                }}
              >
                {/* Pebble SVG */}
                <div style={{
                  transform: `scale(${isActive ? 1.06 : 1}) translateY(${isActive ? -4 : 0}px)`,
                  transition: 'transform 0.5s cubic-bezier(0.34,1.4,0.64,1)',
                  filter: isActive
                    ? `drop-shadow(0 18px 22px rgba(180,40,10,0.30)) drop-shadow(0 4px 8px rgba(180,40,10,0.16))`
                    : `drop-shadow(0 10px 16px rgba(21,20,15,0.16)) drop-shadow(0 2px 4px rgba(21,20,15,0.08))`,
                }}>
                  <svg viewBox="0 0 100 100" width="148" height="148" style={{ display: 'block', overflow: 'visible' }}>
                    <defs>
                      <clipPath id={clipId}>
                        <path d={path} />
                      </clipPath>
                      <radialGradient id={hiId} cx="32%" cy="25%" r="52%">
                        <stop offset="0%" stopColor="rgba(255,255,255,0.75)" />
                        <stop offset="60%" stopColor="rgba(255,255,255,0.12)" />
                        <stop offset="100%" stopColor="rgba(255,255,255,0)" />
                      </radialGradient>
                      <linearGradient id={`psh-${pb.id}`} x1="50%" y1="0%" x2="50%" y2="100%">
                        <stop offset="0%" stopColor="rgba(255,255,255,0.1)" />
                        <stop offset="100%" stopColor="rgba(21,20,15,0.28)" />
                      </linearGradient>
                    </defs>

                    {/* Photo fill */}
                    <image
                      href={pb.img}
                      x="0" y="0" width="100" height="100"
                      preserveAspectRatio="xMidYMid slice"
                      clipPath={`url(#${clipId})`}
                    />

                    {/* Bottom shading */}
                    <path d={path} fill={`url(#psh-${pb.id})`} opacity={isActive ? 0.35 : 0.55} />

                    {/* Specular */}
                    <path d={path} fill={`url(#${hiId})`} />

                    {/* Contour */}
                    <path d={path} fill="none"
                      stroke={isActive ? T.accent : 'rgba(21,20,15,0.12)'}
                      strokeWidth={isActive ? 1.4 : 0.6} />

                    {/* Active inner cream ring */}
                    {isActive && (
                      <path d={path} fill="none" stroke="rgba(255,244,236,0.6)" strokeWidth="2.8" />
                    )}
                  </svg>
                </div>

                {/* Label */}
                <div style={{ textAlign: 'center' }}>
                  <div style={{
                    fontWeight: 700, fontSize: 13, letterSpacing: '-0.01em',
                    color: isActive ? T.accent : T.ink, lineHeight: 1.15,
                    whiteSpace: 'pre-line', transition: 'color 0.3s',
                    textAlign: 'center',
                  }}>
                    {pb.label}
                  </div>
                  <div style={{
                    fontFamily: T.mono, fontSize: 9, letterSpacing: '0.18em',
                    color: T.ink40, marginTop: 3,
                  }}>
                    {String(pb.count).padStart(2,'0')} items
                  </div>
                </div>
              </div>
            );
                })}
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Sticky bottom bar — shows when something selected */}
      <div style={{
        position: 'absolute', bottom: 0, left: 0, right: 0,
        padding: '10px 22px 28px',
        transform: active ? 'translateY(0)' : 'translateY(110%)',
        transition: 'transform 0.4s cubic-bezier(0.32,0.72,0,1)',
        zIndex: 10,
      }}>
        {(() => {
          const pb = PEB_DATA.find(p => p.id === active) || PEB_DATA[0];
          return (
            <button style={{
              width: '100%', padding: '16px 20px', border: 'none',
              background: T.accent, color: T.paperHi,
              fontFamily: T.sans, fontSize: 15, fontWeight: 600, letterSpacing: '-0.01em',
              borderRadius: 999, cursor: 'pointer',
              display: 'flex', justifyContent: 'space-between', alignItems: 'center',
              boxShadow: `0 12px 28px -8px ${T.accent}66`,
            }}>
              <span>{pb.label.replace('\n', ' ')}</span>
              <span style={{
                width: 30, height: 30, borderRadius: '50%', background: T.paperHi, color: T.accent,
                display: 'grid', placeItems: 'center', fontSize: 16, flexShrink: 0,
              }}>→</span>
            </button>
          );
        })()}
      </div>
    </div>
  );
}

window.PebblesScreen = PebblesScreen;

// Clarifi — Accessible screens, Dark direction.
// Same TD tokens as screens-dark.jsx (loaded first).
// Designed for older guests: large text, big touch targets, simple layouts.

// ─────────────────────────────────────────────────────────────
// Accessible dark atoms
// ─────────────────────────────────────────────────────────────
function ADScreen({ children, bg = TD.paper, style = {} }) {
  return (
    <div style={{
      width: '100%', height: '100%', background: bg, color: TD.ink,
      fontFamily: TD.sans, position: 'relative', overflow: 'hidden',
      paddingTop: TD.STATUS_CLEAR, ...style,
    }}>
      <div style={{
        position: 'absolute', inset: 0, pointerEvents: 'none', opacity: 0.2, mixBlendMode: 'screen',
        backgroundImage: `url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='1.1' numOctaves='2' stitchTiles='stitch'/><feColorMatrix values='0 0 0 0 0.9  0 0 0 0 0.88  0 0 0 0 0.85  0 0 0 0.06 0'/></filter><rect width='100%25' height='100%25' filter='url(%23n)'/></svg>")`,
      }} />
      {children}
    </div>
  );
}

function ADBigBtn({ children, bg = TD.accent, color = TD.ink, style = {}, ...rest }) {
  return (
    <button style={{
      width: '100%', padding: '20px 24px',
      border: 'none', borderRadius: 999,
      background: bg, color,
      fontFamily: TD.sans, fontSize: 19, fontWeight: 700,
      letterSpacing: '-0.01em', cursor: 'pointer',
      display: 'flex', justifyContent: 'space-between', alignItems: 'center',
      boxShadow: `0 10px 24px -8px ${bg}66`,
      ...style,
    }} {...rest}>
      {children}
    </button>
  );
}

function ADArrow({ bg = TD.ink, color = TD.accent }) {
  return (
    <span style={{
      width: 38, height: 38, borderRadius: '50%',
      background: bg, color,
      display: 'grid', placeItems: 'center',
      fontSize: 20, flexShrink: 0,
    }}>→</span>
  );
}

// ─────────────────────────────────────────────────────────────
// 1. WELCOME
// ─────────────────────────────────────────────────────────────
function WelcomeScreenA11yDark() {
  return (
    <ADScreen>
      <div style={{ position: 'absolute', top: -60, right: -80, width: 300, height: 300, borderRadius: '50%', filter: 'blur(50px)', opacity: 0.45, background: `radial-gradient(circle, ${TD.accent} 0%, transparent 70%)` }} />
      <div style={{ position: 'absolute', top: 200, left: -80, width: 240, height: 240, borderRadius: '50%', filter: 'blur(60px)', opacity: 0.28, background: `radial-gradient(circle, ${TD.lime} 0%, transparent 70%)` }} />

      <div style={{ position: 'relative', padding: '12px 24px 0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 14, fontWeight: 600, color: TD.ink70 }}>
          <span style={{ width: 10, height: 10, borderRadius: '50%', background: TD.accent, boxShadow: `0 0 8px ${TD.accent}` }} />
          Masa 7
        </div>
        <div style={{ fontSize: 14, color: TD.ink70, fontWeight: 500 }}>19:42</div>
      </div>

      <div style={{ position: 'relative', padding: '52px 24px 0' }}>
        <div style={{ fontSize: 15, fontWeight: 600, color: TD.accent, letterSpacing: '0.04em', textTransform: 'uppercase', marginBottom: 14 }}>Bun venit</div>
        <h1 style={{ fontFamily: TD.display, fontWeight: 600, fontSize: 60, lineHeight: 0.92, letterSpacing: '-0.04em', margin: '0 0 24px' }}>
          Ospătăria<br/><span style={{ color: TD.accent }}>Curții.</span>
        </h1>
        <p style={{ fontSize: 18, lineHeight: 1.55, color: TD.ink70, margin: '0 0 32px' }}>
          Meniuri pentru masa 7. Comandați direct din aplicație — bucătăria pregătește imediat.
        </p>

        <div style={{ background: TD.card, borderRadius: 20, padding: '16px 20px', border: `1px solid ${TD.ink12}`, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px 0', marginBottom: 24 }}>
          {[['Masă','07 · Terasă'],['Oaspeți','2 persoane'],['Chef','Andrei C.'],['Comanda','#4821']].map(([k,v]) => (
            <div key={k}>
              <div style={{ fontSize: 12, color: TD.ink40, fontWeight: 500, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 2 }}>{k}</div>
              <div style={{ fontSize: 17, fontWeight: 600 }}>{v}</div>
            </div>
          ))}
        </div>

        <div style={{ background: TD.paperHi, borderRadius: 22, padding: '18px 20px', display: 'flex', gap: 16, alignItems: 'center', border: `1px solid ${TD.ink12}` }}>
          <DFoodTile label="AZI" tone="pink" style={{ width: 72, height: 88, flexShrink: 0, borderRadius: 14 }} />
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: 12, fontWeight: 600, color: TD.lime, letterSpacing: '0.06em', marginBottom: 6 }}>RECOMANDAREA ZILEI</div>
            <div style={{ fontSize: 18, fontWeight: 600, lineHeight: 1.2, marginBottom: 4 }}>Mușchi de vită Oltenia</div>
            <div style={{ fontSize: 15, color: TD.ink70 }}>Maturat 28 zile · 128 RON</div>
          </div>
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 32, left: 20, right: 20 }}>
        <ADBigBtn>
          <span>Deschide meniul</span>
          <ADArrow />
        </ADBigBtn>
      </div>
    </ADScreen>
  );
}

// ─────────────────────────────────────────────────────────────
// 2. PEBBLES (accessible dark)
// ─────────────────────────────────────────────────────────────
const AD_BLOB_PATHS = [
  "M50,5 C68,3 88,16 92,36 C97,58 84,82 64,92 C44,102 18,94 9,74 C0,54 8,26 24,14 C36,5 42,6 50,5 Z",
  "M50,10 C70,6 90,18 94,40 C98,62 86,86 64,93 C42,100 16,90 8,68 C0,46 10,20 28,12 C38,7 44,12 50,10 Z",
  "M50,4 C66,2 84,18 88,40 C92,64 80,88 60,95 C40,102 16,92 8,70 C0,48 10,22 28,10 C38,4 44,5 50,4 Z",
  "M52,6 C72,4 92,20 94,42 C96,66 82,88 60,94 C38,100 12,88 6,66 C0,44 12,18 32,9 C42,4 46,7 52,6 Z",
  "M50,8 C68,5 88,20 90,42 C92,64 78,86 58,93 C38,100 14,90 7,68 C0,46 12,20 30,11 C40,6 44,9 50,8 Z",
  "M50,7 C72,4 92,18 96,40 C100,64 86,88 62,95 C38,102 12,92 5,68 C-2,44 10,18 30,9 C40,4 42,9 50,7 Z",
];

const AD_PEB_DATA = [
  { id: 'ad-principale', label: 'Feluri principale', count: 14, path: 0, img: 'https://images.unsplash.com/photo-1544025162-d76694265947?w=500&q=80' },
  { id: 'ad-salate',     label: 'Salate',            count: 6,  path: 1, img: 'https://images.unsplash.com/photo-1512621776951-a57141f2eefd?w=500&q=80' },
  { id: 'ad-supe',       label: 'Supe & Ciorbe',     count: 8,  path: 2, img: 'https://images.unsplash.com/photo-1547592180-85f173990554?w=500&q=80' },
  { id: 'ad-bauturi',    label: 'Băuturi',           count: 22, path: 3, img: 'https://images.unsplash.com/photo-1551024709-8f23befc6f87?w=500&q=80' },
  { id: 'ad-deserturi',  label: 'Deserturi',         count: 6,  path: 4, img: 'https://images.unsplash.com/photo-1488477181946-6428a0291777?w=500&q=80' },
  { id: 'ad-vinuri',     label: 'Vinuri',            count: 18, path: 5, img: 'https://images.unsplash.com/photo-1510812431401-41d2bd2722f3?w=500&q=80' },
];

function PebblesScreenA11yDark() {
  const [active, setActive] = React.useState(null);
  const [navigated, setNavigated] = React.useState(null);

  const handlePebbleClick = (pb) => {
    setActive(pb.id);
    setTimeout(() => setNavigated(pb), 320);
  };

  if (navigated) {
    return (
      <div style={{ width: '100%', height: '100%', position: 'relative' }}>
        <MenuScreenA11yDark />
        <button onClick={() => { setNavigated(null); setActive(null); }} style={{
          position: 'absolute', top: TD.STATUS_CLEAR + 10, left: 20,
          width: 48, height: 48, borderRadius: '50%',
          background: TD.card, border: `1px solid ${TD.ink12}`,
          display: 'grid', placeItems: 'center',
          fontSize: 20, cursor: 'pointer', zIndex: 20, color: TD.ink,
          boxShadow: '0 4px 12px rgba(0,0,0,0.3)',
        }}>←</button>
      </div>
    );
  }

  return (
    <div style={{
      width: '100%', height: '100%', position: 'relative', overflow: 'hidden',
      fontFamily: TD.sans, color: TD.ink, background: TD.paper,
      paddingTop: TD.STATUS_CLEAR, display: 'flex', flexDirection: 'column',
    }}>
      <div style={{
        position: 'absolute', inset: 0, pointerEvents: 'none', opacity: 0.2, mixBlendMode: 'screen',
        backgroundImage: `url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='1.1' numOctaves='2' stitchTiles='stitch'/><feColorMatrix values='0 0 0 0 0.9  0 0 0 0 0.88  0 0 0 0 0.85  0 0 0 0.06 0'/></filter><rect width='100%25' height='100%25' filter='url(%23n)'/></svg>")`,
      }} />

      <div style={{ position: 'relative', padding: '12px 20px 0', display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexShrink: 0 }}>
        <button style={{ width: 48, height: 48, borderRadius: '50%', background: TD.card, border: `1px solid ${TD.ink12}`, display: 'grid', placeItems: 'center', color: TD.ink, fontSize: 20, cursor: 'pointer', fontFamily: TD.sans }}>←</button>
        <div style={{ fontSize: 16, fontWeight: 600, color: TD.ink70 }}>Masa 7</div>
      </div>

      <div style={{ position: 'relative', padding: '22px 24px 6px', flexShrink: 0 }}>
        <h2 style={{ fontFamily: TD.display, fontWeight: 600, fontSize: 36, lineHeight: 1.05, letterSpacing: '-0.03em', margin: 0 }}>
          Ce doriți <span style={{ color: TD.accent }}>să mâncați?</span>
        </h2>
        <p style={{ fontSize: 16, color: TD.ink70, margin: '8px 0 0' }}>Atingeți o categorie pentru a vedea meniul</p>
      </div>

      <div style={{ position: 'relative', flex: 1, overflow: 'hidden auto', padding: '12px 16px 100px' }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          {[0, 1, 2].map(group => (
            <div key={group} style={{
              background: 'rgba(232,37,90,0.16)',
              border: '1px solid rgba(232,37,90,0.28)',
              borderRadius: 24,
              padding: '18px 14px 22px',
            }}>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0 10px', alignItems: 'start' }}>
                {AD_PEB_DATA.slice(group * 2, group * 2 + 2).map((pb) => {
                  const isActive = pb.id === active;
                  const path = AD_BLOB_PATHS[pb.path];
                  const clipId = `adpc-${pb.id}`;
                  const hiId = `adph-${pb.id}`;
                  return (
                    <button
                      key={pb.id}
                      onClick={() => handlePebbleClick(pb)}
                      style={{ cursor: 'pointer', border: 'none', background: 'transparent', padding: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 10 }}
                    >
                      <div style={{
                        transform: `scale(${isActive ? 1.06 : 1}) translateY(${isActive ? -4 : 0}px)`,
                        transition: 'transform 0.45s cubic-bezier(0.34,1.4,0.64,1)',
                        filter: isActive
                          ? `drop-shadow(0 16px 22px ${TD.accent}66) drop-shadow(0 4px 8px ${TD.accent}33)`
                          : 'drop-shadow(0 8px 16px rgba(0,0,0,0.55))',
                      }}>
                        <svg viewBox="0 0 100 100" width="158" height="158" style={{ display: 'block', overflow: 'visible' }}>
                          <defs>
                            <clipPath id={clipId}><path d={path} /></clipPath>
                            <radialGradient id={hiId} cx="32%" cy="25%" r="52%">
                              <stop offset="0%" stopColor="rgba(255,255,255,0.6)" />
                              <stop offset="60%" stopColor="rgba(255,255,255,0.1)" />
                              <stop offset="100%" stopColor="rgba(255,255,255,0)" />
                            </radialGradient>
                            <linearGradient id={`adpsh-${pb.id}`} x1="50%" y1="0%" x2="50%" y2="100%">
                              <stop offset="0%" stopColor="rgba(255,255,255,0.05)" />
                              <stop offset="100%" stopColor="rgba(0,0,0,0.5)" />
                            </linearGradient>
                          </defs>
                          <image href={pb.img} x="0" y="0" width="100" height="100" preserveAspectRatio="xMidYMid slice" clipPath={`url(#${clipId})`} />
                          <path d={path} fill={`url(#adpsh-${pb.id})`} opacity={isActive ? 0.35 : 0.6} />
                          <path d={path} fill={`url(#${hiId})`} />
                          <path d={path} fill="none" stroke={TD.accent} strokeWidth={isActive ? 2.5 : 1.6} />
                          {isActive && <path d={path} fill="none" stroke="rgba(245,240,232,0.3)" strokeWidth="3.5" />}
                        </svg>
                      </div>
                      <div style={{ textAlign: 'center', paddingBottom: 2 }}>
                        <div style={{ fontSize: 16, fontWeight: 700, letterSpacing: '-0.01em', color: TD.accent, lineHeight: 1.2, transition: 'color 0.3s' }}>
                          {pb.label}
                        </div>
                        <div style={{ fontSize: 14, color: TD.ink70, fontWeight: 500, marginTop: 3 }}>{pb.count} preparate</div>
                      </div>
                    </button>
                  );
                })}
              </div>
            </div>
          ))}
        </div>
      </div>


    </div>
  );
}

// ─────────────────────────────────────────────────────────────
// 3. MENU (accessible dark)
// ─────────────────────────────────────────────────────────────
function MenuScreenA11yDark() {
  return (
    <ADScreen>
      <div style={{ padding: '10px 20px 14px', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <button style={{ width: 48, height: 48, borderRadius: '50%', background: TD.card, border: `1px solid ${TD.ink12}`, display: 'grid', placeItems: 'center', fontSize: 20, cursor: 'pointer', color: TD.ink }}>←</button>
        <div style={{ textAlign: 'center' }}>
          <div style={{ fontSize: 18, fontWeight: 700, letterSpacing: '-0.01em' }}>Feluri Principale</div>
          <div style={{ fontSize: 14, color: TD.ink70, marginTop: 2 }}>14 preparate</div>
        </div>
        <button style={{ width: 48, height: 48, borderRadius: '50%', background: TD.card, border: `1px solid ${TD.ink12}`, display: 'grid', placeItems: 'center', fontSize: 20, color: TD.ink70, cursor: 'pointer' }}>⌕</button>
      </div>

      <div style={{ display: 'flex', gap: 8, padding: '0 20px 16px', overflowX: 'auto', whiteSpace: 'nowrap' }}>
        {['Toate', 'Carne', 'Pește', 'Vegetarian'].map((t, i) => (
          <span key={t} style={{ fontSize: 15, fontWeight: 600, padding: '10px 18px', borderRadius: 999, background: i === 0 ? TD.accent : TD.card, color: i === 0 ? TD.ink : TD.ink70, border: i === 0 ? 'none' : `1px solid ${TD.ink12}`, flexShrink: 0 }}>{t}</span>
        ))}
      </div>

      <div style={{ padding: '0 16px 18px', display: 'flex', flexDirection: 'column', gap: 12 }}>
        {dMains.map((m) => (
          <article key={m.id} style={{ background: TD.card, borderRadius: 22, padding: 14, border: `1px solid ${TD.ink12}` }}>
            <div style={{ display: 'grid', gridTemplateColumns: '100px 1fr', gap: 16, marginBottom: 12 }}>
              <DFoodTile label={m.id.toUpperCase()} tone={m.tone} style={{ width: 100, height: 100, borderRadius: 16 }} />
              <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'space-between', padding: '2px 0' }}>
                <div>
                  {m.tag && <div style={{ display: 'inline-block', marginBottom: 6, fontSize: 12, fontWeight: 700, color: TD.accent, textTransform: 'uppercase', letterSpacing: '0.06em' }}>{m.tag}</div>}
                  <div style={{ fontSize: 18, fontWeight: 700, letterSpacing: '-0.01em', lineHeight: 1.2, marginBottom: 5 }}>{m.name}</div>
                  <div style={{ fontSize: 14, color: TD.ink70, lineHeight: 1.5 }}>{m.desc}</div>
                </div>
              </div>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', paddingTop: 10, borderTop: `1px solid ${TD.ink12}` }}>
              <div style={{ fontSize: 24, fontWeight: 700, letterSpacing: '-0.02em' }}>{m.price} <span style={{ fontSize: 15, color: TD.ink70, fontWeight: 600 }}>RON</span></div>
              <button style={{ background: TD.accent, color: TD.ink, border: 'none', height: 48, paddingLeft: 20, paddingRight: 20, borderRadius: 999, fontSize: 16, fontWeight: 700, cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 8 }}>
                <span style={{ fontSize: 22 }}>+</span> Adaugă
              </button>
            </div>
          </article>
        ))}
      </div>

      <div style={{ padding: '16px 16px 110px', background: TD.paperHi, borderTop: `1px solid ${TD.ink12}` }}>
        <div style={{ display: 'inline-flex', alignItems: 'center', gap: 8, background: TD.lime, color: TD.paper, borderRadius: 999, padding: '8px 16px', fontSize: 14, fontWeight: 700, marginBottom: 12 }}>
          Recomandate pentru dumneavoastră
        </div>
        <div style={{ display: 'flex', gap: 12, overflowX: 'auto', paddingBottom: 4 }}>
          {[
            { name: 'Fetească Neagră', sub: 'Vin · pahar', price: 42, tone: 'ink' },
            { name: 'Unt cu trufe', sub: 'Aperitiv', price: 18, tone: 'gold' },
            { name: 'Salată verde', sub: 'Garnitură', price: 28, tone: 'sage' },
          ].map((r, i) => (
            <div key={i} style={{ minWidth: 170, background: TD.card, border: `1px solid ${TD.ink12}`, borderRadius: 20, padding: 12, display: 'flex', flexDirection: 'column', gap: 6 }}>
              <DFoodTile label="" tone={r.tone} style={{ height: 80, borderRadius: 14 }} />
              <div style={{ fontSize: 16, fontWeight: 700, lineHeight: 1.2 }}>{r.name}</div>
              <div style={{ fontSize: 13, color: TD.ink70 }}>{r.sub}</div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 4 }}>
                <span style={{ fontSize: 18, fontWeight: 700 }}>{r.price} <span style={{ fontSize: 12, color: TD.ink70, fontWeight: 500 }}>RON</span></span>
                <button style={{ background: TD.accent, color: TD.ink, border: 'none', borderRadius: 999, width: 40, height: 40, fontSize: 22, fontWeight: 700, cursor: 'pointer', display: 'grid', placeItems: 'center' }}>+</button>
              </div>
            </div>
          ))}
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 24, left: 14, right: 14, background: TD.ink, color: TD.paper, padding: '14px 16px 14px 22px', borderRadius: 999, display: 'flex', alignItems: 'center', justifyContent: 'space-between', boxShadow: '0 20px 40px -16px rgba(0,0,0,0.6)' }}>
        <div>
          <div style={{ fontSize: 13, color: 'rgba(27,24,20,0.55)', fontWeight: 500, marginBottom: 2 }}>Coșul meu</div>
          <div style={{ fontSize: 20, fontWeight: 700 }}>218 RON · 3 items</div>
        </div>
        <div style={{ background: TD.accent, color: TD.ink, borderRadius: 999, padding: '13px 20px', fontSize: 15, fontWeight: 700 }}>Coș →</div>
      </div>
    </ADScreen>
  );
}

// ─────────────────────────────────────────────────────────────
// 4. CART (accessible dark)
// ─────────────────────────────────────────────────────────────
function CartScreenA11yDark() {
  const subtotal = dCartItems.reduce((s, i) => s + i.price * i.qty, 0);
  return (
    <ADScreen>
      <div style={{ padding: '10px 22px 16px', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <div style={{ fontSize: 15, fontWeight: 600, color: TD.ink70 }}>Masa 7</div>
        <div style={{ fontSize: 22, fontWeight: 700, letterSpacing: '-0.02em' }}>Comanda mea</div>
        <button style={{ width: 44, height: 44, borderRadius: '50%', background: TD.card, border: `1px solid ${TD.ink12}`, display: 'grid', placeItems: 'center', fontSize: 18, cursor: 'pointer', color: TD.ink }}>✕</button>
      </div>

      <p style={{ fontSize: 16, color: TD.ink70, margin: '0 22px 16px', lineHeight: 1.45 }}>3 preparate selectate · pregătire aproximativ 22 de minute</p>

      <div style={{ padding: '0 16px', display: 'flex', flexDirection: 'column', gap: 10 }}>
        {dCartItems.map((it) => (
          <div key={it.id} style={{ background: TD.card, border: `1px solid ${TD.ink12}`, borderRadius: 22, padding: '14px 16px', display: 'grid', gridTemplateColumns: '68px 1fr', gap: 14 }}>
            <DFoodTile label="" tone={it.tone} style={{ width: 68, height: 68, borderRadius: 14 }} />
            <div>
              <div style={{ fontSize: 18, fontWeight: 700, letterSpacing: '-0.01em', lineHeight: 1.2, marginBottom: 4 }}>{it.name}</div>
              {it.note && <div style={{ display: 'inline-block', fontSize: 13, fontWeight: 700, color: TD.paper, background: TD.lime, padding: '3px 10px', borderRadius: 999, marginBottom: 6 }}>{it.note}</div>}
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginTop: 8 }}>
                <div style={{ fontSize: 22, fontWeight: 700 }}>{it.price * it.qty} <span style={{ fontSize: 14, color: TD.ink70, fontWeight: 500 }}>RON</span></div>
                <div style={{ display: 'inline-flex', alignItems: 'center', background: TD.paperHi, borderRadius: 999, border: `1px solid ${TD.ink12}` }}>
                  <button style={{ width: 40, height: 40, display: 'grid', placeItems: 'center', fontSize: 22, color: TD.ink70, background: 'none', border: 'none', cursor: 'pointer' }}>−</button>
                  <span style={{ width: 28, textAlign: 'center', fontSize: 18, fontWeight: 700 }}>{it.qty}</span>
                  <button style={{ width: 40, height: 40, display: 'grid', placeItems: 'center', fontSize: 22, color: TD.accent, background: 'none', border: 'none', cursor: 'pointer' }}>+</button>
                </div>
              </div>
            </div>
          </div>
        ))}
      </div>

      <div style={{ padding: '16px 22px 0' }}>
        <div style={{ background: TD.card, border: `1px solid ${TD.ink12}`, borderRadius: 20, padding: '16px 18px' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 16, color: TD.ink70, marginBottom: 8 }}><span>Subtotal</span><span>{subtotal} RON</span></div>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 16, color: TD.ink70, marginBottom: 12 }}><span>Serviciu</span><span>inclus</span></div>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', borderTop: `1px solid ${TD.ink12}`, paddingTop: 12 }}>
            <span style={{ fontSize: 18, fontWeight: 600 }}>Total</span>
            <span style={{ fontSize: 32, fontWeight: 800, letterSpacing: '-0.03em' }}>{subtotal} <span style={{ fontSize: 16, color: TD.ink70, fontWeight: 600 }}>RON</span></span>
          </div>
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 28, left: 18, right: 18 }}>
        <ADBigBtn>
          <span>Trimite la bucătărie</span>
          <ADArrow />
        </ADBigBtn>
      </div>
    </ADScreen>
  );
}

// ─────────────────────────────────────────────────────────────
// 5. FLASH DEAL (accessible dark)
// ─────────────────────────────────────────────────────────────
function FlashDealScreenA11yDark() {
  return (
    <ADScreen>
      <div style={{ position: 'absolute', top: -60, right: -80, width: 300, height: 300, borderRadius: '50%', filter: 'blur(50px)', opacity: 0.45, background: `radial-gradient(circle, ${TD.accent} 0%, transparent 70%)` }} />

      <div style={{ position: 'relative', padding: '10px 24px 0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <div style={{ display: 'inline-flex', alignItems: 'center', gap: 8, background: `rgba(232,37,90,0.18)`, color: TD.accent, padding: '8px 16px', borderRadius: 999, fontSize: 15, fontWeight: 700 }}>
          <span style={{ width: 10, height: 10, borderRadius: '50%', background: TD.accent, boxShadow: `0 0 8px ${TD.accent}` }} />
          Ofertă specială
        </div>
        <div style={{ fontSize: 15, color: TD.ink70, fontWeight: 500 }}>Masa 7</div>
      </div>

      <div style={{ position: 'relative', padding: '28px 24px 0' }}>
        <h1 style={{ fontFamily: TD.display, fontWeight: 700, fontSize: 46, lineHeight: 0.95, letterSpacing: '-0.03em', margin: '0 0 16px' }}>
          Ofertă<br/><span style={{ color: TD.accent }}>limitată</span><br/>de chef.
        </h1>
        <p style={{ fontSize: 18, lineHeight: 1.55, color: TD.ink70, margin: '0 0 22px' }}>
          Chef Andrei oferă o porție specială la preț redus. Disponibilă doar câteva minute.
        </p>

        <div style={{ background: TD.card, border: `1px solid ${TD.ink12}`, borderRadius: 24, padding: '16px 18px', marginBottom: 16, display: 'grid', gridTemplateColumns: '96px 1fr', gap: 16 }}>
          <DFoodTile label="SPECIAL" tone="pink" style={{ width: 96, height: 110, borderRadius: 16 }} />
          <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'space-between' }}>
            <div>
              <div style={{ display: 'inline-block', background: TD.lime, color: TD.paper, padding: '4px 12px', borderRadius: 999, fontSize: 14, fontWeight: 800, marginBottom: 8 }}>−43% reducere</div>
              <div style={{ fontSize: 20, fontWeight: 700, lineHeight: 1.2 }}>Tartar de vită cu trufe</div>
              <div style={{ fontSize: 14, color: TD.ink70, marginTop: 4 }}>Chef's cut · porție limitată</div>
            </div>
            <div style={{ display: 'flex', alignItems: 'baseline', gap: 12, marginTop: 10 }}>
              <span style={{ fontSize: 32, fontWeight: 800, color: TD.accent }}>48 <span style={{ fontSize: 16, color: TD.ink70, fontWeight: 600 }}>RON</span></span>
              <span style={{ fontSize: 16, color: TD.ink40, textDecoration: 'line-through' }}>84 RON</span>
            </div>
          </div>
        </div>

        <div style={{ background: TD.card, border: `1px solid ${TD.ink12}`, borderRadius: 22, padding: '16px 18px' }}>
          <div style={{ fontSize: 15, fontWeight: 600, color: TD.ink70, marginBottom: 8 }}>Timp rămas</div>
          <div style={{ display: 'flex', alignItems: 'baseline', gap: 4 }}>
            <span style={{ fontFamily: TD.mono, fontSize: 56, fontWeight: 600, letterSpacing: '-0.04em', lineHeight: 1 }}>03</span>
            <span style={{ fontFamily: TD.mono, fontSize: 56, color: TD.accent, lineHeight: 1 }}>:</span>
            <span style={{ fontFamily: TD.mono, fontSize: 56, fontWeight: 600, letterSpacing: '-0.04em', lineHeight: 1 }}>47</span>
            <span style={{ fontSize: 16, color: TD.ink70, marginLeft: 8, fontWeight: 500 }}>minute</span>
          </div>
          <div style={{ height: 8, background: TD.paperHi, marginTop: 14, borderRadius: 999, overflow: 'hidden' }}>
            <div style={{ width: '62%', height: '100%', background: `linear-gradient(90deg, ${TD.accent}, ${TD.lime})`, borderRadius: 999 }} />
          </div>
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 28, left: 18, right: 18, display: 'flex', flexDirection: 'column', gap: 10 }}>
        <ADBigBtn>
          <span>Adaugă în comandă</span>
          <ADArrow />
        </ADBigBtn>
        <div style={{ textAlign: 'center', fontSize: 15, color: TD.ink70, fontWeight: 500 }}>Nu mulțumesc</div>
      </div>
    </ADScreen>
  );
}

// ─────────────────────────────────────────────────────────────
// 6. ORDER SENT (accessible dark)
// ─────────────────────────────────────────────────────────────
function SentScreenA11yDark() {
  const steps = [
    { t: '19:42', label: 'Trimisă la bucătărie', done: true },
    { t: '19:44', label: 'În pregătire acum', done: true, active: true },
    { t: '~20:04', label: 'Servire la masă', done: false },
  ];
  return (
    <ADScreen>
      <div style={{ position: 'absolute', top: 0, right: -80, width: 280, height: 280, borderRadius: '50%', filter: 'blur(50px)', opacity: 0.28, background: `radial-gradient(circle, ${TD.lime} 0%, transparent 70%)` }} />

      <div style={{ position: 'relative', padding: '10px 24px 0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <div style={{ fontSize: 16, color: TD.ink70, fontWeight: 500 }}>Masa 7</div>
        <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6, background: 'rgba(76,175,80,0.14)', padding: '8px 14px', borderRadius: 999, fontSize: 16, fontWeight: 700, color: '#4CAF50' }}>Confirmat ✓</div>
      </div>

      <div style={{ position: 'relative', padding: '32px 24px 0' }}>
        <h1 style={{ fontFamily: TD.display, fontWeight: 700, fontSize: 48, lineHeight: 0.95, letterSpacing: '-0.03em', margin: '0 0 16px' }}>
          Comanda<br/><span style={{ color: TD.accent }}>a plecat</span><br/>la bucătărie!
        </h1>
        <p style={{ fontSize: 18, lineHeight: 1.55, color: TD.ink70, margin: '0 0 22px' }}>
          Chef Andrei a primit comanda dumneavoastră. Urmăriți statusul mai jos.
        </p>

        <div style={{ background: TD.card, border: `1px solid ${TD.ink12}`, borderRadius: 22, padding: '6px 18px', marginBottom: 16 }}>
          {steps.map((s, i) => (
            <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 16, padding: '18px 0', borderBottom: i < steps.length - 1 ? `1px solid ${TD.ink06}` : 'none' }}>
              <span style={{ width: 20, height: 20, borderRadius: '50%', flexShrink: 0, background: s.done ? (s.active ? TD.accent : TD.ink) : 'transparent', border: s.done ? 'none' : `2px solid ${TD.ink40}`, boxShadow: s.active ? `0 0 12px ${TD.accent}` : 'none' }} />
              <div style={{ flex: 1 }}>
                <div style={{ fontSize: 18, fontWeight: s.active ? 700 : 500, color: s.done ? TD.ink : TD.ink40, lineHeight: 1.2 }}>{s.label}</div>
                <div style={{ fontSize: 14, color: TD.ink40, marginTop: 2 }}>{s.t}</div>
              </div>
              {s.active && <div style={{ fontSize: 13, fontWeight: 700, color: TD.accent, background: `rgba(232,37,90,0.15)`, padding: '6px 12px', borderRadius: 999 }}>ACUM</div>}
            </div>
          ))}
        </div>

        <div style={{ background: TD.card, border: `1px solid ${TD.ink12}`, borderRadius: 22, padding: '18px 20px' }}>
          <div style={{ fontSize: 15, color: TD.ink40, marginBottom: 4, fontWeight: 500 }}>Total comandă</div>
          <div style={{ fontSize: 36, fontWeight: 800, letterSpacing: '-0.03em' }}>218 <span style={{ fontSize: 18, color: TD.ink70, fontWeight: 600 }}>RON</span></div>
          <div style={{ fontSize: 14, color: TD.ink40, marginTop: 8 }}>Plata la final · cash sau card</div>
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 28, left: 18, right: 18, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
        <button style={{ padding: '18px 16px', border: `1px solid ${TD.ink12}`, background: TD.card, color: TD.ink, borderRadius: 999, fontSize: 16, fontWeight: 700, cursor: 'pointer' }}>Chemați chelnerul</button>
        <ADBigBtn style={{ fontSize: 16 }}><span>Mai comand</span><ADArrow /></ADBigBtn>
      </div>
    </ADScreen>
  );
}

// ─────────────────────────────────────────────────────────────
// Export
// ─────────────────────────────────────────────────────────────
Object.assign(window, {
  WelcomeScreenA11yDark,
  PebblesScreenA11yDark,
  MenuScreenA11yDark,
  CartScreenA11yDark,
  FlashDealScreenA11yDark,
  SentScreenA11yDark,
});

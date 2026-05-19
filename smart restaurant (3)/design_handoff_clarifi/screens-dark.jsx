// Dark theme screens — inspired by Supporter Sports Pub & Grill.
// Charcoal ground · hot-pink accent · warm-gold pop.

const TD = {
  paper:    '#1B1814',
  paperHi:  '#221F1A',
  card:     '#2C2923',
  ink:      '#F5F0E8',
  ink70:    'rgba(245,240,232,0.65)',
  ink40:    'rgba(245,240,232,0.38)',
  ink12:    'rgba(245,240,232,0.10)',
  ink06:    'rgba(245,240,232,0.05)',
  accent:   '#E8255A',
  accentDk: '#B81A47',
  lime:     '#FFD166',
  blue:     '#6B9FFF',
  sans:     '"Inter Tight", "Geist", -apple-system, system-ui, sans-serif',
  display:  '"Inter Tight", "Geist", -apple-system, system-ui, sans-serif',
  mono:     '"JetBrains Mono", ui-monospace, Menlo, monospace',
  STATUS_CLEAR: 58,
};

// ─── Atoms ──────────────────────────────────────────────────
function DScreen({ children, bg = TD.paper, style = {} }) {
  return (
    <div style={{
      width: '100%', height: '100%', background: bg, color: TD.ink,
      fontFamily: TD.sans, position: 'relative', overflow: 'hidden',
      paddingTop: TD.STATUS_CLEAR, ...style,
    }}>
      <div style={{
        position: 'absolute', inset: 0, pointerEvents: 'none', opacity: 0.22, mixBlendMode: 'screen',
        backgroundImage: `url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='1.1' numOctaves='2' stitchTiles='stitch'/><feColorMatrix values='0 0 0 0 0.9  0 0 0 0 0.88  0 0 0 0 0.85  0 0 0 0.06 0'/></filter><rect width='100%25' height='100%25' filter='url(%23n)'/></svg>")`,
      }} />
      {children}
    </div>
  );
}

function DTag({ children, color = TD.ink, bg = 'transparent', style = {} }) {
  return (
    <span style={{
      display: 'inline-flex', alignItems: 'center', gap: 6,
      fontFamily: TD.mono, fontSize: 10, fontWeight: 500,
      letterSpacing: '0.14em', textTransform: 'uppercase',
      color, background: bg,
      border: `1px solid ${color === TD.ink ? TD.ink12 : color + '50'}`,
      padding: '5px 9px', borderRadius: 999, lineHeight: 1,
      ...style,
    }}>{children}</span>
  );
}

function DDot({ color = TD.accent, size = 7, glow = false, style = {} }) {
  return (
    <span style={{
      display: 'inline-block', width: size, height: size, borderRadius: '50%',
      background: color, boxShadow: glow ? `0 0 10px ${color}` : 'none',
      ...style,
    }} />
  );
}

function DFoodTile({ label, tone = 'pink', style = {} }) {
  const palettes = {
    pink:  { a: '#E8255A', b: '#FF7CA0', c: '#3A1520' },
    gold:  { a: '#FFD166', b: '#FFE9A3', c: '#2E2510' },
    sage:  { a: '#5E9A7A', b: '#9ECFB4', c: '#16271E' },
    ink:   { a: '#6B9FFF', b: '#A3C0FF', c: '#111B30' },
    terra: { a: '#E85A25', b: '#FF9D77', c: '#2E1510' },
    lime:  { a: '#C4D41A', b: '#E6F54A', c: '#1D2206' },
  };
  const p = palettes[tone] || palettes.pink;
  return (
    <div style={{
      position: 'relative', overflow: 'hidden', borderRadius: 14, background: p.c, ...style,
    }}>
      <div style={{
        position: 'absolute', inset: '-20%',
        background: `radial-gradient(60% 50% at 30% 30%, ${p.a} 0%, transparent 60%),
                     radial-gradient(50% 40% at 75% 75%, ${p.b} 0%, transparent 60%)`,
        filter: 'blur(2px)',
      }} />
      <div style={{
        position: 'absolute', left: 10, top: 10,
        fontFamily: TD.mono, fontSize: 9, letterSpacing: '0.12em',
        color: 'rgba(245,240,232,0.45)', textTransform: 'uppercase',
      }}>{label}</div>
    </div>
  );
}

// ─── Data ────────────────────────────────────────────────────
const dCategories = [
  { id: 'start',  name: 'Începuturi',      count: 8,  tone: 'gold' },
  { id: 'main',   name: 'Principale',      count: 14, tone: 'pink' },
  { id: 'paste',  name: 'Paste & Risotto', count: 9,  tone: 'sage' },
  { id: 'desert', name: 'Desert',          count: 6,  tone: 'ink' },
  { id: 'bautur', name: 'Băuturi',         count: 22, tone: 'lime' },
  { id: 'vin',    name: 'Vinuri',          count: 18, tone: 'terra' },
];

const dMains = [
  { id: 'd1', name: 'Mușchi de vită Oltenia', desc: 'Maturat 28 zile · piure trufat · jus de vin roșu', price: 128, tone: 'pink', tag: 'Semnătura' },
  { id: 'd2', name: 'Somn sălbatic la grătar', desc: 'Delta Dunării · unt de lămâie-capere · mazăre', price: 96, tone: 'sage' },
  { id: 'd3', name: 'Mămăligă cu brânză de burduf', desc: 'Mărginimea Sibiului · coajă de brad · smântână', price: 54, tone: 'gold', tag: 'Vegetarian' },
  { id: 'd4', name: 'Ciolan de porc la cuptor', desc: 'Marinat 24h · varză călită · muștar cu miere', price: 72, tone: 'terra' },
];

const dCartItems = [
  { id: 1, name: 'Mușchi de vită Oltenia', qty: 1, price: 128, tone: 'pink' },
  { id: 2, name: 'Mămăligă cu brânză',     qty: 2, price: 54,  tone: 'gold' },
  { id: 3, name: 'Fetească Neagră · pahar', qty: 1, price: 42, tone: 'ink', note: 'Sugerat de AI' },
];

// ─── 1. Welcome ──────────────────────────────────────────────
function WelcomeScreenDark() {
  return (
    <DScreen>
      <div style={{ position: 'absolute', top: -60, right: -80, width: 320, height: 320, borderRadius: '50%', filter: 'blur(50px)', opacity: 0.5, background: `radial-gradient(circle, ${TD.accent} 0%, transparent 70%)` }} />
      <div style={{ position: 'absolute', top: 200, left: -100, width: 280, height: 280, borderRadius: '50%', filter: 'blur(60px)', opacity: 0.3, background: `radial-gradient(circle, ${TD.lime} 0%, transparent 70%)` }} />

      <div style={{ position: 'relative', padding: '14px 22px 0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <DTag><DDot color={TD.accent} glow />Masa 07 · live</DTag>
        <DTag>22 · 04 · 19:42</DTag>
      </div>

      <div style={{ position: 'relative', padding: '58px 22px 0' }}>
        <div style={{ fontFamily: TD.mono, fontSize: 11, letterSpacing: '0.18em', color: TD.accent, textTransform: 'uppercase', marginBottom: 18 }}>
          Bun venit —
        </div>
        <h1 style={{ fontFamily: TD.display, fontWeight: 500, fontSize: 68, lineHeight: 0.9, letterSpacing: '-0.045em', margin: 0, color: TD.ink }}>
          Nume<br/><span style={{ color: TD.accent }}>Restaurant.</span>
        </h1>
        <p style={{ fontSize: 15, lineHeight: 1.45, color: TD.ink70, maxWidth: 310, margin: '26px 0 34px' }}>
          Meniul de seară, live pentru masa 07. Comandați direct — bucătăria pregătește când trimiteți.
        </p>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginBottom: 28 }}>
          <DTag>07 · terasă</DTag>
          <DTag>2 oaspeți</DTag>
          <DTag>chef · Andrei C.</DTag>
          <DTag>#4821</DTag>
        </div>

        <div style={{ position: 'relative', background: TD.card, color: TD.ink, borderRadius: 22, padding: 18, overflow: 'hidden', display: 'flex', gap: 14, alignItems: 'center', boxShadow: '0 20px 40px -20px rgba(0,0,0,0.6)' }}>
          <div style={{ position: 'absolute', top: -40, right: -40, width: 180, height: 180, borderRadius: '50%', background: TD.accent, filter: 'blur(32px)', opacity: 0.35 }} />
          <DFoodTile label="TODAY" tone="pink" style={{ width: 72, height: 90, flexShrink: 0, borderRadius: 14, zIndex: 1 }} />
          <div style={{ position: 'relative', zIndex: 1, flex: 1 }}>
            <div style={{ fontFamily: TD.mono, fontSize: 9, letterSpacing: '0.2em', color: TD.lime, marginBottom: 6 }}>ASTĂZI · CHEF'S PICK</div>
            <div style={{ fontSize: 19, fontWeight: 500, letterSpacing: '-0.02em', lineHeight: 1.1, marginBottom: 4 }}>Mușchi de vită Oltenia</div>
            <div style={{ fontSize: 12, color: TD.ink70 }}>Maturat 28 zile · 128 RON</div>
          </div>
          <span style={{ fontSize: 22, opacity: 0.7, zIndex: 1 }}>→</span>
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 38, left: 22, right: 22 }}>
        <button style={{ width: '100%', padding: '19px 22px', border: 'none', background: TD.accent, color: TD.ink, fontFamily: TD.sans, fontSize: 15, fontWeight: 600, letterSpacing: '-0.01em', borderRadius: 999, cursor: 'pointer', display: 'flex', justifyContent: 'space-between', alignItems: 'center', boxShadow: `0 12px 28px -8px ${TD.accent}88` }}>
          <span>Deschide meniul</span>
          <span style={{ width: 30, height: 30, borderRadius: '50%', background: TD.ink, color: TD.accent, display: 'grid', placeItems: 'center', fontSize: 16 }}>→</span>
        </button>
      </div>
    </DScreen>
  );
}

// ─── 2. Categories ───────────────────────────────────────────
function CategoryScreenDark() {
  return (
    <DScreen>
      <div style={{ padding: '14px 22px 0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <DTag><DDot color={TD.accent} glow />Masa 07</DTag>
        <DTag>Ro / En</DTag>
      </div>

      <div style={{ padding: '22px 22px 20px' }}>
        <div style={{ fontFamily: TD.mono, fontSize: 11, letterSpacing: '0.18em', color: TD.accent, textTransform: 'uppercase', marginBottom: 10 }}>01 · Capitole</div>
        <h1 style={{ fontFamily: TD.display, fontWeight: 500, fontSize: 40, lineHeight: 0.95, letterSpacing: '-0.03em', margin: '0 0 8px', color: TD.ink }}>
          Ce aveți poftă<br/><span style={{ color: TD.accent }}>să gustați?</span>
        </h1>
        <p style={{ fontSize: 13, color: TD.ink70, margin: 0 }}>77 preparate · bucătărie românească contemporană</p>
      </div>

      <div style={{ padding: '0 22px 18px' }}>
        <div style={{ background: TD.card, border: `1px solid ${TD.ink12}`, borderRadius: 999, padding: '12px 16px', display: 'flex', alignItems: 'center', gap: 10, fontSize: 13, color: TD.ink40, boxShadow: '0 2px 8px -4px rgba(0,0,0,0.3)' }}>
          <span style={{ fontSize: 14 }}>⌕</span>
          <span>Caută preparate, ingrediente…</span>
          <span style={{ marginLeft: 'auto', fontFamily: TD.mono, fontSize: 10, letterSpacing: '0.12em', color: TD.accent }}>AI</span>
        </div>
      </div>

      <div style={{ padding: '0 22px', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
        {dCategories.map((c, i) => (
          <button key={c.id} style={{ position: 'relative', textAlign: 'left', cursor: 'pointer', background: TD.card, border: `1px solid ${TD.ink12}`, borderRadius: 18, padding: 14, height: 104, display: 'flex', flexDirection: 'column', justifyContent: 'space-between', overflow: 'hidden' }}>
            <DFoodTile label="" tone={c.tone} style={{ position: 'absolute', right: -20, top: -20, width: 80, height: 80, borderRadius: '50%', opacity: 0.7 }} />
            <div style={{ fontFamily: TD.mono, fontSize: 10, color: TD.ink40, letterSpacing: '0.14em', position: 'relative' }}>{String(i + 1).padStart(2, '0')}</div>
            <div style={{ position: 'relative' }}>
              <div style={{ fontSize: 17, fontWeight: 500, letterSpacing: '-0.02em', lineHeight: 1.1 }}>{c.name}</div>
              <div style={{ fontSize: 11, color: TD.ink70, marginTop: 4, fontFamily: TD.mono, letterSpacing: '0.08em' }}>{c.count} items</div>
            </div>
          </button>
        ))}
      </div>

      <div style={{ position: 'absolute', bottom: 34, left: 16, right: 16, background: TD.ink, color: TD.paper, padding: '12px 14px 12px 18px', borderRadius: 999, display: 'flex', alignItems: 'center', justifyContent: 'space-between', boxShadow: '0 20px 40px -16px rgba(0,0,0,0.6)' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <div style={{ background: TD.lime, color: TD.paper, borderRadius: 999, padding: '4px 10px', fontFamily: TD.mono, fontSize: 10, fontWeight: 600, letterSpacing: '0.1em' }}>3</div>
          <div>
            <div style={{ fontFamily: TD.mono, fontSize: 9, letterSpacing: '0.14em', color: 'rgba(27,24,20,0.55)' }}>ÎN COȘ</div>
            <div style={{ fontSize: 17, fontWeight: 500, letterSpacing: '-0.02em' }}>218 RON</div>
          </div>
        </div>
        <div style={{ background: TD.accent, color: TD.ink, borderRadius: 999, padding: '12px 18px', fontSize: 13, fontWeight: 600 }}>Vezi comanda →</div>
      </div>
    </DScreen>
  );
}

// ─── 2b. Pebbles ─────────────────────────────────────────────
function PebblesScreenDark() {
  const [active, setActive] = React.useState(null);
  const PEB = [
    { id: 'principale', label: 'Feluri\nprincipale', count: 14, path: 0, img: 'https://images.unsplash.com/photo-1544025162-d76694265947?w=500&q=80' },
    { id: 'salate',     label: 'Salate',             count: 6,  path: 1, img: 'https://images.unsplash.com/photo-1512621776951-a57141f2eefd?w=500&q=80' },
    { id: 'supe',       label: 'Supe &\nciorbe',     count: 8,  path: 2, img: 'https://images.unsplash.com/photo-1547592180-85f173990554?w=500&q=80' },
    { id: 'bauturi',    label: 'Băuturi',            count: 22, path: 3, img: 'https://images.unsplash.com/photo-1551024709-8f23befc6f87?w=500&q=80' },
    { id: 'deserturi',  label: 'Deserturi',          count: 6,  path: 4, img: 'https://images.unsplash.com/photo-1488477181946-6428a0291777?w=500&q=80' },
    { id: 'vinuri',     label: 'Vinuri',             count: 18, path: 5, img: 'https://images.unsplash.com/photo-1510812431401-41d2bd2722f3?w=500&q=80' },
  ];
  const activePeb = PEB.find(p => p.id === active) || PEB[0];

  return (
    <DScreen>
      <div style={{ position: 'relative', padding: '14px 22px 0', display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexShrink: 0 }}>
        <button style={{ width: 36, height: 36, borderRadius: '50%', background: TD.card, border: `1px solid ${TD.ink12}`, display: 'grid', placeItems: 'center', color: TD.ink, fontSize: 15, cursor: 'pointer', fontFamily: TD.sans }}>←</button>
        <DTag>Masa 07</DTag>
      </div>

      <div style={{ position: 'relative', padding: '26px 22px 4px', flexShrink: 0 }}>
        <div style={{ fontFamily: TD.mono, fontSize: 10, letterSpacing: '0.18em', color: TD.accent, textTransform: 'uppercase', marginBottom: 10 }}>Alegeți categoria —</div>
        <h2 style={{ fontFamily: TD.display, fontWeight: 500, fontSize: 40, lineHeight: 0.92, letterSpacing: '-0.04em', margin: 0, color: TD.ink }}>
          Ce aveți<br/><span style={{ color: TD.accent }}>poftă să</span><br/>mâncați?
        </h2>
      </div>

      <div style={{ position: 'relative', flex: 1, overflow: 'hidden auto', marginTop: 8, marginLeft: 14, marginRight: 14, padding: '0 0 90px' }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 29 }}>
          {[0,1,2].map(group => (
            <div key={group} style={{ background: TD.card, border: 'none', borderRadius: 22, padding: '16px 14px 20px' }}>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0 10px', alignItems: 'start' }}>
                {PEB.slice(group*2, group*2+2).map((pb, i) => {
                  const isA = pb.id === active;
                  const path = window.BLOB_PATHS ? window.BLOB_PATHS[pb.path] : BLOB_PATHS[pb.path];
                  const cid = `dpc-${pb.id}`;
                  const hid = `dph-${pb.id}`;
                  return (
                    <div key={pb.id} onClick={() => setActive(isA ? null : pb.id)} style={{ marginTop: i%2===1?22:0, cursor: 'pointer', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 10 }}>
                      <div style={{ transform: `scale(${isA?1.06:1}) translateY(${isA?-4:0}px)`, transition: 'transform 0.5s cubic-bezier(0.34,1.4,0.64,1)', filter: isA ? `drop-shadow(0 18px 22px ${TD.accent}55)` : 'drop-shadow(0 10px 16px rgba(0,0,0,0.4))' }}>
                        <svg viewBox="0 0 100 100" width="148" height="148" style={{ display: 'block', overflow: 'visible' }}>
                          <defs>
                            <clipPath id={cid}><path d={path} /></clipPath>
                            <radialGradient id={hid} cx="32%" cy="25%" r="52%">
                              <stop offset="0%" stopColor="rgba(255,255,255,0.6)" />
                              <stop offset="100%" stopColor="rgba(255,255,255,0)" />
                            </radialGradient>
                            <linearGradient id={`dpsh-${pb.id}`} x1="50%" y1="0%" x2="50%" y2="100%">
                              <stop offset="0%" stopColor="rgba(255,255,255,0.05)" />
                              <stop offset="100%" stopColor="rgba(0,0,0,0.45)" />
                            </linearGradient>
                          </defs>
                          <image href={pb.img} x="0" y="0" width="100" height="100" preserveAspectRatio="xMidYMid slice" clipPath={`url(#${cid})`} />
                          <path d={path} fill={`url(#dpsh-${pb.id})`} opacity={isA?0.4:0.65} />
                          <path d={path} fill={`url(#${hid})`} />
                          <path d={path} fill="none" stroke={isA ? TD.accent : 'rgba(245,240,232,0.12)'} strokeWidth={isA?1.6:0.5} />
                          {isA && <path d={path} fill="none" stroke="rgba(245,240,232,0.25)" strokeWidth="2.8" />}
                        </svg>
                      </div>
                      <div style={{ textAlign: 'center' }}>
                        <div style={{ fontWeight: 700, fontSize: 13, letterSpacing: '-0.01em', color: isA ? TD.accent : TD.ink, lineHeight: 1.15, whiteSpace: 'pre-line', transition: 'color 0.3s' }}>{pb.label}</div>
                        <div style={{ fontFamily: TD.mono, fontSize: 9, letterSpacing: '0.18em', color: TD.ink40, marginTop: 3 }}>{String(pb.count).padStart(2,'0')} items</div>
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          ))}
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 0, left: 0, right: 0, padding: '10px 22px 28px', transform: active ? 'translateY(0)' : 'translateY(110%)', transition: 'transform 0.4s cubic-bezier(0.32,0.72,0,1)', zIndex: 10 }}>
        <button style={{ width: '100%', padding: '16px 20px', border: 'none', background: TD.accent, color: TD.ink, fontFamily: TD.sans, fontSize: 15, fontWeight: 600, letterSpacing: '-0.01em', borderRadius: 999, cursor: 'pointer', display: 'flex', justifyContent: 'space-between', alignItems: 'center', boxShadow: `0 12px 28px -8px ${TD.accent}88` }}>
          <span>{active ? PEB.find(p=>p.id===active)?.label.replace('\n',' ') : ''}</span>
          <span style={{ width: 30, height: 30, borderRadius: '50%', background: TD.ink, color: TD.accent, display: 'grid', placeItems: 'center', fontSize: 16, flexShrink: 0 }}>→</span>
        </button>
      </div>
    </DScreen>
  );
}

// ─── 3. Menu ─────────────────────────────────────────────────
function MenuScreenDark() {
  return (
    <DScreen>
      <div style={{ padding: '10px 22px 14px', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <div style={{ width: 38, height: 38, borderRadius: '50%', background: TD.card, border: `1px solid ${TD.ink12}`, display: 'grid', placeItems: 'center', fontSize: 16 }}>←</div>
        <div style={{ textAlign: 'center' }}>
          <div style={{ fontFamily: TD.mono, fontSize: 9, letterSpacing: '0.18em', color: TD.ink40 }}>02 · 06</div>
          <div style={{ fontSize: 16, fontWeight: 500, letterSpacing: '-0.02em', marginTop: 1 }}>Principale</div>
        </div>
        <div style={{ width: 38, height: 38, borderRadius: '50%', background: TD.card, border: `1px solid ${TD.ink12}`, display: 'grid', placeItems: 'center', fontSize: 14, color: TD.ink70 }}>⌕</div>
      </div>

      <div style={{ display: 'flex', gap: 6, padding: '0 22px 14px', overflowX: 'auto', whiteSpace: 'nowrap' }}>
        {['Toate','Carne','Pește','Veggie','Chef'].map((t, i) => (
          <span key={t} style={{ fontSize: 12, fontWeight: 500, padding: '8px 14px', borderRadius: 999, background: i===0 ? TD.accent : TD.card, color: i===0 ? TD.ink : TD.ink70, border: i===0 ? 'none' : `1px solid ${TD.ink12}` }}>{t}</span>
        ))}
      </div>

      <div style={{ padding: '0 16px 18px', display: 'flex', flexDirection: 'column', gap: 10 }}>
        {dMains.map(m => (
          <article key={m.id} style={{ background: TD.card, borderRadius: 20, padding: 12, display: 'grid', gridTemplateColumns: '96px 1fr', gap: 14, border: `1px solid ${TD.ink12}` }}>
            <DFoodTile label={m.id.toUpperCase()} tone={m.tone} style={{ width: 96, height: 110 }} />
            <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'space-between', padding: '2px 0' }}>
              <div>
                {m.tag && <DTag color={TD.accent} style={{ marginBottom: 6 }}><DDot color={TD.accent} size={5} />{m.tag}</DTag>}
                <div style={{ fontSize: 16, fontWeight: 500, letterSpacing: '-0.02em', lineHeight: 1.15, marginBottom: 4 }}>{m.name}</div>
                <div style={{ fontSize: 11.5, color: TD.ink70, lineHeight: 1.4 }}>{m.desc}</div>
              </div>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginTop: 8 }}>
                <div style={{ fontSize: 18, fontWeight: 600, letterSpacing: '-0.02em' }}>{m.price}<span style={{ fontSize: 10, color: TD.ink70, marginLeft: 3, fontWeight: 500 }}>RON</span></div>
                <button style={{ background: TD.accent, color: TD.ink, border: 'none', width: 34, height: 34, borderRadius: '50%', fontSize: 18, cursor: 'pointer', display: 'grid', placeItems: 'center' }}>+</button>
              </div>
            </div>
          </article>
        ))}
      </div>

      <div style={{ padding: '18px 16px 120px', background: TD.paperHi, borderTop: `1px solid ${TD.ink12}` }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '0 6px', marginBottom: 4 }}>
          <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6, background: TD.lime, color: TD.paper, borderRadius: 999, padding: '5px 10px', fontFamily: TD.mono, fontSize: 10, fontWeight: 600, letterSpacing: '0.12em' }}>
            <DDot color={TD.paper} size={5} />AI · LIVE
          </div>
          <span style={{ fontSize: 11, color: TD.ink40, fontFamily: TD.mono, letterSpacing: '0.1em' }}>2s ago</span>
        </div>
        <div style={{ padding: '0 6px' }}>
          <h4 style={{ fontSize: 22, fontWeight: 500, letterSpacing: '-0.025em', margin: '8px 0 4px' }}>Se potrivesc cu<br/>ce ai ales.</h4>
          <p style={{ fontSize: 12, color: TD.ink70, margin: '0 0 14px' }}>Bazat pe mușchiul de vită din coș</p>
        </div>
        <div style={{ display: 'flex', gap: 10, overflowX: 'auto', padding: '0 6px 4px' }}>
          {[
            { name: 'Fetească Neagră', sub: 'pahar', price: 42, tone: 'ink' },
            { name: 'Unt cu trufe', sub: 'Appetizer', price: 18, tone: 'gold' },
            { name: 'Salată verde', sub: 'Contorno', price: 28, tone: 'sage' },
          ].map((r, i) => (
            <div key={i} style={{ minWidth: 190, background: TD.card, border: `1px solid ${TD.ink12}`, borderRadius: 18, padding: 12, display: 'flex', flexDirection: 'column' }}>
              <DFoodTile label={`PAIR ${i+1}`} tone={r.tone} style={{ height: 76, marginBottom: 10 }} />
              <div style={{ fontFamily: TD.mono, fontSize: 9, letterSpacing: '0.14em', color: TD.ink40, marginBottom: 2 }}>PAIRING</div>
              <div style={{ fontSize: 15, fontWeight: 500, letterSpacing: '-0.01em', lineHeight: 1.15 }}>{r.name}</div>
              <div style={{ fontSize: 11, color: TD.ink40, flex: 1, margin: '6px 0 10px' }}>{r.sub}</div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <span style={{ fontSize: 14, fontWeight: 600 }}>{r.price}<span style={{ fontSize: 10, color: TD.ink70, marginLeft: 2 }}>RON</span></span>
                <button style={{ background: TD.accent, color: TD.ink, border: 'none', borderRadius: 999, padding: '6px 12px', fontSize: 11, fontWeight: 600, cursor: 'pointer' }}>+ Adaugă</button>
              </div>
            </div>
          ))}
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 24, left: 14, right: 14, background: TD.ink, color: TD.paper, padding: '12px 14px 12px 18px', borderRadius: 999, display: 'flex', alignItems: 'center', justifyContent: 'space-between', boxShadow: '0 20px 40px -16px rgba(0,0,0,0.6)' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <div style={{ background: TD.lime, color: TD.paper, borderRadius: 999, padding: '4px 10px', fontFamily: TD.mono, fontSize: 10, fontWeight: 600 }}>3</div>
          <div>
            <div style={{ fontFamily: TD.mono, fontSize: 9, letterSpacing: '0.14em', color: 'rgba(27,24,20,0.55)' }}>ÎN COȘ</div>
            <div style={{ fontSize: 17, fontWeight: 500, letterSpacing: '-0.02em' }}>218 RON</div>
          </div>
        </div>
        <div style={{ background: TD.accent, color: TD.ink, borderRadius: 999, padding: '11px 16px', fontSize: 13, fontWeight: 600 }}>Vezi comanda →</div>
      </div>
    </DScreen>
  );
}

// ─── 4. Cart ─────────────────────────────────────────────────
function CartScreenDark() {
  const subtotal = dCartItems.reduce((s, i) => s + i.price * i.qty, 0);
  return (
    <DScreen>
      <div style={{ position: 'absolute', top: -50, right: -60, width: 240, height: 240, borderRadius: '50%', filter: 'blur(45px)', opacity: 0.28, background: `radial-gradient(circle, ${TD.accent} 0%, transparent 70%)`, pointerEvents: 'none' }} />
      <div style={{ position: 'absolute', bottom: 140, left: -50, width: 200, height: 200, borderRadius: '50%', filter: 'blur(40px)', opacity: 0.22, background: `radial-gradient(circle, ${TD.lime} 0%, transparent 70%)`, pointerEvents: 'none' }} />
      <div style={{ padding: '10px 22px 14px', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <DTag><DDot color={TD.accent} glow />Masa 07</DTag>
        <DTag>Draft #4821</DTag>
        <div style={{ width: 32, height: 32, borderRadius: '50%', background: TD.card, border: `1px solid ${TD.ink12}`, display: 'grid', placeItems: 'center', fontSize: 14 }}>✕</div>
      </div>

      <div style={{ padding: '22px 22px 18px' }}>
        <div style={{ fontFamily: TD.mono, fontSize: 11, letterSpacing: '0.18em', color: TD.accent, textTransform: 'uppercase', marginBottom: 10 }}>Comanda ta</div>
        <h1 style={{ fontFamily: TD.display, fontWeight: 500, fontSize: 42, lineHeight: 0.92, letterSpacing: '-0.03em', margin: '0 0 8px', color: TD.ink }}>
          Gata de<br/><span style={{ color: TD.accent }}>trimis</span>.
        </h1>
        <p style={{ fontSize: 13, color: TD.ink70, margin: 0 }}>3 preparate · ~22 min în pregătire</p>
      </div>

      <div style={{ padding: '0 16px', display: 'flex', flexDirection: 'column', gap: 8 }}>
        {dCartItems.map(it => (
          <div key={it.id} style={{ background: TD.card, border: `1px solid ${TD.ink12}`, borderRadius: 18, padding: 10, display: 'grid', gridTemplateColumns: '60px 1fr auto', gap: 12, alignItems: 'center' }}>
            <DFoodTile label="" tone={it.tone} style={{ width: 60, height: 60, borderRadius: 14 }} />
            <div>
              <div style={{ fontSize: 14.5, fontWeight: 500, letterSpacing: '-0.01em', lineHeight: 1.2 }}>{it.name}</div>
              {it.note
                ? <div style={{ display: 'inline-flex', alignItems: 'center', gap: 4, marginTop: 4, fontSize: 10, color: TD.paper, background: TD.lime, padding: '3px 7px', borderRadius: 999, fontFamily: TD.mono, letterSpacing: '0.1em', fontWeight: 600 }}><DDot color={TD.paper} size={4} />{it.note.toUpperCase()}</div>
                : <div style={{ fontSize: 11, color: TD.ink40, marginTop: 4, fontFamily: TD.mono, letterSpacing: '0.1em' }}>{it.price} × {it.qty}</div>
              }
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 6 }}>
              <div style={{ fontSize: 15, fontWeight: 600 }}>{it.price * it.qty}<span style={{ fontSize: 9, color: TD.ink70, marginLeft: 2 }}>RON</span></div>
              <div style={{ display: 'inline-flex', alignItems: 'center', background: TD.paperHi, borderRadius: 999, padding: 2, gap: 2 }}>
                <span style={{ width: 22, height: 22, display: 'grid', placeItems: 'center', fontSize: 14, color: TD.ink70, cursor: 'pointer' }}>−</span>
                <span style={{ width: 18, textAlign: 'center', fontFamily: TD.mono, fontSize: 11, fontWeight: 600 }}>{it.qty}</span>
                <span style={{ width: 22, height: 22, display: 'grid', placeItems: 'center', fontSize: 14, color: TD.accent, cursor: 'pointer' }}>+</span>
              </div>
            </div>
          </div>
        ))}
      </div>

      <div style={{ padding: '18px 22px 0' }}>
        <div style={{ background: TD.card, border: `1px solid ${TD.ink12}`, borderRadius: 18, padding: '14px 16px' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, color: TD.ink70, marginBottom: 6 }}><span>Subtotal</span><span>{subtotal} RON</span></div>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, color: TD.ink70, marginBottom: 10 }}><span>Servire</span><span>inclus</span></div>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', borderTop: `1px solid ${TD.ink12}`, paddingTop: 10 }}>
            <span style={{ fontSize: 16, fontWeight: 500 }}>Total</span>
            <span style={{ fontSize: 26, fontWeight: 600, letterSpacing: '-0.03em' }}>{subtotal}<span style={{ fontSize: 12, color: TD.ink70, marginLeft: 3 }}>RON</span></span>
          </div>
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 30, left: 16, right: 16 }}>
        <button style={{ width: '100%', padding: '18px 22px', border: 'none', background: TD.accent, color: TD.ink, fontFamily: TD.sans, fontSize: 15, fontWeight: 600, letterSpacing: '-0.01em', borderRadius: 999, cursor: 'pointer', display: 'flex', justifyContent: 'space-between', alignItems: 'center', boxShadow: `0 14px 28px -8px ${TD.accent}88` }}>
          <span>Trimite la bucătărie</span>
          <span style={{ width: 30, height: 30, borderRadius: '50%', background: TD.ink, color: TD.accent, display: 'grid', placeItems: 'center', fontSize: 16 }}>→</span>
        </button>
      </div>
    </DScreen>
  );
}

// ─── 5. Flash Deal ───────────────────────────────────────────
function FlashDealScreenDark() {
  return (
    <DScreen>
      <div style={{ position: 'absolute', top: -80, right: -90, width: 360, height: 360, borderRadius: '50%', filter: 'blur(55px)', opacity: 0.55, background: `radial-gradient(circle, ${TD.lime} 0%, transparent 70%)` }} />
      <div style={{ position: 'absolute', top: 320, left: -120, width: 300, height: 300, borderRadius: '50%', filter: 'blur(60px)', opacity: 0.28, background: `radial-gradient(circle, #C8A030 0%, transparent 70%)` }} />

      <div style={{ position: 'relative', padding: '10px 22px 0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <DTag color={TD.lime} style={{ borderColor: `${TD.lime}55` }}>Live · chef drop</DTag>
        <DTag>Masa 07 · 03:47</DTag>
      </div>

      <div style={{ position: 'relative', padding: '24px 22px 0' }}>
        <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontFamily: TD.mono, fontSize: 11, letterSpacing: '0.2em', color: TD.lime, marginBottom: 12 }}>
          <span style={{ width: 18, height: 18, borderRadius: '50%', background: TD.lime, color: TD.paper, display: 'grid', placeItems: 'center', fontSize: 10 }}>⚡</span>
          FLASH DEAL
        </div>
        <h1 style={{ fontFamily: TD.display, fontWeight: 500, fontSize: 48, lineHeight: 0.9, letterSpacing: '-0.035em', margin: 0, color: TD.ink }}>
          Doar pentru<br/><span style={{ color: TD.paper, background: TD.lime, borderRadius: 8, padding: '0 10px 2px', display: 'inline-block' }}>următoarele</span><br/>4 minute.
        </h1>
        <p style={{ fontSize: 13.5, lineHeight: 1.45, color: TD.ink70, maxWidth: 300, margin: '18px 0 20px' }}>
          Chef-ul Andrei are cantitate limitată. Doar <strong style={{ color: TD.ink, fontWeight: 600 }}>3 mese</strong> primesc oferta chiar acum.
        </p>

        <div style={{ position: 'relative', overflow: 'hidden', background: TD.card, border: `1px solid ${TD.ink12}`, borderRadius: 22, padding: 14, marginBottom: 18, display: 'grid', gridTemplateColumns: '88px 1fr', gap: 14, boxShadow: '0 18px 40px -18px rgba(0,0,0,0.5)' }}>
          <DFoodTile label="FLASH" tone="pink" style={{ width: 88, height: 104 }} />
          <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'space-between' }}>
            <div>
              <div style={{ display: 'inline-flex', alignItems: 'center', gap: 4, background: TD.lime, color: TD.paper, borderRadius: 999, padding: '3px 9px', fontFamily: TD.mono, fontSize: 10, fontWeight: 700, letterSpacing: '0.12em', marginBottom: 6 }}>−43%</div>
              <div style={{ fontSize: 17, fontWeight: 500, letterSpacing: '-0.02em', lineHeight: 1.15 }}>Tartar de vită cu trufe</div>
              <div style={{ fontSize: 11, color: TD.ink40, marginTop: 2 }}>Chef's cut · porție limitată</div>
            </div>
            <div style={{ display: 'flex', alignItems: 'baseline', gap: 10, marginTop: 8 }}>
              <span style={{ fontSize: 28, fontWeight: 700, letterSpacing: '-0.03em', color: TD.lime }}>48<span style={{ fontSize: 11, marginLeft: 2, color: TD.ink70, fontWeight: 500 }}>RON</span></span>
              <span style={{ fontSize: 12, color: TD.ink40, textDecoration: 'line-through' }}>84 RON</span>
            </div>
          </div>
          <div style={{ position: 'absolute', top: 10, right: -34, background: TD.lime, color: TD.paper, fontFamily: TD.mono, fontSize: 9, fontWeight: 700, letterSpacing: '0.2em', padding: '3px 40px', transform: 'rotate(35deg)' }}>HOT</div>
        </div>

        <div style={{ background: TD.card, border: `1px solid ${TD.ink12}`, borderRadius: 20, padding: 14, marginBottom: 12 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 6 }}>
            <span style={{ fontFamily: TD.mono, fontSize: 10, letterSpacing: '0.16em', color: TD.ink70 }}>EXPIRĂ ÎN</span>
            <span style={{ fontFamily: TD.mono, fontSize: 10, letterSpacing: '0.16em', color: TD.lime }}>62% RĂMAS</span>
          </div>
          <div style={{ display: 'flex', alignItems: 'baseline', gap: 2 }}>
            <span style={{ fontFamily: TD.mono, fontSize: 48, fontWeight: 500, letterSpacing: '-0.04em', lineHeight: 1 }}>03</span>
            <span style={{ fontFamily: TD.mono, fontSize: 48, fontWeight: 500, color: TD.lime, lineHeight: 1 }}>:</span>
            <span style={{ fontFamily: TD.mono, fontSize: 48, fontWeight: 500, letterSpacing: '-0.04em', lineHeight: 1 }}>47</span>
            <span style={{ fontFamily: TD.mono, fontSize: 10, color: TD.ink40, marginLeft: 'auto', letterSpacing: '0.14em' }}>MIN : SEC</span>
          </div>
          <div style={{ height: 4, background: TD.paperHi, marginTop: 12, borderRadius: 999, overflow: 'hidden' }}>
            <div style={{ width: '62%', height: '100%', background: `linear-gradient(90deg, ${TD.lime}, #C8A030)` }} />
          </div>
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 30, left: 22, right: 22 }}>
        <button style={{ width: '100%', padding: '18px 22px', border: 'none', background: TD.lime, color: TD.paper, fontFamily: TD.sans, fontSize: 15, fontWeight: 600, letterSpacing: '-0.01em', borderRadius: 999, cursor: 'pointer', display: 'flex', justifyContent: 'space-between', alignItems: 'center', boxShadow: `0 14px 32px -8px ${TD.lime}88` }}>
          <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span style={{ width: 24, height: 24, borderRadius: '50%', background: TD.paper, color: TD.lime, display: 'grid', placeItems: 'center', fontSize: 12, fontWeight: 700 }}>⚡</span>
            Revendică oferta
          </span>
          <span style={{ width: 30, height: 30, borderRadius: '50%', background: TD.paper, color: TD.lime, display: 'grid', placeItems: 'center', fontSize: 16 }}>→</span>
        </button>
        <div style={{ textAlign: 'center', marginTop: 12, fontFamily: TD.mono, fontSize: 10, letterSpacing: '0.14em', color: TD.ink40, textTransform: 'uppercase' }}>Nu mulțumesc · mai târziu</div>
      </div>
    </DScreen>
  );
}

// ─── 6. Order Sent ───────────────────────────────────────────
function SentScreenDark() {
  const steps = [
    { t: '19:42', label: 'Trimis la bucătărie', done: true },
    { t: '19:44', label: 'În pregătire', done: true, active: true },
    { t: '~20:04', label: 'Servire la masă', done: false },
  ];
  return (
    <DScreen>
      <div style={{ position: 'absolute', top: 0, right: -80, width: 320, height: 320, borderRadius: '50%', filter: 'blur(50px)', opacity: 0.3, background: `radial-gradient(circle, ${TD.lime} 0%, transparent 70%)` }} />
      <div style={{ position: 'absolute', bottom: 100, left: -60, width: 220, height: 220, borderRadius: '50%', filter: 'blur(45px)', opacity: 0.22, background: `radial-gradient(circle, ${TD.accent} 0%, transparent 70%)`, pointerEvents: 'none' }} />

      <div style={{ position: 'relative', padding: '10px 22px 0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <DTag><DDot color={TD.accent} glow />Masa 07</DTag>
        <DTag color="#4CAF50">Confirmat ✓</DTag>
      </div>

      <div style={{ position: 'relative', padding: '38px 22px 0' }}>
        <div style={{ fontFamily: TD.mono, fontSize: 11, letterSpacing: '0.2em', color: TD.accent, marginBottom: 12 }}>MULȚUMIM —</div>
        <h1 style={{ fontFamily: TD.display, fontWeight: 500, fontSize: 52, lineHeight: 0.9, letterSpacing: '-0.035em', margin: 0, color: TD.ink }}>
          Comanda<br/><span style={{ color: TD.accent }}>a plecat</span><br/>la bucătărie.
        </h1>
        <p style={{ fontSize: 14, lineHeight: 1.45, color: TD.ink70, maxWidth: 300, margin: '22px 0 28px' }}>Chef-ul Andrei a primit nota. Urmăriți progresul live mai jos.</p>

        <div style={{ background: TD.card, border: `1px solid ${TD.ink12}`, borderRadius: 20, padding: '6px 16px', marginBottom: 14 }}>
          {steps.map((s, i) => (
            <div key={i} style={{ display: 'grid', gridTemplateColumns: '56px 16px 1fr auto', alignItems: 'center', padding: '14px 0', gap: 10, borderBottom: i < steps.length-1 ? `1px solid ${TD.ink06}` : 'none' }}>
              <span style={{ fontFamily: TD.mono, fontSize: 10, color: TD.ink70, letterSpacing: '0.08em' }}>{s.t}</span>
              <DDot color={s.done ? (s.active ? TD.accent : TD.ink) : 'transparent'} size={10} glow={s.active} style={s.done ? {} : { border: `1px solid ${TD.ink40}` }} />
              <span style={{ fontSize: 15, fontWeight: s.active ? 600 : 500, letterSpacing: '-0.01em', color: s.done ? TD.ink : TD.ink40 }}>{s.label}</span>
              {s.active && <DTag color={TD.accent}><DDot color={TD.accent} size={4} />ACUM</DTag>}
            </div>
          ))}
        </div>

        <div style={{ background: TD.card, border: `1px solid ${TD.ink12}`, borderRadius: 20, padding: 16 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
            <span style={{ fontFamily: TD.mono, fontSize: 10, letterSpacing: '0.14em', color: TD.ink40 }}>NOTĂ #4821-07</span>
            <span style={{ fontFamily: TD.mono, fontSize: 10, letterSpacing: '0.14em', color: TD.lime }}>3 ITEMS</span>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
            <span style={{ fontSize: 13, color: TD.ink70 }}>Total estimat</span>
            <span style={{ fontSize: 26, fontWeight: 600, letterSpacing: '-0.03em' }}>218<span style={{ fontSize: 12, color: TD.ink70, marginLeft: 3 }}>RON</span></span>
          </div>
          <div style={{ marginTop: 10, paddingTop: 10, borderTop: `1px solid ${TD.ink12}`, fontFamily: TD.mono, fontSize: 10, letterSpacing: '0.1em', color: TD.ink40 }}>PLATA LA FINAL · CASH SAU CARD</div>
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 30, left: 16, right: 16, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
        <button style={{ padding: '14px 16px', border: `1px solid ${TD.ink12}`, background: TD.card, color: TD.ink, borderRadius: 999, fontSize: 13, fontWeight: 600, cursor: 'pointer' }}>Chemă chelner</button>
        <button style={{ padding: '14px 16px', border: 'none', background: TD.accent, color: TD.ink, borderRadius: 999, fontSize: 13, fontWeight: 600, cursor: 'pointer' }}>Mai comand →</button>
      </div>
    </DScreen>
  );
}

// ─── Export ──────────────────────────────────────────────────
Object.assign(window, {
  WelcomeScreenDark,
  CategoryScreenDark,
  PebblesScreenDark,
  MenuScreenDark,
  CartScreenDark,
  FlashDealScreenDark,
  SentScreenDark,
});

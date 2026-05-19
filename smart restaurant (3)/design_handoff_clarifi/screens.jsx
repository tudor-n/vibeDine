// Clarifi customer-app screens — "Warm modern" direction.
// Warm off-white ground, sharp modern sans, bold terracotta + acid-lime pop,
// rounded pill geometry, soft shadows, a tiny bit flashy on the deal moment.

// ─────────────────────────────────────────────────────────────
// Design tokens
// ─────────────────────────────────────────────────────────────
const T = {
  paper:    '#F4EEE2',    // warm cream
  paperHi:  '#FBF7EC',
  card:     '#FFFFFF',
  ink:      '#15140F',
  ink70:    'rgba(21,20,15,0.62)',
  ink40:    'rgba(21,20,15,0.34)',
  ink12:    'rgba(21,20,15,0.10)',
  ink06:    'rgba(21,20,15,0.05)',
  accent:   '#FF4A1C',    // bold terracotta
  accentDk: '#C0340F',
  lime:     '#D9F54A',    // flashy pop
  blue:     '#2B6BFF',
  sans:     '"Inter Tight", "Geist", -apple-system, system-ui, sans-serif',
  display:  '"Inter Tight", "Geist", -apple-system, system-ui, sans-serif',
  mono:     '"JetBrains Mono", ui-monospace, Menlo, monospace',
  STATUS_CLEAR: 58,  // top padding to clear iOS status bar + dynamic island
};

// ─────────────────────────────────────────────────────────────
// Shared atoms
// ─────────────────────────────────────────────────────────────
function Screen({ children, bg = T.paper, dark = false, style = {} }) {
  const ink = dark ? '#FBF7EC' : T.ink;
  return (
    <div style={{
      width: '100%', height: '100%', background: bg, color: ink,
      fontFamily: T.sans, position: 'relative', overflow: 'hidden',
      paddingTop: T.STATUS_CLEAR, ...style,
    }}>
      {/* subtle grain */}
      <div style={{
        position: 'absolute', inset: 0, pointerEvents: 'none', opacity: dark ? 0.18 : 0.35, mixBlendMode: 'multiply',
        backgroundImage: `url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='1.1' numOctaves='2' stitchTiles='stitch'/><feColorMatrix values='0 0 0 0 0.08  0 0 0 0 0.07  0 0 0 0 0.06  0 0 0 0.05 0'/></filter><rect width='100%25' height='100%25' filter='url(%23n)'/></svg>")`,
      }} />
      {children}
    </div>
  );
}

function Tag({ children, color = T.ink, bg = 'transparent', border = true, style = {} }) {
  return (
    <span style={{
      display: 'inline-flex', alignItems: 'center', gap: 6,
      fontFamily: T.mono, fontSize: 10, fontWeight: 500,
      letterSpacing: '0.14em', textTransform: 'uppercase',
      color, background: bg,
      border: border ? `1px solid ${color === T.ink ? T.ink12 : color}` : 'none',
      padding: '5px 9px', borderRadius: 999, lineHeight: 1,
      ...style,
    }}>{children}</span>
  );
}

function Dot({ color = T.accent, size = 7, glow = false, style = {} }) {
  return (
    <span style={{
      display: 'inline-block', width: size, height: size, borderRadius: '50%',
      background: color, boxShadow: glow ? `0 0 12px ${color}` : 'none',
      ...style,
    }} />
  );
}

// Placeholder image tile — modern gradient blobs, clean but honest
function FoodTile({ label, tone = 'terra', style = {} }) {
  const palettes = {
    terra:   { a: '#FF4A1C', b: '#FFB38A', c: '#FFF4EC' },
    sage:    { a: '#5E7A4C', b: '#B8CFA0', c: '#EEF3E3' },
    gold:    { a: '#E0A528', b: '#FFE29A', c: '#FFF7DE' },
    ink:     { a: '#2A261E', b: '#6A6153', c: '#D8CFBE' },
    blue:    { a: '#2B6BFF', b: '#9BB6FF', c: '#E6EDFF' },
    lime:    { a: '#9DC41A', b: '#D9F54A', c: '#F2FBC3' },
  };
  const p = palettes[tone] || palettes.terra;
  return (
    <div style={{
      position: 'relative', overflow: 'hidden', borderRadius: 14,
      background: p.c,
      ...style,
    }}>
      <div style={{
        position: 'absolute', inset: '-20%',
        background: `radial-gradient(60% 50% at 30% 30%, ${p.a} 0%, transparent 60%),
                     radial-gradient(50% 40% at 75% 75%, ${p.b} 0%, transparent 60%)`,
        filter: 'blur(2px)',
      }} />
      <div style={{
        position: 'absolute', left: 10, top: 10,
        fontFamily: T.mono, fontSize: 9, letterSpacing: '0.12em',
        color: 'rgba(21,20,15,0.55)', textTransform: 'uppercase',
        mixBlendMode: 'multiply',
      }}>{label}</div>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────
// Sample data
// ─────────────────────────────────────────────────────────────
const categories = [
  { id: 'start',  emoji: '◐', name: 'Începuturi',     count: 8,  tone: 'gold' },
  { id: 'main',   emoji: '◆', name: 'Principale',     count: 14, tone: 'terra' },
  { id: 'paste',  emoji: '◇', name: 'Paste & Risotto',count: 9,  tone: 'sage' },
  { id: 'desert', emoji: '◉', name: 'Desert',         count: 6,  tone: 'blue' },
  { id: 'bautur', emoji: '○', name: 'Băuturi',        count: 22, tone: 'lime' },
  { id: 'vin',    emoji: '●', name: 'Vinuri',         count: 18, tone: 'ink' },
];

const mains = [
  { id: 'm1', name: 'Mușchi de vită Oltenia',
    desc: 'Maturat 28 zile · piure trufat · jus de vin roșu · morcovi glasați',
    price: 128, tone: 'terra', tag: 'Semnătura' },
  { id: 'm2', name: 'Somn sălbatic la grătar',
    desc: 'Delta Dunării · unt de lămâie-capere · mazăre · cartof nou',
    price: 96,  tone: 'sage' },
  { id: 'm3', name: 'Mămăligă cu brânză de burduf',
    desc: 'Mărginimea Sibiului · coajă de brad · smântână',
    price: 54,  tone: 'gold', tag: 'Vegetarian' },
  { id: 'm4', name: 'Ciolan de porc la cuptor',
    desc: 'Marinat 24h · varză călită · muștar cu miere',
    price: 72,  tone: 'ink' },
];

// ─────────────────────────────────────────────────────────────
// 1. WELCOME
// ─────────────────────────────────────────────────────────────
function WelcomeScreen() {
  return (
    <Screen>
      {/* accent wash */}
      <div style={{
        position: 'absolute', top: -60, right: -80, width: 320, height: 320,
        borderRadius: '50%', filter: 'blur(40px)', opacity: 0.55,
        background: `radial-gradient(circle, ${T.accent} 0%, transparent 70%)`,
      }} />
      <div style={{
        position: 'absolute', top: 160, left: -100, width: 280, height: 280,
        borderRadius: '50%', filter: 'blur(50px)', opacity: 0.35,
        background: `radial-gradient(circle, ${T.lime} 0%, transparent 70%)`,
      }} />

      <div style={{ position: 'relative', padding: '14px 22px 0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Tag><Dot color={T.accent} glow />Masa 07 · live</Tag>
        <Tag>22 · 04 · 19:42</Tag>
      </div>

      <div style={{ position: 'relative', padding: '58px 22px 0' }}>
        <div style={{ fontFamily: T.mono, fontSize: 11, letterSpacing: '0.18em', color: T.accent, textTransform: 'uppercase', marginBottom: 18 }}>
          Bun venit —
        </div>
        <h1 style={{
          fontFamily: T.display, fontWeight: 500,
          fontSize: 68, lineHeight: 0.9, letterSpacing: '-0.045em',
          margin: 0,
        }}>
          Nume<br/>
          <span style={{ color: T.accent }}>Restaurant.</span>
        </h1>
        <p style={{
          fontSize: 15, lineHeight: 1.45, color: T.ink70,
          maxWidth: 310, margin: '26px 0 34px',
        }}>
          Meniul de seară, live pentru masa 07. Comandați direct — bucătăria pregătește când trimiteți.
        </p>

        {/* session chips */}
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginBottom: 28 }}>
          <Tag>07 · terasă</Tag>
          <Tag>2 oaspeți</Tag>
          <Tag>chef · Andrei C.</Tag>
          <Tag>#4821</Tag>
        </div>

        {/* Featured card */}
        <div style={{
          position: 'relative', background: T.ink, color: T.paperHi,
          borderRadius: 22, padding: 18, overflow: 'hidden',
          display: 'flex', gap: 14, alignItems: 'center',
          boxShadow: '0 20px 40px -20px rgba(21,20,15,0.45)',
        }}>
          <div style={{
            position: 'absolute', top: -40, right: -40, width: 180, height: 180,
            borderRadius: '50%', background: T.accent, filter: 'blur(30px)', opacity: 0.55,
          }} />
          <FoodTile label="TODAY" tone="terra" style={{ width: 72, height: 90, flexShrink: 0, borderRadius: 14, zIndex: 1 }} />
          <div style={{ position: 'relative', zIndex: 1, flex: 1 }}>
            <div style={{ fontFamily: T.mono, fontSize: 9, letterSpacing: '0.2em', color: T.lime, marginBottom: 6 }}>
              ASTĂZI · CHEF'S PICK
            </div>
            <div style={{ fontSize: 19, fontWeight: 500, letterSpacing: '-0.02em', lineHeight: 1.1, marginBottom: 4 }}>
              Mușchi de vită Oltenia
            </div>
            <div style={{ fontSize: 12, color: 'rgba(251,247,236,0.6)' }}>
              Maturat 28 zile · 128 RON
            </div>
          </div>
          <span style={{ fontSize: 22, opacity: 0.7, zIndex: 1 }}>→</span>
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 38, left: 22, right: 22 }}>
        <button style={{
          width: '100%', padding: '19px 22px', border: 'none',
          background: T.accent, color: '#FFF4EC',
          fontFamily: T.sans, fontSize: 15, fontWeight: 600, letterSpacing: '-0.01em',
          borderRadius: 999, cursor: 'pointer',
          display: 'flex', justifyContent: 'space-between', alignItems: 'center',
          boxShadow: `0 12px 28px -8px ${T.accent}66`,
        }}>
          <span>Deschide meniul</span>
          <span style={{
            width: 30, height: 30, borderRadius: '50%', background: '#FFF4EC', color: T.accent,
            display: 'grid', placeItems: 'center', fontSize: 16,
          }}>→</span>
        </button>
      </div>
    </Screen>
  );
}

// ─────────────────────────────────────────────────────────────
// 2. CATEGORIES
// ─────────────────────────────────────────────────────────────
function CategoryScreen() {
  return (
    <Screen>
      <div style={{ padding: '14px 22px 0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Tag><Dot color={T.accent} glow />Masa 07</Tag>
        <Tag>Ro / En</Tag>
      </div>

      <div style={{ padding: '22px 22px 20px' }}>
        <div style={{ fontFamily: T.mono, fontSize: 11, letterSpacing: '0.18em', color: T.accent, textTransform: 'uppercase', marginBottom: 10 }}>
          01 · Capitole
        </div>
        <h1 style={{
          fontFamily: T.display, fontWeight: 500,
          fontSize: 40, lineHeight: 0.95, letterSpacing: '-0.03em',
          margin: '0 0 8px',
        }}>
          Ce aveți poftă<br/>
          <span style={{ color: T.accent }}>să gustați?</span>
        </h1>
        <p style={{ fontSize: 13, color: T.ink70, margin: 0 }}>
          77 preparate · bucătărie românească contemporană
        </p>
      </div>

      {/* search pill */}
      <div style={{ padding: '0 22px 18px' }}>
        <div style={{
          background: T.card, border: `1px solid ${T.ink12}`,
          borderRadius: 999, padding: '12px 16px',
          display: 'flex', alignItems: 'center', gap: 10,
          fontSize: 13, color: T.ink40,
          boxShadow: '0 2px 8px -4px rgba(21,20,15,0.06)',
        }}>
          <span style={{ fontSize: 14 }}>⌕</span>
          <span>Caută preparate, ingrediente…</span>
          <span style={{ marginLeft: 'auto', fontFamily: T.mono, fontSize: 10, letterSpacing: '0.12em', color: T.ink40 }}>
            AI
          </span>
        </div>
      </div>

      {/* category grid */}
      <div style={{ padding: '0 22px', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
        {categories.map((c, i) => (
          <button key={c.id} style={{
            position: 'relative', textAlign: 'left', cursor: 'pointer',
            background: T.card, border: `1px solid ${T.ink12}`,
            borderRadius: 18, padding: 14, height: 104,
            display: 'flex', flexDirection: 'column', justifyContent: 'space-between',
            overflow: 'hidden',
          }}>
            <FoodTile label="" tone={c.tone} style={{
              position: 'absolute', right: -20, top: -20, width: 80, height: 80, borderRadius: '50%', opacity: 0.9,
            }} />
            <div style={{ fontFamily: T.mono, fontSize: 10, color: T.ink40, letterSpacing: '0.14em', position: 'relative' }}>
              {String(i + 1).padStart(2, '0')}
            </div>
            <div style={{ position: 'relative' }}>
              <div style={{ fontSize: 17, fontWeight: 500, letterSpacing: '-0.02em', lineHeight: 1.1 }}>
                {c.name}
              </div>
              <div style={{ fontSize: 11, color: T.ink70, marginTop: 4, fontFamily: T.mono, letterSpacing: '0.08em' }}>
                {c.count} items
              </div>
            </div>
          </button>
        ))}
      </div>

      {/* floating order bar */}
      <div style={{
        position: 'absolute', bottom: 34, left: 16, right: 16,
        background: T.ink, color: T.paperHi, padding: '12px 14px 12px 18px',
        borderRadius: 999,
        display: 'flex', alignItems: 'center', justifyContent: 'space-between',
        boxShadow: '0 20px 40px -16px rgba(21,20,15,0.4)',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <div style={{
            background: T.lime, color: T.ink, borderRadius: 999,
            padding: '4px 10px', fontFamily: T.mono, fontSize: 10, fontWeight: 600, letterSpacing: '0.1em',
          }}>3</div>
          <div>
            <div style={{ fontFamily: T.mono, fontSize: 9, letterSpacing: '0.14em', color: 'rgba(251,247,236,0.55)' }}>ÎN COȘ</div>
            <div style={{ fontSize: 17, fontWeight: 500, letterSpacing: '-0.02em' }}>218 RON</div>
          </div>
        </div>
        <div style={{
          background: T.accent, color: '#FFF4EC', borderRadius: 999,
          padding: '12px 18px', fontSize: 13, fontWeight: 600,
          display: 'flex', alignItems: 'center', gap: 6,
        }}>Vezi comanda →</div>
      </div>
    </Screen>
  );
}

// ─────────────────────────────────────────────────────────────
// 3. MENU LIST + RECS
// ─────────────────────────────────────────────────────────────
function MenuScreen() {
  return (
    <Screen>
      <div style={{ position: 'absolute', top: -40, left: -60, width: 260, height: 260, borderRadius: '50%', filter: 'blur(45px)', opacity: 0.28, background: `radial-gradient(circle, ${T.accent} 0%, transparent 70%)`, pointerEvents: 'none' }} />
      <div style={{ position: 'absolute', bottom: 120, right: -60, width: 200, height: 200, borderRadius: '50%', filter: 'blur(40px)', opacity: 0.22, background: `radial-gradient(circle, ${T.lime} 0%, transparent 70%)`, pointerEvents: 'none' }} />
      {/* header */}
      <div style={{
        padding: '10px 22px 14px',
        display: 'flex', alignItems: 'center', justifyContent: 'space-between',
      }}>
        <div style={{
          width: 38, height: 38, borderRadius: '50%',
          background: T.card, border: `1px solid ${T.ink12}`,
          display: 'grid', placeItems: 'center', fontSize: 16,
        }}>←</div>
        <div style={{ textAlign: 'center' }}>
          <div style={{ fontFamily: T.mono, fontSize: 9, letterSpacing: '0.18em', color: T.ink40 }}>02 · 06</div>
          <div style={{ fontSize: 16, fontWeight: 500, letterSpacing: '-0.02em', marginTop: 1 }}>Principale</div>
        </div>
        <div style={{
          width: 38, height: 38, borderRadius: '50%',
          background: T.card, border: `1px solid ${T.ink12}`,
          display: 'grid', placeItems: 'center', fontSize: 14, color: T.ink70,
        }}>⌕</div>
      </div>

      {/* chips */}
      <div style={{
        display: 'flex', gap: 6, padding: '0 22px 14px',
        overflowX: 'auto', whiteSpace: 'nowrap',
      }}>
        {['Toate','Carne','Pește','Veggie','Chef'].map((t, i) => (
          <span key={t} style={{
            fontSize: 12, fontWeight: 500,
            padding: '8px 14px', borderRadius: 999,
            background: i === 0 ? T.ink : T.card,
            color: i === 0 ? T.paperHi : T.ink,
            border: i === 0 ? 'none' : `1px solid ${T.ink12}`,
          }}>{t}</span>
        ))}
      </div>

      {/* Dish cards */}
      <div style={{ padding: '0 16px 18px', display: 'flex', flexDirection: 'column', gap: 10 }}>
        {mains.map((m) => (
          <article key={m.id} style={{
            background: T.card, borderRadius: 20, padding: 12,
            display: 'grid', gridTemplateColumns: '96px 1fr', gap: 14,
            border: `1px solid ${T.ink12}`,
            boxShadow: '0 2px 10px -6px rgba(21,20,15,0.08)',
          }}>
            <FoodTile label={m.id.toUpperCase()} tone={m.tone} style={{ width: 96, height: 110 }} />
            <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'space-between', padding: '2px 0' }}>
              <div>
                {m.tag && (
                  <Tag color={T.accent} style={{ marginBottom: 6 }}>
                    <Dot color={T.accent} size={5} />{m.tag}
                  </Tag>
                )}
                <div style={{
                  fontSize: 16, fontWeight: 500, letterSpacing: '-0.02em',
                  lineHeight: 1.15, marginBottom: 4,
                }}>{m.name}</div>
                <div style={{ fontSize: 11.5, color: T.ink70, lineHeight: 1.4 }}>{m.desc}</div>
              </div>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginTop: 8 }}>
                <div style={{ fontSize: 18, fontWeight: 600, letterSpacing: '-0.02em' }}>
                  {m.price}<span style={{ fontSize: 10, color: T.ink70, marginLeft: 3, fontWeight: 500 }}>RON</span>
                </div>
                <button style={{
                  background: T.ink, color: T.paperHi, border: 'none',
                  width: 34, height: 34, borderRadius: '50%',
                  fontSize: 18, cursor: 'pointer', display: 'grid', placeItems: 'center',
                  fontWeight: 400, lineHeight: 1,
                }}>+</button>
              </div>
            </div>
          </article>
        ))}
      </div>

      {/* Recommendations */}
      <div style={{
        padding: '18px 16px 120px',
        background: T.paperHi,
        borderTop: `1px solid ${T.ink12}`,
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '0 6px', marginBottom: 4 }}>
          <div style={{
            display: 'inline-flex', alignItems: 'center', gap: 6,
            background: T.lime, color: T.ink, borderRadius: 999,
            padding: '5px 10px', fontFamily: T.mono, fontSize: 10, fontWeight: 600, letterSpacing: '0.12em',
          }}>
            <Dot color={T.ink} size={5} />AI · LIVE
          </div>
          <span style={{ fontSize: 11, color: T.ink40, fontFamily: T.mono, letterSpacing: '0.1em' }}>2s ago</span>
        </div>
        <div style={{ padding: '0 6px' }}>
          <h4 style={{
            fontSize: 22, fontWeight: 500, letterSpacing: '-0.025em',
            margin: '8px 0 4px',
          }}>
            Se potrivesc cu<br/>ce ai ales.
          </h4>
          <p style={{ fontSize: 12, color: T.ink70, margin: '0 0 14px' }}>
            Bazat pe mușchiul de vită din coș
          </p>
        </div>
        <div style={{ display: 'flex', gap: 10, overflowX: 'auto', padding: '0 6px 4px' }}>
          {[
            { name: 'Fetească Neagră', sub: 'Prințul Știrbey · pahar', reason: 'Taninuri potrivite cu vita maturată', price: 42, tone: 'ink' },
            { name: 'Unt cu trufe & pâine',   sub: 'Appetizer', reason: 'Completează jusul de vin', price: 18, tone: 'gold' },
            { name: 'Salată verde cu nuci',   sub: 'Contorno',  reason: 'Contrast proaspăt',        price: 28, tone: 'sage' },
          ].map((r, i) => (
            <div key={i} style={{
              minWidth: 210, background: T.card,
              border: `1px solid ${T.ink12}`, borderRadius: 18,
              padding: 12, display: 'flex', flexDirection: 'column',
            }}>
              <FoodTile label={`PAIR ${i+1}`} tone={r.tone} style={{ height: 76, marginBottom: 10 }} />
              <div style={{ fontFamily: T.mono, fontSize: 9, letterSpacing: '0.14em', color: T.ink40, marginBottom: 2 }}>
                0{i+1} · PAIRING
              </div>
              <div style={{ fontSize: 15, fontWeight: 500, letterSpacing: '-0.01em', lineHeight: 1.15 }}>
                {r.name}
              </div>
              <div style={{ fontSize: 11, color: T.ink40, marginTop: 2 }}>{r.sub}</div>
              <div style={{
                fontSize: 11, color: T.ink70, fontStyle: 'italic',
                flex: 1, margin: '10px 0 10px',
              }}>"{r.reason}"</div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <span style={{ fontSize: 14, fontWeight: 600, letterSpacing: '-0.01em' }}>
                  {r.price}<span style={{ fontSize: 10, color: T.ink70, marginLeft: 2, fontWeight: 500 }}>RON</span>
                </span>
                <button style={{
                  background: T.accent, color: '#FFF4EC', border: 'none',
                  borderRadius: 999, padding: '6px 12px',
                  fontSize: 11, fontWeight: 600, cursor: 'pointer',
                }}>+ Adaugă</button>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Floating cart bar */}
      <div style={{
        position: 'absolute', bottom: 24, left: 14, right: 14,
        background: T.ink, color: T.paperHi, padding: '12px 14px 12px 18px',
        borderRadius: 999,
        display: 'flex', alignItems: 'center', justifyContent: 'space-between',
        boxShadow: '0 20px 40px -16px rgba(21,20,15,0.4)',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <div style={{
            background: T.lime, color: T.ink, borderRadius: 999,
            padding: '4px 10px', fontFamily: T.mono, fontSize: 10, fontWeight: 600, letterSpacing: '0.1em',
          }}>3</div>
          <div>
            <div style={{ fontFamily: T.mono, fontSize: 9, letterSpacing: '0.14em', color: 'rgba(251,247,236,0.55)' }}>ÎN COȘ</div>
            <div style={{ fontSize: 17, fontWeight: 500, letterSpacing: '-0.02em' }}>218 RON</div>
          </div>
        </div>
        <div style={{
          background: T.accent, color: '#FFF4EC', borderRadius: 999,
          padding: '11px 16px', fontSize: 13, fontWeight: 600,
        }}>Vezi comanda →</div>
      </div>
    </Screen>
  );
}

// ─────────────────────────────────────────────────────────────
// 4. CART
// ─────────────────────────────────────────────────────────────
const cartItems = [
  { id: 1, name: 'Mușchi de vită Oltenia',     qty: 1, price: 128, tone: 'terra' },
  { id: 2, name: 'Mămăligă cu brânză',          qty: 2, price: 54,  tone: 'gold' },
  { id: 3, name: 'Fetească Neagră · pahar',   qty: 1, price: 42,  tone: 'ink',
    note: 'Sugerat de AI' },
];

function CartScreen() {
  const subtotal = cartItems.reduce((s, i) => s + i.price * i.qty, 0);
  return (
    <Screen>
      <div style={{ position: 'absolute', top: -50, right: -60, width: 240, height: 240, borderRadius: '50%', filter: 'blur(45px)', opacity: 0.22, background: `radial-gradient(circle, ${T.accent} 0%, transparent 70%)`, pointerEvents: 'none' }} />
      <div style={{ position: 'absolute', bottom: 140, left: -50, width: 200, height: 200, borderRadius: '50%', filter: 'blur(40px)', opacity: 0.18, background: `radial-gradient(circle, ${T.lime} 0%, transparent 70%)`, pointerEvents: 'none' }} />
      <div style={{
        padding: '10px 22px 14px',
        display: 'flex', alignItems: 'center', justifyContent: 'space-between',
      }}>
        <Tag><Dot color={T.accent} glow />Masa 07</Tag>
        <Tag>Draft #4821</Tag>
        <div style={{
          width: 32, height: 32, borderRadius: '50%',
          background: T.card, border: `1px solid ${T.ink12}`,
          display: 'grid', placeItems: 'center', fontSize: 14,
        }}>✕</div>
      </div>

      <div style={{ padding: '22px 22px 18px' }}>
        <div style={{ fontFamily: T.mono, fontSize: 11, letterSpacing: '0.18em', color: T.accent, textTransform: 'uppercase', marginBottom: 10 }}>
          Comanda ta
        </div>
        <h1 style={{
          fontFamily: T.display, fontWeight: 500,
          fontSize: 42, lineHeight: 0.92, letterSpacing: '-0.03em',
          margin: '0 0 8px',
        }}>
          Gata de<br/>
          <span style={{ color: T.accent }}>trimis</span>.
        </h1>
        <p style={{ fontSize: 13, color: T.ink70, margin: 0 }}>
          3 preparate · ~22 min în pregătire
        </p>
      </div>

      <div style={{ padding: '0 16px', display: 'flex', flexDirection: 'column', gap: 8 }}>
        {cartItems.map((it) => (
          <div key={it.id} style={{
            background: T.card, border: `1px solid ${T.ink12}`,
            borderRadius: 18, padding: 10,
            display: 'grid', gridTemplateColumns: '60px 1fr auto', gap: 12, alignItems: 'center',
          }}>
            <FoodTile label="" tone={it.tone} style={{ width: 60, height: 60, borderRadius: 14 }} />
            <div>
              <div style={{ fontSize: 14.5, fontWeight: 500, letterSpacing: '-0.01em', lineHeight: 1.2 }}>
                {it.name}
              </div>
              {it.note ? (
                <div style={{
                  display: 'inline-flex', alignItems: 'center', gap: 4,
                  marginTop: 4, fontSize: 10, color: T.ink,
                  background: T.lime, padding: '3px 7px', borderRadius: 999,
                  fontFamily: T.mono, letterSpacing: '0.1em', fontWeight: 600,
                }}>
                  <Dot color={T.ink} size={4} />{it.note.toUpperCase()}
                </div>
              ) : (
                <div style={{ fontSize: 11, color: T.ink40, marginTop: 4, fontFamily: T.mono, letterSpacing: '0.1em' }}>
                  {it.price} × {it.qty}
                </div>
              )}
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 6 }}>
              <div style={{ fontSize: 15, fontWeight: 600, letterSpacing: '-0.01em' }}>
                {it.price * it.qty}<span style={{ fontSize: 9, color: T.ink70, marginLeft: 2, fontWeight: 500 }}>RON</span>
              </div>
              <div style={{
                display: 'inline-flex', alignItems: 'center',
                background: T.paper, borderRadius: 999, padding: 2, gap: 2,
              }}>
                <span style={{ width: 22, height: 22, display: 'grid', placeItems: 'center', fontSize: 14, color: T.ink70, cursor: 'pointer' }}>−</span>
                <span style={{ width: 18, textAlign: 'center', fontFamily: T.mono, fontSize: 11, fontWeight: 600 }}>{it.qty}</span>
                <span style={{ width: 22, height: 22, display: 'grid', placeItems: 'center', fontSize: 14, color: T.accent, cursor: 'pointer' }}>+</span>
              </div>
            </div>
          </div>
        ))}
      </div>

      <div style={{ padding: '18px 22px 0' }}>
        <div style={{
          background: T.card, border: `1px solid ${T.ink12}`,
          borderRadius: 18, padding: '14px 16px',
        }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, color: T.ink70, marginBottom: 6 }}>
            <span>Subtotal</span><span>{subtotal} RON</span>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, color: T.ink70, marginBottom: 10 }}>
            <span>Servire</span><span>inclus</span>
          </div>
          <div style={{
            display: 'flex', justifyContent: 'space-between', alignItems: 'baseline',
            borderTop: `1px solid ${T.ink12}`, paddingTop: 10,
          }}>
            <span style={{ fontSize: 16, fontWeight: 500 }}>Total</span>
            <span style={{ fontSize: 26, fontWeight: 600, letterSpacing: '-0.03em' }}>
              {subtotal}<span style={{ fontSize: 12, color: T.ink70, marginLeft: 3, fontWeight: 500 }}>RON</span>
            </span>
          </div>
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 30, left: 16, right: 16 }}>
        <button style={{
          width: '100%', padding: '18px 22px', border: 'none',
          background: T.accent, color: '#FFF4EC',
          fontFamily: T.sans, fontSize: 15, fontWeight: 600, letterSpacing: '-0.01em',
          borderRadius: 999, cursor: 'pointer',
          display: 'flex', justifyContent: 'space-between', alignItems: 'center',
          boxShadow: `0 14px 28px -8px ${T.accent}66`,
        }}>
          <span>Trimite la bucătărie</span>
          <span style={{
            width: 30, height: 30, borderRadius: '50%', background: '#FFF4EC', color: T.accent,
            display: 'grid', placeItems: 'center', fontSize: 16,
          }}>→</span>
        </button>
      </div>
    </Screen>
  );
}

// ─────────────────────────────────────────────────────────────
// 5. FLASH DEAL — the flashy moment
// ─────────────────────────────────────────────────────────────
function FlashDealScreen() {
  return (
    <Screen>
      {/* lime-led bloom — flash deal gets its own colour identity */}
      <div style={{
        position: 'absolute', top: -80, right: -90, width: 360, height: 360,
        borderRadius: '50%', filter: 'blur(50px)', opacity: 0.65,
        background: `radial-gradient(circle, ${T.lime} 0%, transparent 70%)`,
      }} />
      <div style={{
        position: 'absolute', top: 320, left: -120, width: 300, height: 300,
        borderRadius: '50%', filter: 'blur(60px)', opacity: 0.35,
        background: `radial-gradient(circle, #9DC41A 0%, transparent 70%)`,
      }} />

      <div style={{ position: 'relative', padding: '10px 22px 0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Tag color="#3E7B10" bg="rgba(217,245,74,0.18)">
          <Dot color="#3E7B10" glow />Live · chef drop
        </Tag>
        <Tag>Masa 07 · 03:47</Tag>
      </div>

      <div style={{ position: 'relative', padding: '24px 22px 0' }}>
        <div style={{
          display: 'inline-flex', alignItems: 'center', gap: 6,
          fontFamily: T.mono, fontSize: 11, letterSpacing: '0.2em',
          color: '#3E7B10', marginBottom: 12,
        }}>
          <span style={{
            width: 18, height: 18, borderRadius: '50%', background: T.lime, color: T.ink,
            display: 'grid', placeItems: 'center', fontSize: 10,
          }}>⚡</span>
          FLASH DEAL
        </div>
        <h1 style={{
          fontFamily: T.display, fontWeight: 500,
          fontSize: 48, lineHeight: 0.9, letterSpacing: '-0.035em',
          margin: 0,
        }}>
          Doar pentru<br/>
          <span style={{ color: T.ink, background: T.lime, borderRadius: 8, padding: '0 10px 2px', display: 'inline-block' }}>următoarele</span><br/>
          4 minute.
        </h1>
        <p style={{ fontSize: 13.5, lineHeight: 1.45, color: T.ink70, maxWidth: 300, margin: '18px 0 20px' }}>
          Chef-ul Andrei are cantitate limitată. Doar <strong style={{ color: T.ink, fontWeight: 600 }}>3 mese</strong> primesc oferta chiar acum.
        </p>

        {/* Deal card — light, matches the rest */}
        <div style={{
          position: 'relative', overflow: 'hidden',
          background: T.card, border: `1px solid ${T.ink12}`,
          borderRadius: 22, padding: 14, marginBottom: 18,
          display: 'grid', gridTemplateColumns: '88px 1fr', gap: 14,
          boxShadow: '0 18px 40px -18px rgba(21,20,15,0.22)',
        }}>
          <FoodTile label="FLASH" tone="terra" style={{ width: 88, height: 104 }} />
          <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'space-between' }}>
            <div>
              <div style={{
                display: 'inline-flex', alignItems: 'center', gap: 4,
                background: T.lime, color: T.ink, borderRadius: 999,
                padding: '3px 9px', fontFamily: T.mono, fontSize: 10, fontWeight: 700, letterSpacing: '0.12em',
                marginBottom: 6,
              }}>−43%</div>
              <div style={{ fontSize: 17, fontWeight: 500, letterSpacing: '-0.02em', lineHeight: 1.15 }}>
                Tartar de vită cu trufe
              </div>
              <div style={{ fontSize: 11, color: T.ink40, marginTop: 2 }}>
                Chef's cut · porție limitată
              </div>
            </div>
            <div style={{ display: 'flex', alignItems: 'baseline', gap: 10, marginTop: 8 }}>
              <span style={{ fontSize: 28, fontWeight: 700, letterSpacing: '-0.03em', color: '#3E7B10' }}>
                48<span style={{ fontSize: 11, marginLeft: 2, color: T.ink70, fontWeight: 500 }}>RON</span>
              </span>
              <span style={{
                fontSize: 12, color: T.ink40,
                textDecoration: 'line-through',
              }}>84 RON</span>
            </div>
          </div>
          {/* corner ribbon */}
          <div style={{
            position: 'absolute', top: 10, right: -34,
            background: T.lime, color: T.ink,
            fontFamily: T.mono, fontSize: 9, fontWeight: 700, letterSpacing: '0.2em',
            padding: '3px 40px', transform: 'rotate(35deg)',
          }}>HOT</div>
        </div>

        {/* Countdown card */}
        <div style={{
          background: T.card, border: `1px solid ${T.ink12}`,
          borderRadius: 20, padding: 14, marginBottom: 12,
        }}>
          <div style={{
            display: 'flex', justifyContent: 'space-between', alignItems: 'baseline',
            marginBottom: 6,
          }}>
            <span style={{ fontFamily: T.mono, fontSize: 10, letterSpacing: '0.16em', color: T.ink70 }}>
              EXPIRĂ ÎN
            </span>
            <span style={{ fontFamily: T.mono, fontSize: 10, letterSpacing: '0.16em', color: '#3E7B10' }}>
              62% RĂMAS
            </span>
          </div>
          <div style={{ display: 'flex', alignItems: 'baseline', gap: 2 }}>
            <span style={{ fontFamily: T.mono, fontSize: 48, fontWeight: 500, letterSpacing: '-0.04em', lineHeight: 1, color: T.ink }}>03</span>
            <span style={{ fontFamily: T.mono, fontSize: 48, fontWeight: 500, color: '#3E7B10', lineHeight: 1 }}>:</span>
            <span style={{ fontFamily: T.mono, fontSize: 48, fontWeight: 500, letterSpacing: '-0.04em', lineHeight: 1, color: T.ink }}>47</span>
            <span style={{ fontFamily: T.mono, fontSize: 10, color: T.ink40, marginLeft: 'auto', letterSpacing: '0.14em' }}>MIN : SEC</span>
          </div>
          <div style={{ height: 4, background: T.paper, marginTop: 12, borderRadius: 999, overflow: 'hidden', border: `1px solid ${T.ink12}` }}>
            <div style={{
              width: '62%', height: '100%',
              background: `linear-gradient(90deg, ${T.lime}, #9DC41A)`,
            }} />
          </div>
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 30, left: 22, right: 22 }}>
        <button style={{
          width: '100%', padding: '18px 22px', border: 'none',
          background: T.lime, color: T.ink,
          fontFamily: T.sans, fontSize: 15, fontWeight: 600, letterSpacing: '-0.01em',
          borderRadius: 999, cursor: 'pointer',
          display: 'flex', justifyContent: 'space-between', alignItems: 'center',
          boxShadow: `0 14px 32px -8px ${T.lime}99`,
        }}>
          <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span style={{
              width: 24, height: 24, borderRadius: '50%', background: T.ink, color: T.lime,
              display: 'grid', placeItems: 'center', fontSize: 12, fontWeight: 700,
            }}>⚡</span>
            Revendică oferta
          </span>
          <span style={{
            width: 30, height: 30, borderRadius: '50%', background: T.ink, color: T.lime,
            display: 'grid', placeItems: 'center', fontSize: 16,
          }}>→</span>
        </button>
        <div style={{
          textAlign: 'center', marginTop: 12,
          fontFamily: T.mono, fontSize: 10, letterSpacing: '0.14em',
          color: T.ink40, textTransform: 'uppercase',
        }}>
          Nu mulțumesc · mai târziu
        </div>
      </div>
    </Screen>
  );
}

// ─────────────────────────────────────────────────────────────
// 6. ORDER SENT
// ─────────────────────────────────────────────────────────────
function SentScreen() {
  const steps = [
    { t: '19:42', label: 'Trimis la bucătărie', done: true },
    { t: '19:44', label: 'În pregătire', done: true, active: true },
    { t: '~20:04', label: 'Servire la masă', done: false },
  ];
  return (
    <Screen>
      <div style={{
        position: 'absolute', top: 0, right: -80, width: 320, height: 320,
        borderRadius: '50%', filter: 'blur(50px)', opacity: 0.4,
        background: `radial-gradient(circle, ${T.lime} 0%, transparent 70%)`,
      }} />
      <div style={{
        position: 'absolute', bottom: 100, left: -60, width: 220, height: 220,
        borderRadius: '50%', filter: 'blur(45px)', opacity: 0.20,
        background: `radial-gradient(circle, ${T.accent} 0%, transparent 70%)`,
        pointerEvents: 'none',
      }} />

      <div style={{ position: 'relative', padding: '10px 22px 0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Tag><Dot color={T.accent} glow />Masa 07</Tag>
        <Tag color="#3C7A3C" bg="rgba(60,122,60,0.08)">Confirmat ✓</Tag>
      </div>

      <div style={{ position: 'relative', padding: '38px 22px 0' }}>
        <div style={{ fontFamily: T.mono, fontSize: 11, letterSpacing: '0.2em', color: T.accent, marginBottom: 12 }}>
          MULȚUMIM —
        </div>
        <h1 style={{
          fontFamily: T.display, fontWeight: 500,
          fontSize: 52, lineHeight: 0.9, letterSpacing: '-0.035em',
          margin: 0,
        }}>
          Comanda<br/>
          <span style={{ color: T.accent }}>a plecat</span><br/>
          la bucătărie.
        </h1>

        <p style={{ fontSize: 14, lineHeight: 1.45, color: T.ink70, maxWidth: 300, margin: '22px 0 28px' }}>
          Chef-ul Andrei a primit nota. Urmăriți progresul live mai jos.
        </p>

        {/* timeline card */}
        <div style={{
          background: T.card, border: `1px solid ${T.ink12}`,
          borderRadius: 20, padding: '6px 16px', marginBottom: 14,
        }}>
          {steps.map((s, i) => (
            <div key={i} style={{
              display: 'grid', gridTemplateColumns: '56px 16px 1fr auto',
              alignItems: 'center', padding: '14px 0', gap: 10,
              borderBottom: i < steps.length - 1 ? `1px solid ${T.ink06}` : 'none',
            }}>
              <span style={{ fontFamily: T.mono, fontSize: 10, color: T.ink70, letterSpacing: '0.08em' }}>{s.t}</span>
              <span style={{ position: 'relative' }}>
                <Dot
                  color={s.done ? (s.active ? T.accent : T.ink) : 'transparent'}
                  size={10}
                  glow={s.active}
                  style={s.done ? {} : { border: `1px solid ${T.ink40}` }}
                />
              </span>
              <span style={{
                fontSize: 15, fontWeight: s.active ? 600 : 500,
                letterSpacing: '-0.01em',
                color: s.done ? T.ink : T.ink40,
              }}>{s.label}</span>
              {s.active && <Tag color={T.accent}><Dot color={T.accent} size={4} />ACUM</Tag>}
            </div>
          ))}
        </div>

        {/* Receipt */}
        <div style={{
          background: T.ink, color: T.paperHi, borderRadius: 20, padding: 16,
        }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
            <span style={{ fontFamily: T.mono, fontSize: 10, letterSpacing: '0.14em', color: 'rgba(251,247,236,0.55)' }}>
              NOTĂ #4821-07
            </span>
            <span style={{ fontFamily: T.mono, fontSize: 10, letterSpacing: '0.14em', color: T.lime }}>
              3 ITEMS
            </span>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
            <span style={{ fontSize: 13, color: 'rgba(251,247,236,0.6)' }}>Total estimat</span>
            <span style={{ fontSize: 26, fontWeight: 600, letterSpacing: '-0.03em' }}>
              218<span style={{ fontSize: 12, color: 'rgba(251,247,236,0.55)', marginLeft: 3, fontWeight: 500 }}>RON</span>
            </span>
          </div>
          <div style={{
            marginTop: 10, paddingTop: 10,
            borderTop: '1px solid rgba(251,247,236,0.1)',
            fontFamily: T.mono, fontSize: 10, letterSpacing: '0.1em',
            color: 'rgba(251,247,236,0.45)',
          }}>
            PLATA LA FINAL · CASH SAU CARD
          </div>
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 30, left: 16, right: 16, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
        <button style={{
          padding: '14px 16px', border: `1px solid ${T.ink12}`,
          background: T.card, color: T.ink,
          borderRadius: 999, fontSize: 13, fontWeight: 600, cursor: 'pointer',
        }}>Chemă chelner</button>
        <button style={{
          padding: '14px 16px', border: 'none',
          background: T.ink, color: T.paperHi,
          borderRadius: 999, fontSize: 13, fontWeight: 600, cursor: 'pointer',
        }}>Mai comand →</button>
      </div>
    </Screen>
  );
}

// ─────────────────────────────────────────────────────────────
// Export
// ─────────────────────────────────────────────────────────────
Object.assign(window, {
  WelcomeScreen, CategoryScreen, MenuScreen, CartScreen, FlashDealScreen, SentScreen,
});

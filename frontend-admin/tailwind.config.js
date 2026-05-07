/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{vue,js,ts,jsx,tsx}",
  ],
  darkMode: ['selector', '[data-theme="dark"]'],
  theme: {
    extend: {
      colors: {
        // CSS variable-driven so theme swap is automatic
        paper:    'var(--paper)',
        paperHi:  'var(--paperHi)',
        card:     'var(--card)',
        ink: {
          DEFAULT: 'var(--ink)',
          70: 'var(--ink70)',
          40: 'var(--ink40)',
          12: 'var(--ink12)',
          '06': 'var(--ink06)',
        },
        accent:   { DEFAULT: 'var(--accent)', dk: 'var(--accentDk)' },
        lime:     'var(--lime)',
        // Backwards compatibility — `text-brand` etc. still maps to accent
        brand:    { DEFAULT: 'var(--accent)', light: 'var(--accent)' },
      },
      fontFamily: {
        sans: ['"Inter Tight"', '-apple-system', 'system-ui', 'sans-serif'],
        display: ['"Inter Tight"', '-apple-system', 'system-ui', 'sans-serif'],
        mono: ['"JetBrains Mono"', 'ui-monospace', 'Menlo', 'monospace'],
      },
      borderRadius: {
        pill: '999px',
        '4xl': '28px',
      },
      letterSpacing: {
        display: '-0.04em',
        eyebrow: '0.18em',
      },
      boxShadow: {
        soft: 'var(--shadow-soft)',
        pop:  'var(--shadow-pop)',
      },
    },
  },
  plugins: [],
}

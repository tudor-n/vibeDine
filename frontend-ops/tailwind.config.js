/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{vue,js,ts,jsx,tsx}'],
  darkMode: ['selector', '[data-theme="dark"]'],
  theme: {
    extend: {
      colors: {
        paper: 'var(--paper)',
        paperHi: 'var(--paperHi)',
        card: 'var(--card)',
        ink: {
          DEFAULT: 'var(--ink)',
          70: 'var(--ink70)',
          40: 'var(--ink40)',
          12: 'var(--ink12)',
          '06': 'var(--ink06)',
        },
        accent: { DEFAULT: 'var(--accent)', dk: 'var(--accentDk)' },
        lime: 'var(--lime)',
        brand: { DEFAULT: 'var(--accent)', light: 'var(--accent)' },
      },
      fontFamily: {
        sans: ['"Inter Tight"', '-apple-system', 'system-ui', 'sans-serif'],
        display: ['"Inter Tight"', '-apple-system', 'system-ui', 'sans-serif'],
        mono: ['"JetBrains Mono"', 'ui-monospace', 'Menlo', 'monospace'],
      },
      borderRadius: { pill: '999px', '4xl': '28px' },
      boxShadow: { soft: 'var(--shadow-soft)', pop: 'var(--shadow-pop)' },
    },
  },
  plugins: [],
}

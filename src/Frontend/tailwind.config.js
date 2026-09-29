/**
 * Tailwind configuration. Colors are bound to CSS custom properties defined in
 * src/styles/tokens.css so light/dark themes swap in one place, and every
 * pairing there is chosen to meet WCAG 2.1 AA (>= 4.5:1 for text).
 * @type {import('tailwindcss').Config}
 */
const token = (name) => `rgb(var(--color-${name}) / <alpha-value>)`;

export default {
  content: ['./index.html', './src/**/*.ts'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        page: token('page'),
        surface: token('surface'),
        column: token('column'),
        fg: token('fg'),
        muted: token('muted'),
        line: token('line'),
        primary: token('primary'),
        'primary-fg': token('primary-fg'),
        header: token('header'),
        'header-fg': token('header-fg'),
        ai: token('ai'),
        'ai-fg': token('ai-fg'),
        alert: token('alert'),
        'alert-fg': token('alert-fg'),
        focus: token('focus'),
      },
      fontFamily: {
        sans: ['"Segoe UI"', 'system-ui', '-apple-system', 'Roboto', 'Helvetica Neue', 'Arial', 'sans-serif'],
      },
    },
  },
  plugins: [],
};

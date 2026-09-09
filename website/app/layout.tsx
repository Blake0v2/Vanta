import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
  title: 'Vanta Auto Clicker — Precise clicking for Windows',
  description:
    'A fast, focused auto clicker for Windows with simple and advanced controls, configurable hotkeys, limits, and speed randomization.',
  icons: { icon: '/vanta-logo.png' },
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}

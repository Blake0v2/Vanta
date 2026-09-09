'use client';

import { useEffect, useMemo, useState } from 'react';
import {
  ArrowDownToLine,
  CalendarDays,
  Code2,
  ExternalLink,
  GitBranch,
  ShieldCheck,
  Sparkles,
} from 'lucide-react';
import { buttonVariants } from '@/components/ui/button';
import { cn } from '@/lib/utils';

const GITHUB_URL = 'https://github.com/Blake0v2/Vanta';
const RELEASES_URL = `${GITHUB_URL}/releases`;
const FALLBACK_DOWNLOAD = `${GITHUB_URL}/releases/download/v0.1.6/Vanta-0.1.6-x64.msi`;

type ReleaseAsset = {
  name: string;
  browser_download_url: string;
  download_count: number;
};

type GitHubRelease = {
  tag_name: string;
  html_url: string;
  published_at: string;
  assets: ReleaseAsset[];
};

const fallbackRelease: GitHubRelease = {
  tag_name: 'v0.1.6',
  html_url: `${RELEASES_URL}/tag/v0.1.6`,
  published_at: '2026-09-09T03:15:07Z',
  assets: [
    {
      name: 'Vanta-0.1.6-x64.msi',
      browser_download_url: FALLBACK_DOWNLOAD,
      download_count: 0,
    },
  ],
};

function installerFor(release: GitHubRelease) {
  return release.assets.find((asset) => asset.name.toLowerCase().endsWith('-x64.msi'));
}

function readableDate(value: string) {
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  }).format(new Date(value));
}

export default function Home() {
  const [releases, setReleases] = useState<GitHubRelease[]>([fallbackRelease]);

  useEffect(() => {
    const controller = new AbortController();
    fetch('https://api.github.com/repos/Blake0v2/Vanta/releases?per_page=6', {
      signal: controller.signal,
      headers: { Accept: 'application/vnd.github+json' },
    })
      .then((response) => {
        if (!response.ok) throw new Error('Unable to load releases');
        return response.json() as Promise<GitHubRelease[]>;
      })
      .then((items) => {
        if (items.length) setReleases(items);
      })
      .catch(() => undefined);
    return () => controller.abort();
  }, []);

  const latestRelease = releases[0] ?? fallbackRelease;
  const latestInstaller = installerFor(latestRelease);
  const downloadUrl = latestInstaller?.browser_download_url ?? FALLBACK_DOWNLOAD;
  const totalDownloads = useMemo(
    () => releases.flatMap((release) => release.assets).reduce((sum, asset) => sum + asset.download_count, 0),
    [releases],
  );

  return (
    <main>
      <header className="site-header">
        <nav className="nav-pill" aria-label="Primary navigation">
          <a className="brand-mark" href="#home" aria-label="Vanta Auto Clicker home">
            <img src="/vanta-logo.png" alt="" />
          </a>
          <div className="nav-links">
            <a href="#home">Home</a>
            <a href="#features">Features</a>
            <a href="#releases">Releases</a>
            <a href={GITHUB_URL} target="_blank" rel="noreferrer">GitHub</a>
          </div>
          <a className={cn(buttonVariants({ size: 'lg' }), 'nav-download')} href={downloadUrl}>
            Download
          </a>
        </nav>
      </header>

      <section className="hero section-shell" id="home">
        <div className="hero-copy reveal">
          <div className="eyebrow"><Sparkles /> Precise. Lightweight. Free.</div>
          <h1>Vanta Auto Clicker</h1>
          <p className="hero-description">
            A focused Windows auto clicker with a clean simple mode and precise controls when
            you need more.
          </p>
          <div className="hero-actions">
            <a className={cn(buttonVariants({ size: 'lg' }), 'primary-action')} href={downloadUrl}>
              <ArrowDownToLine /> Download for Windows
            </a>
            <a
              className={cn(buttonVariants({ variant: 'outline', size: 'lg' }), 'secondary-action')}
              href={GITHUB_URL}
              target="_blank"
              rel="noreferrer"
            >
              <GitBranch /> View on GitHub
            </a>
          </div>
          <div className="hero-meta">
            <span><ShieldCheck /> No admin required for new installs</span>
            <span>Latest {latestRelease.tag_name}</span>
            {totalDownloads > 0 && <span>{totalDownloads.toLocaleString()} downloads</span>}
          </div>
        </div>

        <div className="hero-visual reveal reveal-delay">
          <div className="app-glow" />
          <div className="window-frame window-frame-main">
            <img src="/vanta-advanced.png" alt="Vanta Auto Clicker advanced controls" />
          </div>
          <div className="window-frame window-frame-float">
            <img src="/vanta-simple.png" alt="Vanta Auto Clicker simple controls" />
          </div>
        </div>
      </section>

      <div id="features">
        <section className="feature-section section-shell">
          <div className="feature-media simple-media">
            <div className="window-frame">
              <img src="/vanta-simple.png" alt="Vanta Auto Clicker simple mode" />
            </div>
          </div>
          <div className="feature-copy">
            <span className="section-number">01</span>
            <h2>Simple Mode</h2>
            <p>
              Set the click rate, hotkey, and mouse button from one compact bar. Nothing gets in
              the way of starting quickly.
            </p>
          </div>
        </section>

        <section className="feature-section feature-reverse section-shell">
          <div className="feature-copy">
            <span className="section-number">02</span>
            <h2>Advanced Controls</h2>
            <p>
              Fine-tune intervals, click type, duty cycle, limits, hotkey behavior, and speed
              randomization without sacrificing clarity.
            </p>
          </div>
          <div className="feature-media advanced-media">
            <div className="window-frame">
              <img src="/vanta-advanced.png" alt="Vanta Auto Clicker advanced tab" />
            </div>
          </div>
        </section>

        <section className="feature-section section-shell">
          <div className="feature-media settings-media">
            <div className="window-frame">
              <img src="/vanta-settings.png" alt="Vanta Auto Clicker settings page" />
            </div>
          </div>
          <div className="feature-copy">
            <span className="section-number">03</span>
            <h2>Updates Built In</h2>
            <p>
              Check for releases, download verified installers inside Vanta, test clicks, and
              reach the project links from one settings page.
            </p>
          </div>
        </section>
      </div>

      <section className="open-source-section section-shell">
        <div className="source-icon"><Code2 /></div>
        <span className="section-number">SOURCE AVAILABLE</span>
        <h2>Built in the open.</h2>
        <p>
          The full Windows app and installer configuration are available to review on GitHub,
          from the click engine to every release build.
        </p>
        <div className="source-actions">
          <a className={cn(buttonVariants({ size: 'lg' }), 'primary-action')} href={GITHUB_URL} target="_blank" rel="noreferrer">
            <GitBranch /> View on GitHub
          </a>
          <a className={cn(buttonVariants({ variant: 'outline', size: 'lg' }), 'secondary-action')} href={RELEASES_URL} target="_blank" rel="noreferrer">
            Browse Releases
          </a>
        </div>
      </section>

      <section className="releases-section section-shell" id="releases">
        <div className="section-heading">
          <div>
            <span className="section-number">DOWNLOADS</span>
            <h2>Recent releases</h2>
          </div>
          <a href={RELEASES_URL} target="_blank" rel="noreferrer">All releases <ExternalLink /></a>
        </div>
        <div className="release-list">
          {releases.slice(0, 3).map((release) => {
            const installer = installerFor(release);
            return (
              <article className="release-card" key={release.tag_name}>
                <div className="release-version">
                  <span>{release.tag_name}</span>
                  {release.tag_name === latestRelease.tag_name && <em>Latest</em>}
                </div>
                <div className="release-date"><CalendarDays /> {readableDate(release.published_at)}</div>
                <div className="release-actions">
                  {installer && (
                    <a href={installer.browser_download_url}>
                      <ArrowDownToLine /> Windows installer
                      {installer.download_count > 0 && <small>{installer.download_count.toLocaleString()} downloads</small>}
                    </a>
                  )}
                  <a href={release.html_url} target="_blank" rel="noreferrer">Release notes <ExternalLink /></a>
                </div>
              </article>
            );
          })}
        </div>
      </section>

      <section className="final-cta section-shell">
        <div>
          <span className="section-number">READY WHEN YOU ARE</span>
          <h2>Start clicking with Vanta.</h2>
        </div>
        <a className={cn(buttonVariants({ size: 'lg' }), 'primary-action')} href={downloadUrl}>
          <ArrowDownToLine /> Download {latestRelease.tag_name}
        </a>
      </section>

      <footer>
        <div className="footer-inner section-shell">
          <div className="footer-brand">
            <img src="/vanta-logo.png" alt="" />
            <div><strong>Vanta Auto Clicker</strong><span>Focused clicking for Windows.</span></div>
          </div>
          <div className="footer-links">
            <strong>Project</strong>
            <a href={GITHUB_URL} target="_blank" rel="noreferrer">GitHub</a>
            <a href={RELEASES_URL} target="_blank" rel="noreferrer">Releases</a>
          </div>
        </div>
        <div className="footer-bottom section-shell">
          <span>© 2026 Vanta</span><span>Source available on GitHub</span>
        </div>
      </footer>
    </main>
  );
}

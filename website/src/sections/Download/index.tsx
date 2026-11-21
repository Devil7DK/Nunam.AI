import './styles.css';

import type { FC } from 'react';
import { Link } from 'react-router';

import { RELEASES_URL } from '../../Constants';

interface Package {
  os: string;
  format: string;
  detail: string;
}

const PACKAGES: Package[] = [
  {
    os: 'Windows, 64-bit',
    format: '.msi',
    detail:
      'Installs the indexer as a Windows service, with a Start-menu shortcut to configure it.',
  },
  {
    os: 'Debian and Ubuntu',
    format: '.deb',
    detail:
      'A sandboxed systemd service with its settings in /etc/nunam-indexer.',
  },
  {
    os: 'Fedora',
    format: '.rpm',
    detail: 'The same service and layout as the .deb.',
  },
];

export const Download: FC = () => (
  <section className='section download' id='download'>
    <div className='inner'>
      <header className='head'>
        <p className='eyebrow'>Download</p>
        <h2 className='title'>Get Nunam</h2>
        <p className='subtitle'>
          Start with the server, then install the indexer on each computer whose
          files you want to find. The installers are self-contained and need no
          .NET runtime. macOS is coming.
        </p>
      </header>

      <div className='download-grid'>
        <article className='card server'>
          <p className='step'>1 · The server</p>
          <h3 className='title'>Docker Compose</h3>
          <p className='text'>
            Postgres, the API, the web UI and the AI worker, on any Linux box or
            VM with Docker. Point it at an Ollama server with a GPU.
          </p>
          <Link className='button button-primary' to='/docs/deployment'>
            Deployment guide
          </Link>
        </article>

        <article className='card'>
          <p className='step'>2 · The indexer</p>
          <ul className='packages'>
            {PACKAGES.map((p) => (
              <li key={p.format}>
                <span className='format'>{p.format}</span>
                <div>
                  <p className='os'>{p.os}</p>
                  <p className='detail'>{p.detail}</p>
                </div>
              </li>
            ))}
          </ul>
          <div className='actions'>
            <a
              className='button button-primary'
              href={RELEASES_URL}
              target='_blank'
              rel='noreferrer'
            >
              Latest release
            </a>
            <Link className='button button-ghost' to='/docs/adding-a-machine'>
              Install guide
            </Link>
          </div>
        </article>
      </div>
    </div>
  </section>
);

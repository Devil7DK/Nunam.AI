import './styles.css';

import type { FC } from 'react';
import { Link } from 'react-router';

const POINTS = [
  {
    title: 'Your files stay where they are',
    text: 'The indexer never copies a file. The server gets its path, details, extracted text and one preview image of at most 1568 pixels.',
  },
  {
    title: 'AI on your own hardware',
    text: 'Descriptions and vectors come from models running on your own GPU through Ollama. No file, preview or query is sent to a cloud service.',
  },
  {
    title: 'One token per machine',
    text: 'Each indexer signs in with its own token, which reads and writes only its own machine’s files. Remove the machine and the token stops working. The database is never on the network.',
  },
  {
    title: 'Least privilege on each computer',
    text: 'The indexer runs as its own service account, a virtual account on Windows and a sandboxed systemd user on Linux, with read access to the folders you choose.',
  },
];

export const Private: FC = () => (
  <section className='section private' id='private'>
    <div className='inner'>
      <header className='head'>
        <p className='eyebrow'>Privacy</p>
        <h2 className='title'>Nothing leaves your network</h2>
        <p className='subtitle'>
          Your documents and photos are the most personal data you have. Nunam
          keeps all of it, and every model that reads it, at home.
        </p>
      </header>

      <div className='points'>
        {POINTS.map((p) => (
          <article key={p.title} className='point'>
            <h3 className='title'>{p.title}</h3>
            <p className='text'>{p.text}</p>
          </article>
        ))}
      </div>

      <p className='more'>
        <Link to='/docs/overview#what-leaves-each-computer'>
          What the server stores, in detail
        </Link>
      </p>
    </div>
  </section>
);

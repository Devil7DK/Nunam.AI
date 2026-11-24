import './styles.css';

import type { FC } from 'react';

interface Step {
  where: string;
  name: string;
  detail: string;
}

const STEPS: Step[] = [
  {
    where: 'On each computer',
    name: 'Indexer',
    detail:
      'Scans and watches your folders, reads each file where it is, and sends the text, details and a small preview to the server. A Windows service or a systemd unit, installed with one command from the web UI.',
  },
  {
    where: 'On the server',
    name: 'Postgres + pgvector',
    detail:
      'Holds the index: text, previews and vectors. The API is the only way in: the web UI searches through it, indexers send files to it, and the AI worker fills in descriptions and vectors. Runs in Docker.',
  },
  {
    where: 'On your GPU',
    name: 'Ollama',
    detail:
      'Runs a vision model that describes images and an embedding model that turns text into vectors. Any Ollama or OpenAI-compatible server on your network.',
  },
];

const NOTES = [
  {
    title: 'Keeps working without the GPU',
    text: 'If the model server is off, search falls back to keywords and new files wait in the queue until it is back.',
  },
  {
    title: 'Small server',
    text: '2 CPU cores and 4 GB of memory are enough. The AI work happens on the GPU machine.',
  },
];

export const HowItWorks: FC = () => (
  <section className='section how' id='how-it-works'>
    <div className='inner'>
      <header className='head'>
        <p className='eyebrow'>How it works</p>
        <h2 className='title'>Three parts, all on your network</h2>
        <p className='subtitle'>
          Files are read where they are. Only what is needed to find them goes
          to the server.
        </p>
      </header>

      <ol className='steps'>
        {STEPS.map((step, i) => (
          <li key={step.name} className='card'>
            <p className='where'>
              <span className='n'>{i + 1}</span>
              {step.where}
            </p>
            <h3 className='name'>{step.name}</h3>
            <p className='detail'>{step.detail}</p>
          </li>
        ))}
      </ol>

      <div className='notes'>
        {NOTES.map((note) => (
          <div key={note.title} className='note'>
            <h3 className='title'>{note.title}</h3>
            <p className='text'>{note.text}</p>
          </div>
        ))}
      </div>
    </div>
  </section>
);

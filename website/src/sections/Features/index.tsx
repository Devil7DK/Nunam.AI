import './styles.css';

import type { FC, ReactNode } from 'react';

import {
  FileTextIcon,
  ImageIcon,
  MonitorIcon,
  SlidersIcon,
  SparkleIcon,
  UsersIcon,
} from '../../components/Icons';

interface Feature {
  icon: ReactNode;
  title: string;
  description: string;
}

const FEATURES: Feature[] = [
  {
    icon: <SparkleIcon />,
    title: 'Search by meaning',
    description:
      'Every file becomes a vector, so “warranty card for the fridge” finds the scan even when it never says warranty. Keyword matches are merged in, with highlighted snippets.',
  },
  {
    icon: <FileTextIcon />,
    title: 'Reads inside your files',
    description:
      'Text from PDFs, Word, Excel and PowerPoint files and plain text, OCR of scanned pages and images, tags of songs, and details of photos and videos.',
  },
  {
    icon: <ImageIcon />,
    title: 'Knows what a photo shows',
    description:
      'A vision model on your own GPU writes a short description of each photo, scanned page and video frame, and that description is searchable too.',
  },
  {
    icon: <MonitorIcon />,
    title: 'Every computer, one search',
    description:
      'An indexer on each Windows and Linux machine watches the folders you pick, or the whole drive without the system folders, and keeps the index current as files change.',
  },
  {
    icon: <UsersIcon />,
    title: 'Accounts and sharing',
    description:
      'Everyone signs in with their own account and sees only the machines shared with them. The owner of a machine decides who can search it.',
  },
  {
    icon: <SlidersIcon />,
    title: 'Manage from the browser',
    description:
      'See each indexer’s status, queue and logs, and, where a machine allows it, pause it, rescan it or change its folders from the Machines page.',
  },
];

export const Features: FC = () => (
  <section className='section features' id='features'>
    <div className='inner'>
      <header className='head'>
        <p className='eyebrow'>What it does</p>
        <h2 className='title'>Search that understands your files</h2>
        <p className='subtitle'>
          File names only get you so far. Nunam searches what is written in a
          document and what is shown in a photo.
        </p>
      </header>

      <div className='features-grid'>
        {FEATURES.map((feature) => (
          <article key={feature.title} className='card'>
            <span className='icon'>{feature.icon}</span>
            <h3 className='title'>{feature.title}</h3>
            <p className='desc'>{feature.description}</p>
          </article>
        ))}
      </div>
    </div>
  </section>
);

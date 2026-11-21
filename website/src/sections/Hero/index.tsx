import './styles.css';

import type { FC, ReactNode } from 'react';
import { Link } from 'react-router';

import {
  FileTextIcon,
  ImageIcon,
  SearchIcon,
  VideoIcon,
} from '../../components/Icons';

interface Result {
  icon: ReactNode;
  name: string;
  machine: string;
  folder: string;
  snippet: ReactNode;
}

const RESULTS: Result[] = [
  {
    icon: <ImageIcon />,
    name: 'IMG_20250314_0912.jpg',
    machine: 'laptop',
    folder: 'Pictures/Phone/2025',
    snippet: (
      <>
        A photo of a printed <mark>electricity bill</mark> on a table. The{' '}
        <mark>due date</mark> reads 28 March 2025.
      </>
    ),
  },
  {
    icon: <FileTextIcon />,
    name: 'scan0041.pdf',
    machine: 'desktop',
    folder: 'Documents/Scans',
    snippet: (
      <>
        Consumer no. 0412… amount payable ₹2,140, <mark>due by</mark> 28-03-2025
      </>
    ),
  },
  {
    icon: <VideoIcon />,
    name: 'meter-reading.mp4',
    machine: 'laptop',
    folder: 'Videos',
    snippet: <>A close-up of an electricity meter display on a wall.</>,
  },
];

const SearchMock: FC = () => (
  <div className='search-mock' aria-hidden='true'>
    <div className='box'>
      <SearchIcon />
      <span className='query'>when is the electricity bill due</span>
      <span className='mode'>Hybrid</span>
    </div>
    <ul className='results'>
      {RESULTS.map((r) => (
        <li key={r.name}>
          <span className='type'>{r.icon}</span>
          <div className='body'>
            <p className='name'>
              {r.name}
              <span className='machine'>{r.machine}</span>
            </p>
            <p className='folder'>{r.folder}</p>
            <p className='snippet'>{r.snippet}</p>
          </div>
        </li>
      ))}
    </ul>
  </div>
);

export const Hero: FC = () => (
  <section className='hero' id='top'>
    <div className='content'>
      <p className='eyebrow'>Self-hosted file search</p>
      <h1 className='title'>
        Find any file in your home by what&rsquo;s{' '}
        <span className='accent'>in</span> it.
      </h1>
      <p className='lead'>
        Nunam indexes every computer on your network into one search box. It
        reads documents, OCRs scans, and has a local AI model describe your
        photos, so you can search for a bill, a receipt or a beach photo the way
        you&rsquo;d describe it.
      </p>
      <div className='actions'>
        <a className='button button-primary' href='#download'>
          Get Nunam
        </a>
        <Link className='button button-ghost' to='/docs'>
          Read the docs
        </Link>
      </div>
    </div>

    <div className='visual'>
      <SearchMock />
    </div>
  </section>
);

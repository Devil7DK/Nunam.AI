import type { FC } from 'react';

import { Download, Features, Hero, HowItWorks, Private } from '../../sections';

export const Home: FC = () => (
  <main className='app'>
    <Hero />
    <Features />
    <HowItWorks />
    <Private />
    <Download />
  </main>
);

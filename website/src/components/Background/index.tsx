import './styles.css';

import type { FC } from 'react';
import DrifterStars from 'react-drifter-stars';

export const Background: FC = () => {
  return (
    <div className='background' aria-hidden='true'>
      <DrifterStars
        className='background-stars'
        colorVar='--star-rgb'
        pixelRatio
        particle={{ count: 70, sizeBase: 0.5, sizeMultiplier: 0.4 }}
        flare={{ render: true, count: 3 }}
        links={{ render: true, opacity: 0.1 }}
        glare={{ render: true, opacityMultiplier: 0.04 }}
        motion={{ enabled: true, randomMotion: true, ratio: 0.03 }}
      />
    </div>
  );
};

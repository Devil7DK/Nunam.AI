import './styles.css';

import type { FC } from 'react';
import { Link } from 'react-router';

import logo from '../../assets/logo.svg';
import { GITHUB_URL, SITE_NAME } from '../../Constants';

const YEAR = new Date().getFullYear();

export const Footer: FC = () => {
  return (
    <footer className='footer'>
      <div className='inner'>
        <div className='brand'>
          <Link className='logo' to='/#top' aria-label={`${SITE_NAME} home`}>
            <img src={logo} width='28' height='28' alt='' />
            <span>{SITE_NAME}</span>
          </Link>
          <p className='tag'>
            Search every file in your home, on your own hardware.
          </p>
        </div>

        <nav className='links' aria-label='Footer'>
          <Link to='/#features'>Features</Link>
          <Link to='/#how-it-works'>How it works</Link>
          <Link to='/#download'>Download</Link>
          <Link to='/docs'>Docs</Link>
          <a href={GITHUB_URL} target='_blank' rel='noreferrer'>
            GitHub
          </a>
        </nav>
      </div>

      <div className='bottom'>
        <p>&copy; {YEAR} Dineshkumar T.</p>
      </div>
    </footer>
  );
};

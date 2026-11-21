import './styles.css';

import { animate, motion, useMotionValue } from 'motion/react';
import type { FC } from 'react';
import { useLayoutEffect, useRef } from 'react';
import { Link, useLocation } from 'react-router';

import logo from '../../assets/logo.svg';
import { GITHUB_URL, SITE_NAME } from '../../Constants';
import { ThemeToggle } from '../ThemeToggle';
import { useActiveSection } from './useActiveSection';

const SECTION_LINKS = [
  { id: 'top', href: '/#top', label: 'Home' },
  { id: 'features', href: '/#features', label: 'Features' },
  { id: 'how-it-works', href: '/#how-it-works', label: 'How it works' },
  { id: 'private', href: '/#private', label: 'Privacy' },
  { id: 'download', href: '/#download', label: 'Download' },
];

const ROUTE_LINKS = [{ id: 'docs', to: '/docs', label: 'Docs' }];

const SECTION_IDS = SECTION_LINKS.map((link) => link.id);

const SPRING = { damping: 30, stiffness: 380, type: 'spring' } as const;

const GithubIcon: FC = () => {
  return (
    <svg
      viewBox='0 0 24 24'
      width='20'
      height='20'
      fill='currentColor'
      aria-hidden='true'
    >
      <path d='M12 .5C5.37.5 0 5.87 0 12.5c0 5.3 3.44 9.8 8.21 11.39.6.11.82-.26.82-.58v-2.03c-3.34.73-4.04-1.61-4.04-1.61-.55-1.39-1.34-1.76-1.34-1.76-1.09-.75.08-.73.08-.73 1.2.08 1.84 1.24 1.84 1.24 1.07 1.83 2.81 1.3 3.5.99.11-.78.42-1.3.76-1.6-2.67-.3-5.47-1.33-5.47-5.93 0-1.31.47-2.38 1.24-3.22-.13-.3-.54-1.52.11-3.18 0 0 1.01-.32 3.3 1.23a11.5 11.5 0 0 1 6.01 0c2.29-1.55 3.3-1.23 3.3-1.23.65 1.66.24 2.88.12 3.18.77.84 1.23 1.91 1.23 3.22 0 4.61-2.81 5.63-5.49 5.93.43.37.82 1.1.82 2.22v3.29c0 .32.22.7.83.58A12.01 12.01 0 0 0 24 12.5C24 5.87 18.63.5 12 .5Z' />
    </svg>
  );
};

export const Nav: FC = () => {
  const { pathname } = useLocation();
  const activeSection = useActiveSection(SECTION_IDS);
  const active =
    ROUTE_LINKS.find((link) => pathname.startsWith(link.to))?.id ??
    activeSection;

  const linksRef = useRef<HTMLElement>(null);
  const x = useMotionValue(0);
  const width = useMotionValue(0);
  const opacity = useMotionValue(0);

  useLayoutEffect(() => {
    const target = linksRef.current?.querySelector<HTMLElement>(
      `[data-nav='${active}']`,
    );
    if (!target || target.offsetParent === null) {
      opacity.set(0);
      return;
    }

    if (opacity.get() === 0) {
      x.set(target.offsetLeft);
      width.set(target.offsetWidth);
      opacity.set(1);
      return;
    }

    animate(x, target.offsetLeft, SPRING);
    animate(width, target.offsetWidth, SPRING);
  }, [active, pathname, x, width, opacity]);

  return (
    <header className='nav'>
      <div className='inner'>
        <Link className='brand' to='/#top' aria-label={`${SITE_NAME} home`}>
          <img className='logo' src={logo} width='32' height='32' alt='' />
          <span className='wordmark'>
            Nunam<span className='tld'>.AI</span>
          </span>
        </Link>

        <nav className='links' aria-label='Primary' ref={linksRef}>
          {SECTION_LINKS.map((link) => (
            <Link
              key={link.href}
              to={link.href}
              data-nav={link.id}
              className={link.id === active ? 'is-active' : undefined}
            >
              {link.label}
            </Link>
          ))}
          {ROUTE_LINKS.map((link) => (
            <Link
              key={link.to}
              to={link.to}
              data-nav={link.id}
              className={link.id === active ? 'is-active' : undefined}
            >
              {link.label}
            </Link>
          ))}
          <motion.span className='indicator' style={{ opacity, width, x }} />
        </nav>

        <div className='actions'>
          <a
            className='icon-link'
            href={GITHUB_URL}
            target='_blank'
            rel='noreferrer'
            aria-label={`${SITE_NAME} on GitHub`}
          >
            <GithubIcon />
          </a>
          <ThemeToggle />
        </div>
      </div>
    </header>
  );
};
